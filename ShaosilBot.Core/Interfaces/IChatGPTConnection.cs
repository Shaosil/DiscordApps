using OpenAI.Chat;

namespace ShaosilBot.Core.Interfaces
{
	public interface IChatGPTConnection
	{
		ChatCompletion SendMessages(List<ChatMessage> messages);
	}
}