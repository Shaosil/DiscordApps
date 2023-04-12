using Discord;

namespace ShaosilBot.Core.Interfaces
{
	public interface IChatGPTProvider
	{
		Task HandleChatRequest(IMessage message);
		Task SendChatMessage(IMessageChannel channel, string prompt);
		Task ResetAndFillAllUserBuckets(); // Should be called at the start of each month
		void UpdateAllUserBuckets(ulong id, bool userAdded); // Should be called when a user leaves/joins
	}
}