using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using ShaosilBot.Interfaces;
using ShaosilBot.Models;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShaosilBot.Singletons
{
	public class DiscordGatewayMessageHandler : IDiscordGatewayMessageHandler
	{
		private const string ChannelVisibilitiesFile = "ChannelVisibilityMappings.json";

		// Hardcoded channel and message snowflake IDs for readability
		private const ulong CHANNEL_VISIBILITIES_ID = 1052640054100639784;

		private readonly ILogger<IDiscordGatewayMessageHandler> _logger;
		private readonly IDataBlobProvider _dataBlobProvider;

		// Keep the blob file in memory since this is the only file that modifies it
		private List<ChannelVisibility> _channelVisibilities;

		public DiscordGatewayMessageHandler(ILogger<IDiscordGatewayMessageHandler> logger, IDataBlobProvider dataBlobProvider)
		{
			_logger= logger;
			_dataBlobProvider = dataBlobProvider;
		}

		public async Task MessageReceived(SocketMessage message)
		{
			await Task.Delay(0);
		}

		public async Task ReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
		{
			_logger.LogInformation($"User {reaction.UserId} ADDED reaction '{reaction.Emote.Name}' to channel {channel.Id} message {message.Id}.");

			switch (channel.Id)
			{
				case CHANNEL_VISIBILITIES_ID:
					await UpdateChannelVisibilities(message.Id, channel.Value as SocketTextChannel, reaction.Emote.Name, reaction.User.GetValueOrDefault() as SocketGuildUser, true);
					break;
			}
		}

		public async Task ReactionRemoved(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
		{
			_logger.LogInformation($"User {reaction.UserId} REMOVED reaction '{reaction.Emote.Name}' to channel {channel.Id} message {message.Id}.");

			switch (channel.Id)
			{
				case CHANNEL_VISIBILITIES_ID:
					await UpdateChannelVisibilities(message.Id, channel.Value as SocketTextChannel, reaction.Emote.Name, reaction.User.GetValueOrDefault() as SocketGuildUser, false);
					break;
			}
		}

		private async Task UpdateChannelVisibilities(ulong messageId, IMessageChannel channel, string emote, SocketGuildUser user, bool add)
		{
			// Lazy load from blob storage
			if (_channelVisibilities == null)
			{
				string contents = await _dataBlobProvider.GetBlobTextAsync(ChannelVisibilitiesFile);
				_channelVisibilities = JsonSerializer.Deserialize<List<ChannelVisibility>>(contents);
			}

			var visibility = _channelVisibilities.FirstOrDefault(v => v.MessageID == messageId);
			if (visibility != null)
			{
				// Always load message and specific channel mapping info
				var message = await channel.GetMessageAsync(messageId);

				// Load channel permission overrides for each other channel mapping
				var otherMappings = visibility.Mappings?.Where(m => m.Emoji != "⭐").ToList();

				// Star emojis handle the ROLE for the user.
				if (emote == "⭐")
				{
					bool userHasRole = user.Roles.Any(r => r.Id == visibility.Role);

					if (add && !userHasRole) await user.AddRoleAsync(visibility.Role);
					else if (!add && userHasRole) await user.RemoveRoleAsync(visibility.Role);

					// Remove all other reactions that have explicit channel permissions for this user on this message
					if (add && otherMappings != null)
					{
						foreach (var otherMapping in otherMappings)
						{
							if (otherMapping.Channels.Any(c => user.Guild.Channels.First(gc => gc.Id == c).GetPermissionOverwrite(user) != null))
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
						var guildChannel = user.Guild.Channels.First(gc => gc.Id == affectedChannel);
						var userPermissionOverride = guildChannel.GetPermissionOverwrite(user);

						if (add && userPermissionOverride == null) await guildChannel.AddPermissionOverwriteAsync(user, new OverwritePermissions(viewChannel: PermValue.Allow));
						else if (!add && userPermissionOverride != null) await guildChannel.RemovePermissionOverwriteAsync(user);
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