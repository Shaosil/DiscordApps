using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.Singletons
{
	public class DiscordGatewayMessageHandler : IDiscordGatewayMessageHandler
	{
		private readonly ILogger<IDiscordGatewayMessageHandler> _logger;
		private readonly IConfiguration _configuration;
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly IChatGPTProvider _chatGPTProvider;
		private readonly IQuartzProvider _quartzProvider;

		public DiscordGatewayMessageHandler(ILogger<IDiscordGatewayMessageHandler> logger,
			IConfiguration configuration,
			IDiscordRestClientProvider restClientProvider,
			IChatGPTProvider chatGPTProvider,
			IQuartzProvider quartzProvider)
		{
			_logger = logger;
			_configuration = configuration;
			_restClientProvider = restClientProvider;
			_chatGPTProvider = chatGPTProvider;
			_quartzProvider = quartzProvider;
		}

		public Task UserJoined(SocketGuildUser user)
		{
			// Update ChatGPT buckets
			return Task.FromResult(() => { if (!user.IsBot) _chatGPTProvider.UpdateAllUserBuckets(user.Id, true); });
		}

		public Task UserLeft(SocketGuild guild, SocketUser user)
		{
			// Update ChatGPT buckets
			return Task.FromResult(() => { if (!user.IsBot) _chatGPTProvider.UpdateAllUserBuckets(user.Id, false); });
		}

		public async Task MessageReceived(SocketMessage message)
		{
			ulong ourself = _restClientProvider.BotUser.Id;
			var mentionedSelf = message.MentionedUsers.FirstOrDefault(m => m.Id == ourself);

			// Respond to chat request
			if (message.Author.Id != ourself)
			{
				if (Regex.IsMatch(message.Content.Trim(), "^[\\.!]c ", RegexOptions.IgnoreCase))
				{
					// If we are not enabled, notify the channel. Else, handle request on a separate thread
					if (!_configuration.GetValue<bool>("ChatGPTEnabled"))
					{
						await message.Channel.SendMessageAsync("Sorry, my chatting feature is currently disabled.");
					}
					else
					{
						await Task.Factory.StartNew(async () => await _chatGPTProvider.HandleChatRequest(message), TaskCreationOptions.LongRunning).ConfigureAwait(false);
					}
				}
				// If it starts with a ping that isn't a reply, remind users to use the proper prefix
				else if (mentionedSelf != null && message.Content.TrimStart().StartsWith(mentionedSelf.Mention) && message.Reference == null)
				{
					await message.Channel.SendMessageAsync("Hey there! If you want to chat with me, just start your message with `!c` and chat away! No need to tag me.");
				}
			}

			// Automatically delete no-no zone messages after 12 hours
			if (message.Channel.Id == 1022371866272346112)
			{
				_quartzProvider.SelfDestructMessage(message, 12);
			}
		}

		public Task ReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
		{
			_logger.LogInformation($"User {reaction.UserId} ADDED reaction '{reaction.Emote.Name}' to channel {channel.Id} message {message.Id}.");
			return Task.CompletedTask;
		}

		public Task ReactionRemoved(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
		{
			_logger.LogInformation($"User {reaction.UserId} REMOVED reaction '{reaction.Emote.Name}' to channel {channel.Id} message {message.Id}.");
			return Task.CompletedTask;
		}
	}
}