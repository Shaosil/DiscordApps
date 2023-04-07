using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.ObjectModels.RequestModels;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using System.Globalization;

namespace ShaosilBot.Core.Singletons
{
	public class ChatGPTProvider : IChatGPTProvider
	{
		public const string ChatGPTUsersFile = "ChatGPTUsers.json";
		public const string ChatLogFile = "ChatGPTLog.json";

		private readonly ILogger<ChatGPTProvider> _logger;
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly IFileAccessHelper _fileAccessHelper;
		private readonly IConfiguration _configuration;
		private readonly IOpenAIService _openAIService;
		private readonly object _userFileLock = new();
		private class TypingState { public IDisposable? State; public ManualResetEventSlim GoSignal = new ManualResetEventSlim(true); }
		private Dictionary<ulong, TypingState> _typingInstances = new Dictionary<ulong, TypingState>();
		private int _maxWaitTimeMs = 100000; // At least as long as the HTTP timeout

		public ChatGPTProvider(ILogger<ChatGPTProvider> logger, IDiscordRestClientProvider restClientProvider, IFileAccessHelper fileAccessHelper, IConfiguration configuration, IOpenAIService openAIService)
		{
			_logger = logger;
			_restClientProvider = restClientProvider;
			_fileAccessHelper = fileAccessHelper;
			_configuration = configuration;
			_openAIService = openAIService;

			// Initialize user buckets if needed
			var allUsers = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTUsersFile);
			if (!allUsers.Any()) ResetAndFillAllUserBuckets();
		}

		public async Task HandleChatRequest(SocketMessage message)
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
			bool userHasTokens = allUsers[message.Author.Id].AvailableTokens >= 100 || allUsers.Where(u => u.Key != message.Author.Id).Sum(u => u.Value!.BorrowableTokens) > 1000;
			if (!userHasTokens)
			{
				// If a user has at least 100 tokens OR the total amount of unborrowed tokens > 1000
				await message.Channel.SendMessageAsync("You have no remaining tokens for this month! Check back on the 1st of next month.");
				return;
			}

			try
			{
				SetTypingLock(true, message.Channel);

				// Replace any mentions with the root usernames, and use a custom prompt if one exists
				string sanitizedMessage = message.Content.Trim().Substring(3);
				foreach (var mention in message.MentionedUsers)
				{
					sanitizedMessage = sanitizedMessage.Replace($"<@{mention.Id}>", mention.Username);
				}
				sanitizedMessage = $"[{DateTime.Now.ToString("g", CultureInfo.CreateSpecificCulture("en-us"))} - {message.Author.Username}]: {sanitizedMessage}";
				string customPrompt = (allUsers[message.Author.Id].CustomSystemPrompt ?? string.Empty).Trim();
				if (!string.IsNullOrWhiteSpace(customPrompt)) customPrompt = $"(INSTRUCTIONS): {customPrompt}"; // Format if not empty

				// Load history for this channel
				var chatHistory = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, Queue<ChatGPTChannelMessage>>>(ChatLogFile);
				if (!chatHistory.ContainsKey(message.Channel.Id)) chatHistory[message.Channel.Id] = new Queue<ChatGPTChannelMessage>();
				var history = chatHistory[message.Channel.Id];

				// Build system prompt and send request
				var botUser = _restClientProvider.Client.CurrentUser;
				int messageTokenLimit = _configuration.GetValue<int>("ChatGPTMessageTokenLimit");

				var response = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
				{
					Messages = new List<ChatMessage>
						{ ChatMessage.FromSystem($"{_configuration.GetValue<string>("ChatGPTSystemMessage")}\n\nCurrent Channel: #{message.Channel.Name}") }                        // System message
						.Concat(history.Select(h => h.UserID != botUser.Id ? ChatMessage.FromUser(h.Message) : ChatMessage.FromAssistant(h.Message)))                               // Historical messages
						.Concat(new[] { ChatMessage.FromUser(customPrompt), ChatMessage.FromUser(sanitizedMessage) }.Where(m => !string.IsNullOrWhiteSpace(m.Content))).ToList(),   // Current customized message
					MaxTokens = messageTokenLimit
				});

