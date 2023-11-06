﻿using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using ShaosilBot.Core.SlashCommands;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.Providers
{
	public class MessageCommandProvider : IMessageCommandProvider
	{
		private const string ChannelVisibilitiesFile = "ChannelVisibilityMappings.json";

		// Hardcoded channel visibility ID
		private const ulong CHANNEL_VISIBILITIES_ID = 1052640054100639784;

		private readonly ILogger<SlashCommandProvider> _logger;
		private readonly RemindMeCommand _remindMeCommand;
		private readonly PollCommand _pollCommand;
		private readonly IFileAccessHelper _fileAccessHelper;
		private readonly IDiscordRestClientProvider _restClientProvider;

		public class CommandNames
		{
			public const string RemindMe = "Remind Me!";
			public const string MockThis = "mOcK tHiS";

			public class Modals
			{
				public const string CustomReminder = "custom-reminder-modal";
			}
		}

		public MessageCommandProvider(ILogger<SlashCommandProvider> logger,
			RemindMeCommand remindMeCommand,
			PollCommand pollCommand,
			IFileAccessHelper fileAccessHelper,
			IDiscordRestClientProvider restClientProvider)
		{
			_logger = logger;
			_remindMeCommand = remindMeCommand;
			_pollCommand = pollCommand;
			_fileAccessHelper = fileAccessHelper;
			_restClientProvider = restClientProvider;
		}

		public async Task BuildMessageCommands()
		{
			var allMessageNames = typeof(CommandNames).GetFields(BindingFlags.Static | BindingFlags.Public).Select(f => f.GetValue(null)!.ToString()!).ToList();

			var guilds = _restClientProvider.Guilds;

			foreach (var guild in guilds)
			{
				// Create message commands
				var messageCommands = (await guild.GetApplicationCommandsAsync()).Where(c => c.Type == ApplicationCommandType.Message).ToList();

				// Remove ones that no longer exist
				foreach (var msgCommand in messageCommands.Where(c => !allMessageNames.Contains(c.Name)))
				{
					await msgCommand.DeleteAsync();
				}

				// Create ones that are new
				foreach (string newMsgCommandName in allMessageNames.Where(n => !messageCommands.Any(c => c.Name == n)))
				{
					await guild.CreateApplicationCommandAsync(new MessageCommandBuilder { Name = newMsgCommandName }.Build());
				}
			}
		}

		public string HandleMessageCommand(RestMessageCommand command)
		{
			switch (command.Data.Name)
			{
				case CommandNames.RemindMe:
					return _remindMeCommand.HandleRemindMeMessageCommand(command);

				case CommandNames.MockThis:
					return ExecuteMockReply(command);

				default:
					return command.Respond("Unsupported command! Poke Shaosil for details.");
			}
		}

		public async Task<string> HandleMessageComponent(RestMessageComponent messageComponent)
		{
			string customButtonId = messageComponent.Data.CustomId;

			switch (messageComponent.Data.Type)
			{
				case ComponentType.Button:
					if (customButtonId.StartsWith($"{CommandNames.RemindMe}-"))
					{
						return await _remindMeCommand.HandleReminderTimeButton(messageComponent);
					}
					else
					{
						return messageComponent.Respond("Unknown button ID! Poke Shaosil for details.", ephemeral: true);
					}

				case ComponentType.SelectMenu:
					if (customButtonId == PollCommand.SelectMenuID)
					{
						return _pollCommand.HandleVote(messageComponent);
					}
					else if (customButtonId == ChannelVisibility.SelectMenuID)
					{
						await UpdateChannelVisibilities(messageComponent);
						return messageComponent.Defer(true);
					}
					else
					{
						return messageComponent.Respond("Unknown select menu ID! Poke Shaosil for details.", ephemeral: true);
					}

				default:
					return messageComponent.Respond("Unknown component type! Poke Shaosil for details.", ephemeral: true);
			}
		}

		public async Task<string> HandleModel(RestModal modal)
		{
			switch (modal.Data.CustomId)
			{
				case CommandNames.Modals.CustomReminder:
					return await _remindMeCommand.HandleReminderTimeModal(modal);

				default:
					return modal.Respond("Unknown modal type! Poke Shaosil for details.", ephemeral: true);
			}
		}

		private async Task UpdateChannelVisibilities(RestMessageComponent messageComponent)
		{
			var _channelVisibilities = _fileAccessHelper.LoadFileJSON<List<ChannelVisibility>>(ChannelVisibilitiesFile);

			var visibility = _channelVisibilities.FirstOrDefault(v => v.MessageID == messageComponent.Message.Id);
			if (visibility != null)
			{
				// Always load user, message and specific channel mapping info
				var guildChannel = (IGuildChannel)messageComponent.Channel;
				var allChannels = await guildChannel.Guild.GetTextChannelsAsync();
				var visibilityChannels = allChannels.Where(c => visibility.Mappings.Any(m => m.Channels.Contains(c.Id))).ToList();
				var user = (IGuildUser)messageComponent.User;

				// First, clear out all related roles and permissions for this user
				await user.RemoveRoleAsync(visibility.Role);
				visibilityChannels.ForEach(async c => await c.RemovePermissionOverwriteAsync(user));

				// Then check if they provided an "ALL" value and give them the overall role
				if (messageComponent.Data.Values.Any(v => v == "ALL"))
				{
					await user.AddRoleAsync(visibility.Role);
				}
				else
				{
					// Otherwise, loop through the supplied values and assign channel permissions
					var targetChannelIDs = visibility.Mappings.Where(m => messageComponent.Data.Values.Contains(m.Value)).SelectMany(m => m.Channels).Distinct().ToList();
					targetChannelIDs.ForEach(async c =>
					{
						var targetChannel = visibilityChannels.FirstOrDefault(v => v.Id == c);
						if (targetChannel != null)
						{
							await targetChannel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
						}
					});
				}
			}
		}

		private string ExecuteMockReply(RestMessageCommand command)
		{
			// Read the content of the message, if any
			var regex = new Regex("[a-zA-Z]");
			string? originalText = command.Data.Message.Content?.Trim();
			if (string.IsNullOrWhiteSpace(originalText))
			{
				return command.Respond("The target message has no text, nothing to mock.", ephemeral: true);
			}
			// Don't mock < 4 characters
			else if (regex.Matches(originalText).Count < 4)
			{
				return command.Respond("Sorry, this message doesn't have enough letters to effectively mock.", ephemeral: true);
			}
			// Don't mock messages from ourself
			else if (command.Data.Message.Author.Id == _restClientProvider.BotUser.Id)
			{
				return command.Respond("Nice try, but I refuse to mock my own words. :slight_smile:", ephemeral: true);
			}

			// For each alpha character, flip back and forth from lowers to uppers
			var sb = new StringBuilder();
			int alphaInc = 0;
			foreach (char c in originalText)
			{
				if (regex.IsMatch($"{c}"))
				{
					sb.Append(alphaInc++ % 2 == 0 ? $"{c}".ToLower() : $"{c}".ToUpper());
				}
				else
				{
					sb.Append(c);
				}
			}

			return command.Respond(sb.ToString());
		}
	}
}