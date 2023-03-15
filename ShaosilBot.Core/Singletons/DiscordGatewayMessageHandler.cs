using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.Singletons
{
	public class DiscordGatewayMessageHandler : IDiscordGatewayMessageHandler
	{
		private const string ChannelVisibilitiesFile = "ChannelVisibilityMappings.json";

		// Hardcoded channel and message snowflake IDs for readability
		private const ulong CHANNEL_VISIBILITIES_ID = 1052640054100639784;

		private readonly ILogger<IDiscordGatewayMessageHandler> _logger;
		private readonly IFileAccessHelper _fileAccessHelper;
		private readonly IChatGPTProvider _chatGPTProvider;

		public DiscordGatewayMessageHandler(ILogger<IDiscordGatewayMessageHandler> logger,
			IFileAccessHelper fileAccessHelper,
			IChatGPTProvider chatGPTProvider)
		{
			_logger = logger;
			_fileAccessHelper = fileAccessHelper;
			_chatGPTProvider = chatGPTProvider;
		}

		public async Task MessageReceived(SocketMessage message)
		{
			// If not debugging, in bot channel, and not ourself, do chat stuff on a separate thread
			ulong ourself = 971049069986316338;
			var mentionedSelf = message.MentionedUsers.FirstOrDefault(m => m.Id == ourself);
			if (message.Channel.Id == 1085277525283975318 && message.Author.Id != ourself)
			{
				if (Regex.IsMatch(message.Content.Trim(), "^[\\.!]c ", RegexOptions.IgnoreCase))
				{
					new Task(() => _chatGPTProvider.HandleChatRequest(message), TaskCreationOptions.LongRunning).Start();
				}
				// If it starts with a ping that isn't a reply, remind users to use the proper prefix
				else if (mentionedSelf != null && message.Content.TrimStart().StartsWith(mentionedSelf.Mention) && message.Reference == null)
				{
					await message.Channel.SendMessageAsync("Hey there! If you want to chat with me, just start your message with `!c` and chat away! No need to tag me.");
				}
			}
		}

		public async Task ReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
		{
			_logger.LogInformation($"User {reaction.UserId} ADDED reaction '{reaction.Emote.Name}' to channel {channel.Id} message {message.Id}.");

			switch (channel.Id)
			{
				case CHANNEL_VISIBILITIES_ID:
					await UpdateChannelVisibilities(message.Id, channel.Value as SocketTextChannel, reaction.Emote.Name, reaction.UserId, true);
					break;
			}
		}

		public async Task ReactionRemoved(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
		{
			_logger.LogInformation($"User {reaction.UserId} REMOVED reaction '{reaction.Emote.Name}' to channel {channel.Id} message {message.Id}.");

			switch (channel.Id)
			{
				case CHANNEL_VISIBILITIES_ID:
					await UpdateChannelVisibilities(message.Id, channel.Value as SocketTextChannel, reaction.Emote.Name, reaction.UserId, false);
					break;
			}
		}

		private async Task UpdateChannelVisibilities(ulong messageId, IMessageChannel channel, string emote, ulong userId, bool add)
		{
			string contents = _fileAccessHelper.GetFileText(ChannelVisibilitiesFile);
			var _channelVisibilities = JsonSerializer.Deserialize<List<ChannelVisibility>>(contents);

			var visibility = _channelVisibilities.FirstOrDefault(v => v.MessageID == messageId);
			if (visibility != null)
			{
				// Always load user, message and specific channel mapping info
				var guildChannel = channel as IGuildChannel;
				var allChannels = await guildChannel.Guild.GetTextChannelsAsync();
				var user = await guildChannel.GetUserAsync(userId);
				var message = await channel.GetMessageAsync(messageId);

				// Load channel permission overrides for each other channel mapping
				var otherMappings = visibility.Mappings?.Where(m => m.Emoji != "⭐").ToList();

				// Star emojis handle the ROLE for the user.
				if (emote == "⭐")
				{
					bool userHasRole = user.RoleIds.Contains(visibility.Role);

					if (add && !userHasRole) await user.AddRoleAsync(visibility.Role);
					else if (!add && userHasRole) await user.RemoveRoleAsync(visibility.Role);

					// Remove all other reactions that have explicit channel permissions for this user on this message
					if (add && otherMappings != null)
					{
						foreach (var otherMapping in otherMappings)
						{
							if (otherMapping.Channels.Any(c => allChannels.First(gc => gc.Id == c).GetPermissionOverwrite(user) != null))
							{
								await message.RemoveReactionAsync(Emoji.Parse(otherMapping.Emoji), user);
							}
						}
					}
				}

				// Other emojis should be found in the channel mappings
				else if (visibility.Mappings.Any(m => m.Emoji == emote))
				{
					var affectedChannels = visibility.Mappings.First(m => m.Emoji == emote)?.Channels.ToList();

					foreach (var affectedChannel in affectedChannels)
					{
						var targetChannel = allChannels.First(gc => gc.Id == affectedChannel);
						var userPermissionOverride = targetChannel.GetPermissionOverwrite(user);

						if (add && userPermissionOverride == null) await targetChannel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
						else if (!add && userPermissionOverride != null) await targetChannel.RemovePermissionOverwriteAsync(user);
					}

					// If this is an ADD, clear any star reaction from this user on this message
					if (add)
					{
						await message.RemoveReactionAsync(Emoji.Parse("⭐"), user);
					}
				}
			}
		}
	}
}