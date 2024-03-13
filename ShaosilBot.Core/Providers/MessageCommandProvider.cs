using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using ShaosilBot.Core.SlashCommands;
using System.Text;
using System.Text.RegularExpressions;
using static ShaosilBot.Core.Providers.MessageCommandProvider.MessageComponentNames;

namespace ShaosilBot.Core.Providers
{
	public class MessageCommandProvider : IMessageCommandProvider
	{
		private const string ChannelVisibilitiesFile = "ChannelVisibilityMappings.json";

		private readonly ILogger<SlashCommandProvider> _logger;
		private readonly RemindMeCommand _remindMeCommand;
		private readonly ImageGenerateCommand _imageGenerateCommand;
		private readonly PollCommand _pollCommand;
		private readonly IFileAccessHelper _fileAccessHelper;
		private readonly IDiscordRestClientProvider _restClientProvider;

		public static class MessageCommandNames
		{
			public const string RemindMe = "Remind Me!";
			public const string MockThis = "mOcK tHiS";

			public static class Modals
			{
				public const string CustomReminder = "custom-reminder-modal";
			}
		}

		public static class MessageComponentNames
		{
			public static class ImageGeneration
			{
				public const string ImageGenerate = "Image Generate";
				public const string CmdCancel = "Cancel"; // By batch ID
				public const string CmdRequeue = "Requeue"; // By image name
				public const string CmdDelete = "Delete"; // By image name
			}
		}

		public MessageCommandProvider(ILogger<SlashCommandProvider> logger,
			RemindMeCommand remindMeCommand,
			ImageGenerateCommand imageGenerateCommand,
			PollCommand pollCommand,
			IFileAccessHelper fileAccessHelper,
			IDiscordRestClientProvider restClientProvider)
		{
			_logger = logger;
			_remindMeCommand = remindMeCommand;
			_imageGenerateCommand = imageGenerateCommand;
			_pollCommand = pollCommand;
			_fileAccessHelper = fileAccessHelper;
			_restClientProvider = restClientProvider;
		}

		/// <summary>
		/// Handles message commands (i.e. app commands, or the custom context/right click menu things)
		/// </summary>
		public string HandleMessageCommand(RestMessageCommand command)
		{
			switch (command.Data.Name)
			{
				case MessageCommandNames.RemindMe:
					return _remindMeCommand.HandleRemindMeMessageCommand(command);

				case MessageCommandNames.MockThis:
					return ExecuteMockReply(command);

				default:
					return command.Respond("Unsupported command! Poke Shaosil for details.");
			}
		}

		/// <summary>
		/// Handles message components (i.e. buttons, dropdowns, etc within a message)
		/// </summary>
		public async Task<string> HandleMessageComponent(RestMessageComponent messageComponent)
		{
			string customButtonId = messageComponent.Data.CustomId;

			switch (messageComponent.Data.Type)
			{
				case ComponentType.Button:
					if (customButtonId.StartsWith($"{MessageCommandNames.RemindMe}-"))
					{
						return await _remindMeCommand.HandleReminderTimeButton(messageComponent);
					}
					else if (customButtonId.StartsWith($"{ImageGeneration.ImageGenerate}-"))
					{
						return await _imageGenerateCommand.HandleGenerationButton(messageComponent);
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
				case MessageCommandNames.Modals.CustomReminder:
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