using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.ObjectModels.RequestModels;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Core.Providers
{
	public class ChatGPTProvider : IChatGPTProvider
	{
		// Todo: Store previous messages
		// Todo: Limit rates

		private readonly ILogger<ChatGPTProvider> _logger;
		private readonly IConfiguration _configuration;
		private readonly IOpenAIService _openAIService;
		private class Typing { public bool InUse; public IDisposable TypingState; }
		private Typing _typingInstance = new Typing();

		public ChatGPTProvider(ILogger<ChatGPTProvider> logger, IConfiguration configuration, IOpenAIService openAIService)
		{
			_logger = logger;
			_configuration = configuration;
			_openAIService = openAIService;
		}

		public async Task HandleChatRequest(SocketMessage message)
		{
			try
			{
				// If we are not enabled, notify the channel
				if (!_configuration.GetValue<bool>("ChatGPTEnabled"))
				{
					await message.Channel.SendMessageAsync("Sorry, my chatting feature is currently disabled.");
					return;
				}

				await SetTypingLock(true, message.Channel);

				// Replace any mentions with the root usernames
				string sanitizedMessage = message.Content.Trim().Substring(3);
				foreach (var mention in message.MentionedUsers)
				{
					sanitizedMessage = sanitizedMessage.Replace($"<@{mention.Id}>", mention.Username);
				}

				int messageTokenLimit = _configuration.GetValue<int>("ChatGPTMessageTokenLimit");
				var response = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
				{
					Messages = new List<ChatMessage>
					{
						ChatMessage.FromSystem(_configuration.GetValue<string>("ChatGPTSystemMessage")!),
						ChatMessage.FromUser($"Message from {message.Author.Username}:\n\n{sanitizedMessage}")
					},
					MaxTokens = messageTokenLimit
				});

				// Validate the response is within limits
				_logger.LogInformation(response.ToString());
				var responseMessage = response.Choices?.FirstOrDefault()?.Message;
				var reference = new MessageReference(message.Id);
				string? content = responseMessage!.Content;
				if (response.Usage?.CompletionTokens == messageTokenLimit) content += "...\n\n[Message token limit reached]";
				if ((content?.Length ?? 0) > 1997) content = $"{content!.Substring(0, 1997)}..."; // Discord limits responses to 2000 characters. In theory our token limit should prevent this

				// Handle error or empty responses and send the response, suprressing embeds
				var sendMsg = async (string msg) => await message.Channel.SendMessageAsync(msg, messageReference: reference, flags: MessageFlags.SuppressEmbeds);
				if (!string.IsNullOrWhiteSpace(response.Error?.Message)) await sendMsg($"Error from Chat API: {response.Error.Message}. Please try again later.");
				else if (responseMessage == null) await sendMsg("Error: No message content received from Chat API.");
				else if (string.IsNullOrWhiteSpace(content)) await sendMsg("[Empty response message received]");
				else await sendMsg(content);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error in HandleChatRequest");
				var shaosil = await message.Channel.GetUserAsync(392127164570664962) as SocketUser;
				await message.Channel.SendMessageAsync($"[Internal exception occurred. {shaosil!.Mention}, check the logs.]");
			}
			finally
			{
				await SetTypingLock(false, message.Channel);
			}
		}

		public async Task SendChatMessage(ISocketMessageChannel channel, string prompt)
		{
			await SetTypingLock(true, channel);

			var response = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
			{
				Messages = new List<ChatMessage>
				{
					ChatMessage.FromSystem(_configuration.GetValue<string>("ChatGPTSystemMessage")!),
					ChatMessage.FromUser($"Internal instructions from Shaosil (other users cannot see this prompt):\n\n{prompt}")
				}
			});

			await channel.SendMessageAsync(response.Choices[0].Message.Content);

			await SetTypingLock(false, channel);
		}

		private async Task SetTypingLock(bool typing, ISocketMessageChannel channel)
		{
			// If we are trying to aquire the lock, spin until another thread releases it
			while (typing && _typingInstance.InUse) await Task.Delay(250);

			lock (_typingInstance)
			{
				_typingInstance.InUse = typing;

				// Either enter the typing state or leave it
				if (typing) _typingInstance.TypingState = channel.EnterTypingState();
				else _typingInstance.TypingState.Dispose();
			}
		}
	}
}