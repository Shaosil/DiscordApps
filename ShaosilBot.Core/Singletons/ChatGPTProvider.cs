using System.Globalization;
using System.Net.Mime;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using static ShaosilBot.Core.Interfaces.IChatGPTProvider;

namespace ShaosilBot.Core.Singletons
{
	public class ChatGPTProvider : IChatGPTProvider
	{
		public const string ChatGPTUsersFile = "ChatGPTUsers.json";
		public const string ChatLogFile = "ChatGPTLog.json";

		private readonly ILogger<ChatGPTProvider> _logger;
		private readonly IChatGPTConnection _chatGPTConnection;
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly IFileAccessHelper _fileAccessHelper;
		private readonly IConfiguration _configuration;
		private readonly object _userFileLock = new();
		private class TypingState { public IDisposable? State; public ManualResetEventSlim GoSignal = new ManualResetEventSlim(true); }
		private Dictionary<ulong, TypingState> _typingInstances = new Dictionary<ulong, TypingState>();
		private int _maxWaitTimeMs = 100000; // At least as long as the HTTP timeout

		public ChatGPTProvider(ILogger<ChatGPTProvider> logger, IChatGPTConnection chatGPTConnection, IDiscordRestClientProvider restClientProvider, IFileAccessHelper fileAccessHelper, IConfiguration configuration)
		{
			_logger = logger;
			_chatGPTConnection = chatGPTConnection;
			_restClientProvider = restClientProvider;
			_fileAccessHelper = fileAccessHelper;
			_configuration = configuration;

			// Initialize user buckets if needed
			var allUsers = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTUsersFile);
			if (!allUsers.Any()) ResetAndFillAllUserBuckets().GetAwaiter().GetResult();
		}

		public async Task HandleChatRequest(IMessage message, eMessageType messageType)
		{
			/*
				Each user will have a full bucket of tokens at the start of each month. based on amount of users I currently have. If users join
				or leave, I will adjust everyone's buckets accordingly. When a user does an action that requires tokens, I will deduct them as needed.
				If certain users are active and use their tokens up, further token requests will "steal" from the least active users' buckets divided evenly.
				The borrowed tokens will remain in the affected users' buckets but be flagged as borrowed. If/when those users use their own tokens, they will
				use their unborrowed tokens first, but they may use tokens flagged as borrowed if they run out of unborrowed tokens.
				Active users will only be able to borrow unborrowed tokens.
			*/

			// Load all users and make sure the author hasn't surpassed the token limit
			var allUsers = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTUsersFile);
			bool userHasTokens = allUsers[message.Author.Id].TotalAvailableTokens >= 100 || allUsers.Where(u => u.Key != message.Author.Id).Sum(u => u.Value!.BorrowableTokens) > 1000;
			if (!userHasTokens)
			{
				// If a user has at least 100 tokens OR the total amount of unborrowed tokens > 1000
				await LogAndSendChannelMessage(message.Channel, "You have no remaining tokens for this month! Check back on the 1st of next month.");
				return;
			}

