using Discord.WebSocket;

namespace ShaosilBot.Core.Interfaces
{
	public interface IChatGPTProvider
	{
		Task HandleChatRequest(SocketMessage message);
		Task SendChatMessage(ISocketMessageChannel channel, string prompt);
		void FillAllUserBuckets(); // Should be called at the start of each month
		void UpdateAllUserBuckets(ulong id, bool userAdded); // Should be called when a user leaves/joins
	}
}