using Discord.WebSocket;

namespace ShaosilBot.Core.Interfaces
{
	public interface IChatGPTProvider
	{
		Task HandleChatRequest(SocketMessage message);
		Task SendChatMessage(ISocketMessageChannel channel, string prompt);
	}
}