using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using ShaosilBot.Core.SlashCommands;

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

		public class CommandNames
		{
			public const string RemindMe = "Remind Me!";

			public class Modals
			{
				public const string CustomReminder = "custom-reminder-modal";
			}
		}

		public MessageCommandProvider(ILogger<SlashCommandProvider> logger,
			RemindMeCommand remindMeCommand,
			PollCommand pollCommand,
			IFileAccessHelper fileAccessHelper)
		{
			_logger = logger;
			_remindMeCommand = remindMeCommand;
			_pollCommand = pollCommand;
			_fileAccessHelper = fileAccessHelper;
		}

		public string HandleMessageCommand(RestMessageCommand command)
		{
			switch (command.Data.Name)
			{
				case CommandNames.RemindMe:
					return _remindMeCommand.HandleRemindMeMessageCommand(command);

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
	}
}