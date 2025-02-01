using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Core.Providers
{
	public class ChatGPTConnection : IChatGPTConnection
	{
		private readonly IConfiguration _configuration;

		public ChatGPTConnection(IConfiguration configuration)
		{
			_configuration = configuration;
		}

		public ChatCompletion SendMessages(List<ChatMessage> messages)
		{
			var chatClient = new ChatClient(_configuration["ChatGPTModel"]!, _configuration["OpenAIAPIKey"]!);
			int messageTokenLimit = _configuration.GetValue<int>("ChatGPTMessageTokenLimit");

			return chatClient.CompleteChat(messages, new ChatCompletionOptions { MaxOutputTokenCount = messageTokenLimit > 0 ? messageTokenLimit : null })!.Value;
		}
	}
}