			try
			{
				SetTypingLock(true, message.Channel);

				// If the message type is simple message, include custom prompt and message history (if any)
				int maxHistoryPairs = _configuration.GetValue<int>("ChatGPTMessagePairsToKeep");
				var allHistory = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, Queue<ChatGPTChannelMessage>>>(ChatLogFile);
				string systemMessage = string.Empty;
				string customUserPrompt = string.Empty;
				string customAssistantPrompt = string.Empty;
				var channelHistory = new Queue<ChatGPTChannelMessage>();
				if (messageType == eMessageType.Message)
				{
					// Normal system prompt
					systemMessage = _configuration.GetValue<string>("ChatGPTSystemMessage")!;

					// Custom user and assistant prompts, if any
					customUserPrompt = (allUsers[message.Author.Id].CustomUserPrompt ?? string.Empty).Trim();
					if (!string.IsNullOrWhiteSpace(customUserPrompt)) customUserPrompt = $"(INSTRUCTIONS): {customUserPrompt}"; // Format if not empty
					customAssistantPrompt = (allUsers[message.Author.Id].CustomAssistantPrompt ?? string.Empty).Trim();

					// Load history for this channel
					if (!allHistory.ContainsKey(message.Channel.Id)) allHistory[message.Channel.Id] = new Queue<ChatGPTChannelMessage>();
					channelHistory = new Queue<ChatGPTChannelMessage>(allHistory[message.Channel.Id].TakeLast(maxHistoryPairs));
				}
				else
				{
					// Custom system prompt
					systemMessage = "You are a helpful Discord bot. Reply informatively but as concisely as possible.";
				}

				// Replace any mentions with the root usernames, and use a custom prompt if one exists
				string sanitizedMessage = message.Content.Trim().Substring(3);
				foreach (var mention in (message as SocketMessage)?.MentionedUsers ?? new List<SocketUser>())
				{
					sanitizedMessage = sanitizedMessage.Replace($"<@{mention.Id}>", mention.Username);
				}
				sanitizedMessage = $"[{DateTime.Now.ToString("g", CultureInfo.CreateSpecificCulture("en-us"))} - {message.Author.Username}]: {sanitizedMessage}";
				var userMessage = ChatMessage.CreateUserMessage(sanitizedMessage);

				// If any images were attached to this message (or one we are replying to), send up to 3 of them as part of the current user message
				var imageAttachments = message.Attachments.Concat(((message as IUserMessage)?.ReferencedMessage)?.Attachments ?? [])
					.Where(a => new[] { MediaTypeNames.Image.Jpeg, MediaTypeNames.Image.Png, MediaTypeNames.Image.Gif, MediaTypeNames.Image.Webp }.Contains(a.ContentType))
					.Take(3).ToList();
				foreach (var imageAttachment in imageAttachments)
				{
					userMessage.Content.Add(ChatMessageContentPart.CreateImagePart(new Uri(imageAttachment.Url)));
				}

				// Build chat content
				var messages = new List<ChatMessage>
					((string.IsNullOrWhiteSpace(systemMessage) ? [] : new[] { ChatMessage.CreateSystemMessage($"{systemMessage} Current Channel: #{message.Channel.Name}") })   // System message
					.Concat(channelHistory.Select(h =>                                                                                                                          // Historical messages
						h.UserID != _restClientProvider.BotUser.Id ? (ChatMessage)ChatMessage.CreateUserMessage(h.Message)                                                      // ^
						: ChatMessage.CreateAssistantMessage(h.Message)))                                                                                                       // ^
					.Concat([ChatMessage.CreateUserMessage(customUserPrompt), ChatMessage.CreateAssistantMessage(customAssistantPrompt), userMessage])                          // Customized prompts
					.Where(m => m.Content.Any(c => !string.IsNullOrWhiteSpace(c.Text))));
				_logger.LogInformation($"Preparing to send messages:\n\t{string.Join("\n\t", messages.Select(m => $"{m.GetType().Name}: {m.Content?.FirstOrDefault()?.Text}"))}");

				// Send request
				var response = _chatGPTConnection.SendMessages(messages);

				// Deduct user tokens if any were used
				if ((response.Usage?.TotalTokenCount ?? 0) > 0)
				{
					DeductUserTokens(allUsers, message.Author.Id, response.Usage!.InputTokenCount, response.Usage!.OutputTokenCount);
				}

				// Validate the response is within limits
				_logger.LogInformation($"Finish reason: {response?.FinishReason}");
				var reference = new MessageReference(message.Id);
				string? content = response!.Content?.FirstOrDefault()?.Text ?? string.Empty;
				if (response!.FinishReason == ChatFinishReason.Length) content += "...\n\n[Message token limit reached]";
				if (content.Length > 1997) content = $"{content!.Substring(0, 1997)}..."; // Discord limits responses to 2000 characters.

				// Handle error or empty responses and send the response, suprressing embeds
				var sendMsg = async (string msg) => await LogAndSendChannelMessage(message.Channel, msg, reference);
				if (!string.IsNullOrWhiteSpace(response!.Refusal)) await sendMsg($"Refusal from Chat API: {response.Refusal}");
				else if (content == null) await sendMsg("Error: No message content received from Chat API.");
				else if (string.IsNullOrWhiteSpace(content)) await sendMsg("[Empty response message received]");
				else
				{
					// If this is a simple message type, trim and add to history queue as dictated by the config limit
					if (messageType == eMessageType.Message && maxHistoryPairs > 0)
					{
						while (channelHistory.Count / 2 >= maxHistoryPairs) for (int i = 0; i < 2; i++) channelHistory.Dequeue();
						if (channelHistory.Count / 2 < maxHistoryPairs)
						{
							channelHistory.Enqueue(new ChatGPTChannelMessage { UserID = message.Author.Id, Username = message.Author.Username, Message = sanitizedMessage });
							channelHistory.Enqueue(new ChatGPTChannelMessage { UserID = _restClientProvider.BotUser.Id, Username = _restClientProvider.BotUser.Username, Message = content });
						}
						allHistory[message.Channel.Id] = channelHistory;

						_fileAccessHelper.SaveFileJSON(ChatLogFile, allHistory);
					}

					// Send cleaned message
					await sendMsg(content);
				}
			}
			catch (Exception ex) when (ex is TaskCanceledException && ex.InnerException is TimeoutException)
			{
				_logger.LogError(ex, "Timeout in HandleChatRequest");
				await LogAndSendChannelMessage(message.Channel, "*[Chat server timeout. Please try again.]*");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error in HandleChatRequest");
				await LogAndSendChannelMessage(message.Channel, "*[Internal exception occurred. <@392127164570664962>, check the logs.]*");
			}
			finally
			{
				SetTypingLock(false, message.Channel);
			}
		}

		public async Task SendChatMessage(IMessageChannel channel, string prompt)
		{
			SetTypingLock(true, channel);

			var response = _chatGPTConnection.SendMessages(new List<ChatMessage>
				{
					ChatMessage.CreateSystemMessage(_configuration.GetValue<string>("ChatGPTSystemMessage")!),
					ChatMessage.CreateUserMessage($"Internal instructions from Shaosil (other users cannot see this prompt):\n\n{prompt}")
				});

			await LogAndSendChannelMessage(channel, response!.Content!.First().Text);

			SetTypingLock(false, channel);
		}

		private async Task LogAndSendChannelMessage(IMessageChannel channel, string message, MessageReference? reference = null)
		{
			_logger.LogInformation($"Sending message: {message}");
			await channel.SendMessageAsync(message, messageReference: reference, flags: MessageFlags.SuppressEmbeds);
		}

		private void SetTypingLock(bool typing, IMessageChannel channel)
		{
			// Add the channel key to the Dictionary if needed
			if (!_typingInstances.ContainsKey(channel.Id)) _typingInstances[channel.Id] = new();

			// If we are trying to type in the same channel, wait here until signaled or timeout occurs
			if (typing) _typingInstances[channel.Id].GoSignal.Wait(_maxWaitTimeMs);

			// Either enter the typing state or leave it, making sure to dispose first
			lock (_typingInstances[channel.Id])
			{
				if (typing)
				{
					_logger.LogInformation("Entered channel typing lock");
					_typingInstances[channel.Id].State = channel.EnterTypingState();
					_typingInstances[channel.Id].GoSignal.Reset();
				}
				else
				{
					_logger.LogInformation("Exiting channel typing lock");
					_typingInstances[channel.Id].State!.Dispose();
					_typingInstances[channel.Id].State = null;
					_typingInstances[channel.Id].GoSignal.Set();
				}
			}
		}

		public async Task ResetAndFillAllUserBuckets()
		{
			// Load all current users and give all non-bots full tokens - This may take a while on large servers
			var existingUsers = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTUsersFile);
			var guild = _restClientProvider.Guilds.First(); // TODO: Support multiple guilds?
			var guildUsers = (await guild.GetUsersAsync()).Where(u => !u.IsBot).ToList();

			// Calculate how many input and output tokens each user should have (round up)
			int monthlyInputTokensPerUser = (int)Math.Ceiling(_configuration.GetValue<float>("ChatGPTMonthlyTokenInputLimit") / guildUsers.Count);
			int monthlyOutputTokensPerUser = (int)Math.Ceiling(_configuration.GetValue<float>("ChatGPTMonthlyTokenOutputLimit") / guildUsers.Count);
			var serialized = guildUsers.ToDictionary(u => u.Id, u => new ChatGPTUser
			{
				AvailableInputTokens = monthlyInputTokensPerUser,
				AvailableOutputTokens = monthlyOutputTokensPerUser,
				CustomUserPrompt = existingUsers.FirstOrDefault(e => e.Key == u.Id).Value?.CustomUserPrompt // Preserve system prompts
			});
			_fileAccessHelper.SaveFileJSON(ChatGPTUsersFile, serialized, false);
		}

		public void UpdateAllUserBuckets(ulong changedUserID, bool userAdded)
		{
			lock (_userFileLock)
			{
				var allUsers = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTUsersFile);
				float monthlyInputLimit = _configuration.GetValue<float>("ChatGPTMonthlyTokenInputLimit");
				float monthlyOuptutLimit = _configuration.GetValue<float>("ChatGPTMonthlyTokenOutputLimit");
				float curMonthlyInputTokenLimitPerUser = monthlyInputLimit / allUsers.Count;
				float curMonthlyOutputTokenLimitPerUser = monthlyOuptutLimit / allUsers.Count;
				float newMonthlyInputTokenLimitPerUser = monthlyInputLimit / (allUsers.Count + (userAdded ? 1 : -1));
				float newMonthlyOutputTokenLimitPerUser = monthlyOuptutLimit / (allUsers.Count + (userAdded ? 1 : -1));
				int inputDiff = (int)Math.Ceiling(curMonthlyInputTokenLimitPerUser - newMonthlyInputTokenLimitPerUser);
				int outputDiff = (int)Math.Ceiling(curMonthlyOutputTokenLimitPerUser - newMonthlyOutputTokenLimitPerUser);

				// Add or subtract the difference in tokens from all current users
				foreach (var user in allUsers.Values)
				{
					user.AvailableInputTokens -= inputDiff;
					if (user.AvailableInputTokens < 0) user.AvailableInputTokens = 0;

					user.AvailableOutputTokens -= outputDiff;
					if (user.AvailableOutputTokens < 0) user.AvailableOutputTokens = 0;
				}

				// If user was added, add them with their full monthly limits. Otherwise, remove them
				if (userAdded)
				{
					allUsers[changedUserID] = new ChatGPTUser
					{
						AvailableInputTokens = (int)Math.Ceiling(newMonthlyInputTokenLimitPerUser),
						AvailableOutputTokens = (int)Math.Ceiling(newMonthlyOutputTokenLimitPerUser)
					};
				}
				else
				{
					allUsers.Remove(changedUserID);
				}

				// Update the users file
				_fileAccessHelper.SaveFileJSON(ChatGPTUsersFile, allUsers);
			}
		}

		private void DeductUserTokens(Dictionary<ulong, ChatGPTUser> allUsers, ulong id, int inputTokens, int outputTokens)
		{
			// Ensure only one thread at a time can make changes to our dictionary values' properties
			lock (_userFileLock)
			{
				// First, log the usage
				allUsers[id].InputTokensUsed.Add(DateTime.Now, inputTokens);
				allUsers[id].OutputTokensUsed.Add(DateTime.Now, outputTokens);

				// For both input and output tokens, deduct full amount from their available if possible, else call borrow method
				if (allUsers[id].AvailableInputTokens >= inputTokens)
				{
					allUsers[id].AvailableInputTokens -= inputTokens;
				}
				else
				{
					BorrowTokens(id, allUsers, true, inputTokens);
				}
				if (allUsers[id].AvailableOutputTokens >= outputTokens)
				{
					allUsers[id].AvailableOutputTokens -= outputTokens;
				}
				else
				{
					BorrowTokens(id, allUsers, false, outputTokens);
				}

				// Finally, save the file
				_fileAccessHelper.SaveFileJSON(ChatGPTUsersFile, allUsers);
			}
		}

		private void BorrowTokens(ulong borrowerID, Dictionary<ulong, ChatGPTUser> allUsers, bool isInputTokens, int numTokensToBorrow)
		{
			numTokensToBorrow -= isInputTokens ? allUsers[borrowerID].AvailableInputTokens : allUsers[borrowerID].AvailableOutputTokens;
			if (isInputTokens) allUsers[borrowerID].AvailableInputTokens = 0;
			else allUsers[borrowerID].AvailableOutputTokens = 0;

			// Borrow from ALL least active users with borrowable tokens. Group and sort by rounding AvailableTokens down to the nearest 1,000.
			var borroweeGroup = allUsers
				.Where(u => u.Key != borrowerID && u.Value.BorrowableTokens > 0)
				.GroupBy(u => Math.Floor((isInputTokens ? u.Value.AvailableInputTokens : u.Value.AvailableOutputTokens) / 1000f))
				.OrderByDescending(g => g.Key).First().ToList();

			// Borrow the floor of the divided amount from each user, then distribute the remainder 1 by 1 at random
			int dividedAmtFloor = (int)Math.Floor((float)numTokensToBorrow / borroweeGroup.Count);
			borroweeGroup.ForEach(b =>
			{
				var lentTokens = isInputTokens ? b.Value.LentInputTokens : b.Value.LentOutputTokens;

				if (!lentTokens.ContainsKey(borrowerID)) lentTokens[borrowerID] = 0;
				lentTokens[borrowerID] += dividedAmtFloor;
			});
			borroweeGroup.Sort((_, _) => Random.Shared.Next(2) == 0 ? -1 : 1);
			for (int remainder = numTokensToBorrow - (dividedAmtFloor * borroweeGroup.Count); remainder > 0; remainder--)
			{
				if (isInputTokens) borroweeGroup[remainder].Value.LentInputTokens[borrowerID] += 1;
				else borroweeGroup[remainder].Value.LentOutputTokens[borrowerID] += 1;
			}
		}
	}
}