using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.ObjectModels.RequestModels;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Core.Providers
{
	public class ChatGPTProvider : IChatGPTProvider
	{
		private readonly IConfiguration _configuration;
		private readonly IOpenAIService _openAIService;

		public ChatGPTProvider(IConfiguration configuration, IOpenAIService openAIService)
		{
			_configuration = configuration;
			_openAIService = openAIService;
		}

		public async Task HandleChatRequest(SocketMessage message)
		{
			using (message.Channel.EnterTypingState())
			{
				try
				{
					// If we are not enabled, notify the channel
					if (!_configuration.GetValue<bool>("ChatGPTEnabled"))
					{
						await message.Channel.SendMessageAsync("Sorry, my chatting feature is currently disabled.");
						return;
					}

					var response = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
					{
						Messages = new List<ChatMessage>
						{
							ChatMessage.FromSystem(_configuration.GetValue<string>("ChatGPTSystemMessage")!),
							ChatMessage.FromUser($"Message from {message.Author.Mention} ({message.Author.Username}):\n\n{message.Content.Trim().Skip(2)}")
						}
					});

					// Todo: Store previous messages
					// Todo: Remove user IDs with regex
					// Todo: Limit rates

					await message.Channel.SendMessageAsync(response.Choices[0].Message.Content);
				}
				catch (Exception ex)
				{
					await message.Channel.SendMessageAsync("[Internal exception occurred. Sending debug information to Shaosil]");
					var shaosil = await message.Channel.GetUserAsync(392127164570664962) as SocketUser;
					await (await shaosil!.CreateDMChannelAsync()).SendMessageAsync($"ChatGPT Exception: {ex.Message}\n\nStack Trace: {ex.StackTrace}");
				}
			}
		}
	}
}