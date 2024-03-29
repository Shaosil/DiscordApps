﻿using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using System.Text;

namespace ShaosilBot.Core.SlashCommands
{
	public class TimeoutCommand : BaseCommand
	{
		private const string OptOutsFile = "TimeoutOptOuts.json";
		private readonly IGuildHelper _guildHelper;
		private readonly IFileAccessHelper _fileAccessHelper;
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly IDiscordSocketClientProvider _socketClientProvider;

		public TimeoutCommand(ILogger<BaseCommand> logger,
			IGuildHelper guildHelper,
			IFileAccessHelper fileAccessHelper,
			IDiscordRestClientProvider restClientProvider,
			IDiscordSocketClientProvider socketClientProvider) : base(logger)
		{
			_guildHelper = guildHelper;
			_fileAccessHelper = fileAccessHelper;
			_restClientProvider = restClientProvider;
			_socketClientProvider = socketClientProvider;
		}

		public override string CommandName => "timeout-roulette";

		public override string HelpSummary => "Has a slight chance of timing out your target for X seconds. Times YOU out for DOUBLE if it fails.";

		public override string HelpDetails => @$"/{CommandName} (string user) [int duration=1] [string reason] [bool opt-out]

Uses a quadratic formula to determine the chance of successfully timing out the target user for the specified duration. If the chance fails, you will be timed out for double that time.
As a result, 30 seconds has a 50% chance of success, 5 minutes only has a 15% chance, and 10 minutes has a 5%.

REQUIRED ARGS:
* user
    The target user you wish to timeout. Note: It is not possible to timeout server administrators.

OPTIONAL ARGS:
* duration (default = 1 minute)
    How many seconds the target user will be timed out for. Be careful, since you will be timed out for double this length if unsuccessful.

* reason
	The logic behind why you want to time out the specified user.

* opt-out 
    If specified, will add or remove the target user argument from the opt-out list. Target user must be yourself or a lower ranked user.";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new List<SlashCommandOptionBuilder>
				{
					new SlashCommandOptionBuilder { Name = "user", IsRequired = true, Type = ApplicationCommandOptionType.User, Description = "Who are you trying to time out?" },
					new SlashCommandOptionBuilder
					{
						Name = "duration",
						Type = ApplicationCommandOptionType.Integer,
						Description = "How many minutes to timeout the target user. Will be DOUBLED if it fails back to you!",
						Choices = new List<ApplicationCommandOptionChoiceProperties>
						{
							new ApplicationCommandOptionChoiceProperties { Name = "Random (30-300 seconds)", Value = 0 },
							new ApplicationCommandOptionChoiceProperties { Name = "30 seconds", Value = 30 },
							new ApplicationCommandOptionChoiceProperties { Name = "1 minute", Value = 60 },
							new ApplicationCommandOptionChoiceProperties { Name = "2 minutes", Value = 120 },
							new ApplicationCommandOptionChoiceProperties { Name = "3 minutes", Value = 180 },
							new ApplicationCommandOptionChoiceProperties { Name = "4 minutes", Value = 240 },
							new ApplicationCommandOptionChoiceProperties { Name = "5 minutes", Value = 300 },
							new ApplicationCommandOptionChoiceProperties { Name = "10 minutes", Value = 600 }
						}
					},
					new SlashCommandOptionBuilder { Name = "reason", Type = ApplicationCommandOptionType.String, Description = "Provide the reason why you wish to time out the target user." },
					new SlashCommandOptionBuilder { Name = "opt-out", Type = ApplicationCommandOptionType.Boolean, Description = "Toggle the opt-out status of the target user." }
				}
			}.Build();
		}

		public override async Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			Logger.LogInformation($"/{CommandName} executed at {DateTime.Now}");

			// Validate target user exists, and is not admin or a bot
			var userArg = cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "user")?.Value as RestGuildUser;
			var optOutCommand = cmdWrapper.Command.Data.Options.FirstOrDefault(c => c.Name == "opt-out");
			var curGuild = _restClientProvider.Guilds.First(g => g.Id == cmdWrapper.Command.GuildId);

			if (userArg == null)
				return cmdWrapper.Respond("You must provide a target user.", ephemeral: true);

			// Always load opt-outs
			var optOuts = _guildHelper.LoadUserIDs(OptOutsFile);

			// Only validate the following if needed
			var cmdUser = (cmdWrapper.Command.User as RestGuildUser)!;
			if (optOutCommand == null)
			{
				if (cmdUser.GuildPermissions.Administrator)
					return cmdWrapper.Respond("Unfortunately you cannot use this command as an administrator because you are unable to be timed out. A workaround may be coming soon.", ephemeral: true);
				if (userArg.GuildPermissions.Administrator)
					return cmdWrapper.Respond("Unfortunately, Discord doesn't allow true administrators to be timed out. A workaround may be coming soon.", ephemeral: true);
				if (userArg.IsBot)
					return cmdWrapper.Respond("Sorry, Shaosil doesn't want you to time out bots since that would mess with their code. Respect the code. All hail the code.", ephemeral: true);
				if (optOuts.Any(u => u == userArg.Id))
					return cmdWrapper.Respond($"Sorry, {userArg.DisplayName} has opted out of the /{CommandName} command. You'll have to pick on somebody else.", ephemeral: true);
				if (optOuts.Any(u => u == cmdUser.Id))
					return cmdWrapper.Respond($"Sorry, you can't participate in $/{CommandName} because you are in the opt-out list. If you want to opt back in, use `/{CommandName} user {{your username}} opt-out False`", ephemeral: true);
			}

			// The opt-out command is handled by itself
			if (optOutCommand != null)
			{
				var matchingOptOut = optOuts.FirstOrDefault(o => o == userArg.Id);
				bool isOptOut = (bool)optOutCommand.Value;

				// Make sure we have permission to edit this user
				bool canEdit = _guildHelper.UserCanEditTargetUser(curGuild, cmdUser, userArg);

				// Release the lock and return if there's nothing to do
				if (!canEdit || isOptOut == (matchingOptOut != default))
				{
					_fileAccessHelper.ReleaseFileLease(OptOutsFile);

					return !canEdit ? cmdWrapper.Respond($"You are not ranked high enough to edit {userArg.DisplayName}'s opt-out status. Please ask someone more important, or tell them to do it themselves.", ephemeral: true)
						: isOptOut ? cmdWrapper.Respond($"{userArg.DisplayName} is already opted out of timeouts. Thanks for your consideration though :)", ephemeral: true)
						: cmdWrapper.Respond($"{userArg.DisplayName} is not currently opted out of timeouts.", ephemeral: true);
				}

				// Add or remove user
				if (isOptOut)
					optOuts.Add(userArg.Id);
				else
					optOuts.Remove(matchingOptOut);
				_fileAccessHelper.SaveFileJSON(OptOutsFile, optOuts);

				return isOptOut ? cmdWrapper.Respond($"{userArg.Mention} was successfully opted out of the /{CommandName} command.")
					: cmdWrapper.Respond($"{userArg.Mention} was successfully removed from the opt-out list of the /{CommandName} command.");

			}

			// Handle timeouts async in case it takes more than 3 seconds
			return await cmdWrapper.DeferWithCode(async () =>
			{
				// First validate the target user isn't in one
				if (await _socketClientProvider.UserIsInVC(userArg.Id))
				{
					await cmdWrapper.Command.FollowupAsync($"{userArg} cannot be timed out since they are currently in a voice chat channel.");
					return;
				}

				// Store existing timeout check since that gives unique results
				var remainingTimeout = userArg.TimedOutUntil.HasValue && userArg.TimedOutUntil > DateTimeOffset.UtcNow ? userArg.TimedOutUntil.Value - DateTimeOffset.UtcNow : new TimeSpan();

				// Get minutes and calculate percentage of success for which user to affect
				int seconds = int.Parse(cmdWrapper.Command.Data.Options.FirstOrDefault(c => c.Name == "duration")?.Value.ToString() ?? "60");
				if (seconds == 0) seconds = Random.Shared.Next(30, 301);
				int toHit = (int)Math.Round((0.000169 * Math.Pow(seconds, 2)) - (0.185 * seconds) + 55.409); // Predetermined quadratic formula
				int result = Random.Shared.Next(1, 101);
				bool success = result <= toHit;
				var targetUser = success ? userArg : cmdUser;

				// Apply timeout to target user - if the target user is already in a timeout, append the new minutes.
				// If the target user is timed out and this FAILED, add the target's remaining timeout to the usual punishment
				bool selfTarget = userArg.Id == cmdUser.Id;
				var timeoutSpan = remainingTimeout + new TimeSpan(0, 0, seconds * (selfTarget || success ? 1 : 2));
				string timeoutEndUnix = $"<t:{(DateTimeOffset.Now + timeoutSpan).ToUnixTimeSeconds()}:R>";
				await targetUser.SetTimeOutAsync(timeoutSpan);

				// Get reason
				string? reason = cmdWrapper.Command.Data.Options.FirstOrDefault(c => c.Name == "reason")?.Value.ToString();
				reason = $"{(string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Reason:```{reason}```")}";

				// Unique response texts for someone who was already timed out or targeting yourself
				var response = new StringBuilder();
				if (selfTarget)
				{
					response.AppendLine($"{userArg.Mention} has decided they wish to time themselves out for {seconds} seconds. Since I'm a nice cooperative bot, I can't refuse.{reason}");
					response.AppendLine();
					response.AppendLine($"Timeout expires {timeoutEndUnix}");
				}
				else
				{
					response.Append($"{cmdUser.Mention} has attempted to timeout {userArg.Mention} for {seconds} seconds");
					if (remainingTimeout.TotalSeconds > 0) response.AppendLine($" even though they are already in timeout! Bold move Cotton, let's see how it pays off.{reason}");
					else response.AppendLine($".{reason}");
					response.AppendLine();
					response.AppendLine($"🎲 Using a {toHit}% chance of success as a maximum roll target out of 100, they rolled a {result}.");
					response.AppendLine();
					response.Append($"**{(success ? "🟢 Success" : "🔴 Fail")}!** As a result, {targetUser.Mention} ");
					if (success)
					{
						if (remainingTimeout.TotalSeconds > 0) response.Append($"has an ADDITIONAL {seconds} seconds tacked on to their timeout, which now expires {timeoutEndUnix}.");
						else response.Append($"is now timed out with an expiration of {timeoutEndUnix}.");
					}
					else
					{
						if (remainingTimeout.TotalSeconds > 0) response.Append($"has been punished with double the target duration PLUS the target user's remaining timeout! Their timeout will expire {timeoutEndUnix}.");
						else response.Append($"has been slammed with double the target duration. Their timeout will expire {timeoutEndUnix}.");
					}
				}

				await cmdWrapper.Command.FollowupAsync(response.ToString());
			});
		}
	}
}