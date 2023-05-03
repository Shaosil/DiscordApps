using Discord;

namespace ShaosilBot.Core.Interfaces
{
	public interface IChatGPTProvider
	{
		public enum eMessageType { Message, Question }

		Task HandleChatRequest(IMessage message, eMessageType messageType);
		Task SendChatMessage(IMessageChannel channel, string prompt);
		Task ResetAndFillAllUserBuckets(); // Should be called at the start of each month
		void UpdateAllUserBuckets(ulong id, bool userAdded); // Should be called when a user leaves/joins
	}
}