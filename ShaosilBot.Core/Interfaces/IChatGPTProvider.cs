using Discord;
using Discord.WebSocket;

namespace ShaosilBot.Core.Interfaces
{
	public interface IChatGPTProvider
	{
		Task HandleChatRequest(SocketMessage message);
		Task SendChatMessage(IMessageChannel channel, string prompt);
		void ResetAndFillAllUserBuckets(); // Should be called at the start of each month
		void UpdateAllUserBuckets(ulong id, bool userAdded); // Should be called when a user leaves/joins
	}
}