				// Deduct user tokens if any were used
				if ((response.Usage?.TotalTokens ?? 0) > 0) DeductUserTokens(allUsers, message.Author.Id, response.Usage!.TotalTokens);

				// Validate the response is within limits
				_logger.LogInformation(response.ToString());
				var responseMessage = response.Choices?.FirstOrDefault()?.Message;
				var reference = new MessageReference(message.Id);
				string? content = responseMessage?.Content ?? string.Empty;
				if (response.Usage?.CompletionTokens == messageTokenLimit) content += "...\n\n[Message token limit reached]";
				if (content.Length > 1997) content = $"{content!.Substring(0, 1997)}..."; // Discord limits responses to 2000 characters.

				// Handle error or empty responses and send the response, suprressing embeds
				var sendMsg = async (string msg) => await message.Channel.SendMessageAsync(msg, messageReference: reference, flags: MessageFlags.SuppressEmbeds);
				if (!string.IsNullOrWhiteSpace(response.Error?.Message)) await sendMsg($"Error from Chat API: {response.Error.Message}. Please try again later.");
				else if (responseMessage == null) await sendMsg("Error: No message content received from Chat API.");
				else if (string.IsNullOrWhiteSpace(content)) await sendMsg("[Empty response message received]");
				else
				{
					// Trim and add to history queue as dictated by the config limit
					int maxHistoryPairs = _configuration.GetValue<int>("ChatGPTMessagePairsToKeep");
					if (maxHistoryPairs > 0)
					{
						while (history.Count / 2 >= maxHistoryPairs) for (int i = 0; i < 2; i++) history.Dequeue();
						if (history.Count / 2 < maxHistoryPairs)
						{
							history.Enqueue(new ChatGPTChannelMessage { UserID = message.Author.Id, Username = message.Author.Username, Message = sanitizedMessage });
							history.Enqueue(new ChatGPTChannelMessage { UserID = botUser.Id, Username = botUser.Username, Message = content });
						}

						_fileAccessHelper.SaveFileJSON(ChatLogFile, chatHistory);
					}

					// Send cleaned message
					await sendMsg(content);
				}
			}
			catch (Exception ex) when (ex is TaskCanceledException && ex.InnerException is TimeoutException)
			{
				_logger.LogError(ex, "Timeout in HandleChatRequest");
				await message.Channel.SendMessageAsync("*[Chat server timeout. Please try again.]*");
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error in HandleChatRequest");
				await message.Channel.SendMessageAsync("*[Internal exception occurred. <@392127164570664962>, check the logs.]*");
			}
			finally
			{
				SetTypingLock(false, message.Channel);
			}
		}

		public async Task SendChatMessage(IMessageChannel channel, string prompt)
		{
			SetTypingLock(true, channel);

			var response = await _openAIService.ChatCompletion.CreateCompletion(new ChatCompletionCreateRequest
			{
				Messages = new List<ChatMessage>
				{
					ChatMessage.FromSystem(_configuration.GetValue<string>("ChatGPTSystemMessage")!),
					ChatMessage.FromUser($"Internal instructions from Shaosil (other users cannot see this prompt):\n\n{prompt}")
				}
			});

			await channel.SendMessageAsync(response.Choices[0].Message.Content);

			SetTypingLock(false, channel);
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

		public async void ResetAndFillAllUserBuckets()
		{
			// Load all current users and give all non-bots full tokens - This may take a while on large servers
			var existingUsers = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTUsersFile);
			ulong guildID = _configuration.GetValue<ulong>("TargetGuild");
			var guild = _restClientProvider.Client.GetGuildAsync(guildID).Result;
			var guildUsers = (guild.GetUsersAsync().FlattenAsync().Result).Where(u => !u.IsBot).ToList();

			// Calculate how many tokes each user should have (round up)
			int totalMonthlyTokensPerUser = (int)Math.Ceiling(_configuration.GetValue<float>("ChatGPTMonthlyTokenLimit") / guildUsers.Count);
			var serialized = guildUsers.ToDictionary(u => u.Id, u => new ChatGPTUser
			{
				AvailableTokens = totalMonthlyTokensPerUser,
				CustomSystemPrompt = existingUsers.FirstOrDefault(e => e.Key == u.Id).Value?.CustomSystemPrompt // Preserve system prompts
			});
			_fileAccessHelper.SaveFileJSON(ChatGPTUsersFile, serialized, false);

			// Let everyone know it's a new month
			var generalChannel = await guild.GetChannelAsync(_configuration.GetValue<ulong>("MainChannel")) as RestTextChannel;
			await generalChannel!.SendMessageAsync("It's a brand new month, and as a result, my chatting usage has been reset and everyone has a fresh new bucket of tokens! Happy `!c`hatting everyone. :blush:");
		}

		public void UpdateAllUserBuckets(ulong changedUserID, bool userAdded)
		{
			lock (_userFileLock)
			{
				var allUsers = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTUsersFile);
				float monthlyLimit = _configuration.GetValue<float>("ChatGPTMonthlyTokenLimit");
				float curMonthlyTokenLimitPerUser = monthlyLimit / allUsers.Count;
				float newMonthlyTokenLimitPerUser = monthlyLimit / (allUsers.Count + (userAdded ? 1 : -1));
				int diff = (int)Math.Ceiling(curMonthlyTokenLimitPerUser - newMonthlyTokenLimitPerUser);

				// Add or subtract the difference in tokens from all current users
				foreach (var user in allUsers.Values)
				{
					user.AvailableTokens -= diff;
					if (user.AvailableTokens < 0) user.AvailableTokens = 0;
				}

				// If user was added, add them with their full monthly limit. Otherwise, remove them
				if (userAdded)
				{
					allUsers[changedUserID] = new ChatGPTUser { AvailableTokens = (int)Math.Ceiling(newMonthlyTokenLimitPerUser) };
				}
				else
				{
					allUsers.Remove(changedUserID);
				}

				// Update the users file
				_fileAccessHelper.SaveFileJSON(ChatGPTUsersFile, allUsers);
			}
		}

		private void DeductUserTokens(Dictionary<ulong, ChatGPTUser> allUsers, ulong id, int tokens)
		{
			// Ensure only one thread at a time can make changes to our dictionary values' properties
			lock (_userFileLock)
			{
				// First, log the usage
				allUsers[id].TokensUsed.Add(DateTime.Now, tokens);

				// If they have enough available, deduct from that.
				if (allUsers[id].AvailableTokens >= tokens)
				{
					allUsers[id].AvailableTokens -= tokens;
				}
				// If not, deduct what we can and borrow the rest
				else
				{
					tokens -= allUsers[id].AvailableTokens;
					allUsers[id].AvailableTokens = 0;

					// Borrow from ALL least active users with borrowable tokens. Group and sort by rounding AvailableTokens down to the nearest 1,000.
					var borroweeGroup = allUsers
						.Where(u => u.Key != id && u.Value.BorrowableTokens > 0)
						.GroupBy(u => Math.Floor(u.Value.AvailableTokens / 1000f))
						.Order().First().ToList();

					// Borrow the floor of the divided amount from each user, then distribute the remainder 1 by 1 at random
					int dividedAmtFloor = (int)Math.Floor((float)tokens / borroweeGroup.Count);
					borroweeGroup.ForEach(b =>
					{
						if (!b.Value.LentTokens.ContainsKey(id)) b.Value.LentTokens[id] = 0;
						b.Value.LentTokens[id] += dividedAmtFloor;
					});
					borroweeGroup.Sort((_, _) => Random.Shared.Next(2) == 0 ? -1 : 1);
					for (int remainder = tokens - (dividedAmtFloor * borroweeGroup.Count); remainder > 0; remainder--)
					{
						borroweeGroup[remainder].Value.LentTokens[id] += 1;
					}
				}

				// Finally, save the file
				_fileAccessHelper.SaveFileJSON(ChatGPTUsersFile, allUsers);
			}
		}
	}
}