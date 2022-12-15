﻿using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;

namespace ShaosilBot.Interfaces
{
	public interface IDiscordGatewayMessageHandler
	{
		Task MessageReceived(SocketMessage message);
		Task ReactionAdded(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction);
		Task ReactionRemoved(Cacheable<IUserMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction);
	}
}