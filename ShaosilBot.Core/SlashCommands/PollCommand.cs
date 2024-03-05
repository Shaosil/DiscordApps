using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.SQLite;
using ShaosilBot.Core.Providers;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.SlashCommands
{
	public class PollCommand : BaseCommand
	{
		public const string SelectMenuID = "PollCommandSelectMenu";
		public readonly ManualResetEventSlim SelectMenuReadySignal = new(true);

		private readonly IQuartzProvider _quartzProvider;
		private readonly ISQLiteProvider _sqliteProvider;

		public PollCommand(ILogger<BaseCommand> logger, IQuartzProvider quartzProvider, ISQLiteProvider sqliteProvider) : base(logger)
		{
			_quartzProvider = quartzProvider;
			_sqliteProvider = sqliteProvider;
		}

		public override string CommandName => "poll";

		public override string HelpSummary => "Lets people vote on a question. Can be a simple yes/no or up to 20 choices!";

		public override string HelpDetails => @$"/{CommandName} (string question) [int minute-limit, string choice1 - choice20, bool blind, bool multiselect]

Passing only a question will provide 'Aye' and 'Nay' choices by default. Otherwise, provide 2+ choices for a custom list.

REQUIRED ARGS:
* question:
    The text on which to vote.

OPTIONAL ARGS:
* minute-limit:
	If provided, will end the poll after the specified minutes. Must be between 1 and 180.

* choice1 - choice20:
    If one is provided, at least two must be.

* blind
	If true, will NOT display voting results until the poll expires. minute-limit must also be provided for blind polls.

* multiselect:
	Allows more than one selection for this poll.";

		public override SlashCommandProperties BuildCommand()
		{
			var choices = new List<SlashCommandOptionBuilder>();
			for (int i = 1; i <= 20; i++)
				choices.Add(new SlashCommandOptionBuilder
				{
					Name = $"choice{i}",
					Description = HelpSummary,
					Type = ApplicationCommandOptionType.String
				});

			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new[]
				{
					new SlashCommandOptionBuilder
					{
						Name = "question",
						Type = ApplicationCommandOptionType.String,
						Description = "What do you want people to vote on?",
						IsRequired = true
					},
					new SlashCommandOptionBuilder
					{
						Name = "minute-limit",
						Type = ApplicationCommandOptionType.Integer,
						Description = "Optional time limit (in minutes)",
						MinValue = 1, MaxValue = 180 // 3 hours max
					}
				}
				.Concat(choices)
				.Concat(new[]
				{
					new SlashCommandOptionBuilder
					{
						Name = "blind",
						Type = ApplicationCommandOptionType.Boolean,
						Description = "Hide voting results until the end."
					},
					new SlashCommandOptionBuilder
					{
						Name = "multiselect",
						Type = ApplicationCommandOptionType.Boolean,
						Description = "Whether or not to allow multiple votes"
					}
				}).ToList()
			}.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			// Validation
			string? questionText = cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "question")?.Value as string;
			if (string.IsNullOrWhiteSpace(questionText))
				return Task.FromResult(cmdWrapper.Respond("You must provide a question!", ephemeral: true));

			// Take all distinct trimmed choice values (stripping out custom emojis)
			var choices = cmdWrapper.Command.Data.Options.Where(o => o.Name.StartsWith("choice") && o.Value != null)
				.Select(c => Regex.Replace(c.Value.ToString()!.Trim(), @"<a?:.+:\d+>", string.Empty))
				.Where(c => !string.IsNullOrWhiteSpace(c))
				.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (choices.Count == 1)
				return Task.FromResult(cmdWrapper.Respond("If specifying choices, you must provide 2+ unique ones. Custom emojis are not supported.", ephemeral: true));
			else if (choices.Count == 0)
				choices.AddRange(new[] { "Aye!", "Nay!" });

			bool.TryParse(cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "blind")?.Value.ToString(), out var isBlind);
			bool.TryParse(cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "multiselect")?.Value.ToString(), out var multiselect);

			// Get the time limit. If one was not provided, use a week but do not show it
			var minuteLimitVal = cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "minute-limit")?.Value.ToString();
			int.TryParse(minuteLimitVal ?? $"{new TimeSpan(7, 0, 0, 0).TotalMinutes}", out var minuteLimit);
			var targetTime = DateTimeOffset.Now.AddMinutes(minuteLimit);

			// Validate blind polls have a minute limit
			if (isBlind && string.IsNullOrEmpty(minuteLimitVal))
			{
				return Task.FromResult(cmdWrapper.Respond("Blind polls are only supported with time limits! Please also specify the `minute-limit` option.", ephemeral: true));
			}

			// Build the select menu
			var selectMenu = new ComponentBuilder().WithSelectMenu(SelectMenuID,
				choices.Select(c => new SelectMenuOptionBuilder(c, c)).ToList(),
				$"Cast your vote{(multiselect ? "s" : string.Empty)}!",
				minValues: 0,
				maxValues: multiselect ? choices.Count : 1,
				defaultValues: []);

			// Set a task to run AFTER the response is sent so we can grab it and store the message ID
			return cmdWrapper.DeferWithCode(async () =>
			{
				int tries = 0;
				IUserMessage? response = null;
				do
				{
					await Task.Delay(250);
					response = await cmdWrapper.Command.GetOriginalResponseAsync();
				} while (tries++ < 3 && response == null);

				if (response == null)
				{
					await cmdWrapper.Command.FollowupAsync("*Error: could not load original response!*");
					return;
				}

				// Store the poll in the DB and schedule the poll end job
				var newPoll = new PollMessage { MessageID = response.Id, IsBlind = isBlind, Text = questionText.Trim(), ExpiresAt = targetTime };
				var newChoices = choices.Select((c, i) => new PollChoice { PollMessageID = response.Id, SortOrder = i, Text = c! }).ToList();
				_sqliteProvider.UpsertDataRecords(newPoll);
				_sqliteProvider.UpsertDataRecords(newChoices.ToArray());
				_quartzProvider.SchedulePollEnd(cmdWrapper.Command.ChannelId!.Value, response.Id, targetTime);

				var embed = new EmbedBuilder() { Title = $"📊 **{questionText.Trim()}**", Description = GetPollDescription(newPoll), Color = new Color(0x7c0089) };
				response = await cmdWrapper.Command.FollowupAsync(embed: embed.Build(), components: (multiselect ? null : selectMenu.Build()));

				// If this is multiselect, handle voting in a separate message so user selections are persisted
				if (multiselect)
				{
					response = await response.Channel.SendMessageAsync("Vote here!", components: selectMenu.Build(), messageReference: new MessageReference(response.Id));
					newPoll.SelectMenuMessageID = response.Id;
					_sqliteProvider.UpsertDataRecords(newPoll);
				}
			});
		}

		public string HandleVote(RestMessageComponent messageComponent)
		{
			// Run all of the following code on a new thread so we can immediately defer a response
			_ = Task.Run(async () =>
			{
				try
				{
					// Wait here and lock so we only handle one at a time
					SelectMenuReadySignal.Wait(10000);
					SelectMenuReadySignal.Reset();

					// To avoid a race condition, always re-load the message and make sure it currently has selections
					var loadedMessage = await messageComponent.Channel.GetMessageAsync(messageComponent.Message.Id);
					if (!loadedMessage?.Components.Any() ?? false) return;

					// If this is referencing the original message, load that as well
					var referenceMessage = messageComponent.Message.Reference != null ? await messageComponent.Channel.GetMessageAsync(messageComponent.Message.Reference.MessageId.Value) : null;
					var originalEmbed = (referenceMessage ?? messageComponent.Message).Embeds.First();

					// Validate poll still exists in case of race conditions
					ulong pollMessageID = referenceMessage?.Id ?? messageComponent.Message.Id;
					var poll = _sqliteProvider.GetDataRecord<PollMessage, ulong>(pollMessageID);
					if (poll == null)
					{
						return;
					}

					// Delete current user votes for this choice
					var curChoices = poll.PollChoices.SelectMany(c => c.PollUserVotes).Where(v => v.UserID == messageComponent.User.Id).ToArray();
					_sqliteProvider.DeleteDataRecords(curChoices);

					// Insert new user votes if there are any
					List<PollUserVote> newVotes = new();
					foreach (var vote in messageComponent.Data.Values)
					{
						var matchingChoice = poll.PollChoices.First(c => c.Text == vote);
						newVotes.Add(new PollUserVote { PollChoiceID = matchingChoice.ID, UserID = messageComponent.User.Id });
					}
					if (newVotes.Any())
					{
						_sqliteProvider.UpsertDataRecords(newVotes.ToArray());
					}

					// Update description and message if the poll is not blind
					if (!poll.IsBlind)
					{
						var modifiedEmbed = new EmbedBuilder()
						{
							Title = originalEmbed.Title,
							Color = originalEmbed.Color,
							Description = GetPollDescription(poll)
						}.Build();

						// Update the poll or the voting message's referenced message (for multiselect)
						if (referenceMessage != null)
						{
							await messageComponent.Channel.ModifyMessageAsync(referenceMessage.Id, m =>
							{
								m.Content = referenceMessage.Content;
								m.Embed = modifiedEmbed;
							});
						}
						else
						{
							await messageComponent.ModifyOriginalResponseAsync(m =>
							{
								m.Content = messageComponent.Message.Content;
								m.Embed = modifiedEmbed;

								// I have to rebuild it this way because of a null value bug in Discord.NET's SelectMenuBuilder :(
								var oldSelectMenu = messageComponent.Message.Components.First().Components.First() as SelectMenuComponent;
								var curSelectMenu = new SelectMenuBuilder(oldSelectMenu);
								curSelectMenu.CustomId = oldSelectMenu!.CustomId;
								m.Components = new ComponentBuilder().WithSelectMenu(curSelectMenu).Build();
							});
						}
					}
				}
				finally
				{
					// Ready for the next thread
					SelectMenuReadySignal.Set();
				}
			});

			return messageComponent.Defer(true);
		}

		public string GetPollDescription(PollMessage poll, bool pollEnded = false)
		{
			var descSb = new StringBuilder();

			bool showResults = pollEnded || !poll.IsBlind;

			// Choices
			foreach (var choice in poll.PollChoices)
			{
				descSb.AppendLine($"{choice.SortOrder + 1}: {choice.Text}{(showResults ? $" ({choice.PollUserVotes.Count})" : string.Empty)}");
			}

			// Voters
			if (showResults && poll.PollChoices.Sum(c => c.PollUserVotes.Count) > 0)
			{
				descSb.AppendLine();
				foreach (var choice in poll.PollChoices.Where(c => c.PollUserVotes.Any()).OrderByDescending(c => c.PollUserVotes.Count))
				{
					descSb.AppendLine($"*{choice.SortOrder + 1}: {string.Join(", ", choice.PollUserVotes.Select(v => $"<@{v.UserID}>"))}*");
				}
			}

			// Ending description
			descSb.AppendLine();
			if (pollEnded)
			{
				descSb.AppendLine("Poll has ended and the results are in!");
			}
			else
			{
				if (poll.IsBlind)
				{
					descSb.AppendLine("Blind Poll! Results will be revealed when it ends.");
					descSb.AppendLine();
				}
				descSb.AppendLine($"Poll will end <t:{poll.ExpiresAt.AddSeconds(1).ToUnixTimeSeconds()}:R>!");
			}

			return descSb.ToString();
		}
	}
}