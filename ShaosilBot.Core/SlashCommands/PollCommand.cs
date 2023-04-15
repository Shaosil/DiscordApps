using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.SlashCommands
{
	public class PollCommand : BaseCommand
	{
		public const string SelectMenuID = "PollCommandSelectMenu";
		public readonly ManualResetEvent SelectMenuReadySignal = new(true);

		private readonly IQuartzProvider _quartzProvider;

		public PollCommand(ILogger<BaseCommand> logger, IQuartzProvider quartzProvider) : base(logger)
		{
			_quartzProvider = quartzProvider;
		}

		public override string CommandName => "poll";

		public override string HelpSummary => "Lets people vote on a question. Can be a simple yes/no or up to 20 choices!";

		public override string HelpDetails => @$"/{CommandName} (string question) [string choice1 - choice20, string minute-limit]

Passing only a question will provide 'Aye' and 'Nay' choices by default. Otherwise, provide 2+ choices for a custom list.

REQUIRED ARGS:
* question:
    The text on which to vote.

OPTIONAL ARGS:
* choice1 - choice20:
    If one is provided, at least two must be.

* minute-limit:
	If provided, will end the poll after the specified minutes. Must be between 1 and 180.";

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
					}
				}
				.Concat(choices)
				.Concat(new[]
				{
					new SlashCommandOptionBuilder
					{
						Name = "minute-limit",
						Type = ApplicationCommandOptionType.Integer,
						Description = "Optional time limit (in minutes)",
						MinValue = 1, MaxValue = 180 // 3 hours max
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
			// TODO: REWRITE!
			return Task.FromResult(cmdWrapper.Respond("Work in progress! Minor rewrite coming that will improve how polling works. Poke Shaosil for details", ephemeral: true));

			// Validation
			string? questionText = cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "question")?.Value as string;
			if (string.IsNullOrWhiteSpace(questionText))
				return Task.FromResult(cmdWrapper.Respond("You must provide a question!", ephemeral: true));

			// Take all distinct trimmed choice values
			var choices = cmdWrapper.Command.Data.Options.Where(o => o.Name.StartsWith("choice")).Select(c => c.Value?.ToString()?.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (choices.Count == 1)
				return Task.FromResult(cmdWrapper.Respond("You may either provide no choices for a default yes/no poll, or 2+ choices for a custom list.", ephemeral: true));
			else if (choices.Count == 0)
				choices.AddRange(new[] { "Aye!", "Nay!" });

			// Build the initial list of options, suffixing each with (0) votes
			int curIndex = 1;
			var choiceTexts = choices.Select(c => $"{$"{curIndex++}".PadLeft(choices.Count.ToString().Length)}: {c} (0)").ToList();

			// Get the time limit, if any
			_ = int.TryParse(cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "minute-limit")?.Value.ToString() ?? "0", out var minuteLimit);
			if (minuteLimit > 0)
			{
				var targetTime = DateTimeOffset.Now.AddMinutes(minuteLimit);
				choiceTexts.Add($"\nPoll will end <t:{targetTime.AddSeconds(1).ToUnixTimeSeconds()}:R>!");

				// Set a task to run AFTER the response is sent so we can grab it and store the message ID
				_ = Task.Run(async () =>
				{
					int tries = 0;
					IUserMessage? response = null;
					while (tries++ < 3 && response == null) response = await cmdWrapper.Command.GetOriginalResponseAsync();
					if (response == null) return;

					_quartzProvider.SchedulePollEnd(cmdWrapper.Command.ChannelId!.Value, response.Id, targetTime);
				});
			}

			bool.TryParse(cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "multiselect")?.Value.ToString(), out var multiselect);

			// Build the select menu
			var selectMenu = new ComponentBuilder().WithSelectMenu(SelectMenuID,
				choices.Select(c => new SelectMenuOptionBuilder(c, c)).ToList(),
				"Cast your vote!",
				minValues: 0,
				maxValues: multiselect ? choices.Count : 1);

			var embed = new EmbedBuilder() { Title = $"📊 **{questionText.Trim()}**", Description = string.Join('\n', choiceTexts), Color = new Color(0x7c0089) };
			return Task.FromResult(cmdWrapper.Respond(embed: embed.Build(), components: selectMenu.Build()));
		}

		public string HandleVote(RestMessageComponent messageComponent)
		{
			// Run all of the following code on a new thread so we can immediately defer a response
			_ = Task.Run(async () =>
			{
				try
				{
					// Wait here and lock so we only handle one at a time
					SelectMenuReadySignal.WaitOne(10000);
					SelectMenuReadySignal.Reset();

					var originalEmbed = messageComponent.Message.Embeds.First();

					// To avoid a race condition, always re-load the message and make sure it currently has selections
					var loadedMessage = await messageComponent.Channel.GetMessageAsync(messageComponent.Message.Id);
					if (!loadedMessage.Components.Any()) return;

					// If there is any extra description after the choices, capture it here
					string endingDesc = Regex.Match(originalEmbed.Description, ".+(^Poll .+)", RegexOptions.Multiline).Groups[1].Value;

					// Get current votes and add 1 to all supplied values
					var currentVotes = ParseVotes(originalEmbed.Description, false);
					foreach (string vote in messageComponent.Data.Values)
					{
						currentVotes.First(v => v.description == vote).Votes++;
					}

					// Update description and message
					string newDesc = $"{GetDescriptionFromParsedVotes(currentVotes)}{endingDesc}";
					var modifiedEmbed = new EmbedBuilder()
					{
						Title = originalEmbed.Title,
						Color = originalEmbed.Color,
						Description = newDesc
					};
					await messageComponent.ModifyOriginalResponseAsync(m =>
					{
						m.Content = messageComponent.Message.Content;
						m.Embed = modifiedEmbed.Build();
						m.Components = ComponentBuilder.FromComponents(messageComponent.Message.Components).Build();
					});

					// Ready for the next thread
					SelectMenuReadySignal.Set();
				}
				catch (Exception ex)
				{
					var i = 0;
				}
			});

			return messageComponent.Defer(true);
		}

		public string GetDescriptionFromParsedVotes(List<VoteOption> votes)
		{
			return string.Join("\n", votes.Select(v => $"{$"{v.order}".PadLeft(votes.Count)}: {v.description} ({v.Votes})"));
		}

		public record VoteOption(int order, string description)
		{
			public int Votes { get; set; }
			public VoteOption(int ord, string desc, int votes) : this(ord, desc)
			{
				Votes = votes;
			}
		}

		public List<VoteOption> ParseVotes(string description, bool highlightWinners)
		{
			var parsedVotes = new List<VoteOption>();
			var matches = Regex.Matches(description, @"^\s*(\d+): (.+) \((\d+)\)$", RegexOptions.Multiline);
			for (int i = 0; i < matches.Count; i++)
			{
				var match = matches[i];
				parsedVotes.Add(new VoteOption(int.Parse(match.Groups[1].Value), match.Groups[2].Value, int.Parse(match.Groups[3].Value)));
			}

			return parsedVotes;
		}
	}
}