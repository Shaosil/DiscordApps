using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.GPT3.Interfaces;
using OpenAI.GPT3.ObjectModels.RequestModels;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;

namespace ShaosilBot.Core.Singletons
{
	public class ChatGPTProvider : IChatGPTProvider
	{
		private const string ChatGPTUsersFile = "ChatGPTUsers.json";
		private readonly ILogger<ChatGPTProvider> _logger;
		private readonly IFileAccessHelper _fileAccessHelper;
		private readonly IConfiguration _configuration;
		private readonly IOpenAIService _openAIService;
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly Dictionary<ulong, ChatGPTUser> _allUsers;
		private class TypingState { public IDisposable? State; }
		private Dictionary<ulong, TypingState> _typingInstances = new Dictionary<ulong, TypingState>();

		public ChatGPTProvider(ILogger<ChatGPTProvider> logger, IFileAccessHelper fileAccessHelper, IConfiguration configuration, IOpenAIService openAIService, IDiscordRestClientProvider restClientProvider)
		{
			_logger = logger;
			_fileAccessHelper = fileAccessHelper;
			_configuration = configuration;
			_openAIService = openAIService;
			_restClientProvider = restClientProvider;

			// Load all users once for the lifetime of the app. Anytime a change is made, simply overwrite the file
			_allUsers = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTUsersFile, true);
			if (!_allUsers.Any()) FillAllUserBuckets();
			_fileAccessHelper.ReleaseFileLease(ChatGPTUsersFile);
		}

		public async Task HandleChatRequest(SocketMessage message)
		{
			// Make sure the author hasn't surpassed the token limit
			if (!UserHasTokens(message.Author.Id))
			{
				await message.Channel.SendMessageAsync("(WIP) You have no remaining tokens. Check back later.");
				return;
			}

			try
			{
				SetTypingLock(true, message.Channel);

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
						// Todo: Store previous messages
						ChatMessage.FromSystem(_configuration.GetValue<string>("ChatGPTSystemMessage")!),
						ChatMessage.FromUser($"Message from {message.Author.Username}:\n\n{sanitizedMessage}")
					},
					MaxTokens = messageTokenLimit
				});

				// Deduct user tokens if any were used
				if ((response.Usage?.TotalTokens ?? 0) > 0) DeductUserTokens(message.Author.Id, response.Usage!.TotalTokens);

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
				await message.Channel.SendMessageAsync("*[Internal exception occurred. <@392127164570664962>, check the logs.]*");
			}
			finally
			{
				SetTypingLock(false, message.Channel);
			}
		}

		public async Task SendChatMessage(ISocketMessageChannel channel, string prompt)
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

		private void SetTypingLock(bool typing, ISocketMessageChannel channel)
		{
			// Add the channel key to the Dictionary if needed
			if (!_typingInstances.ContainsKey(channel.Id)) _typingInstances[channel.Id] = new();

			// Either enter the typing state or leave it
			lock (_typingInstances[channel.Id])
			{
				if (typing) _typingInstances[channel.Id].State = channel.EnterTypingState();
				else _typingInstances[channel.Id].State!.Dispose();
			}
		}

		public void FillAllUserBuckets()
		{
			// Load all current users and give all non-bots full tokens - This may take a while on large servers
			ulong guildID = _configuration.GetValue<ulong>("TargetGuild");
			var guild = _restClientProvider.Client.GetGuildAsync(guildID).Result;
			var guildUsers = (guild.GetUsersAsync().FlattenAsync().Result).Where(u => !u.IsBot).ToList();

			// Calculate how many tokes each user should have (round up)
			int totalMonthlyTokensPerUser = (int)Math.Ceiling(_configuration.GetValue<float>("ChatGPTMonthlyTokenLimit") / guildUsers.Count);
			var serialized = guildUsers.ToDictionary(u => u.Id, u => new ChatGPTUser { AvailableTokens = totalMonthlyTokensPerUser });
			_fileAccessHelper.SaveFileJSON(ChatGPTUsersFile, serialized, false);
		}

		public void UpdateAllUserBuckets(ulong id, bool userAdded)
		{
			lock (_allUsers)
			{
				float monthlyLimit = _configuration.GetValue<float>("ChatGPTMonthlyTokenLimit");
				float curMonthlyTokenLimitPerUser = monthlyLimit / _allUsers.Count;
				float newMonthlyTokenLimitPerUser = monthlyLimit / (_allUsers.Count + (userAdded ? 1 : -1));
				int diff = (int)Math.Ceiling(curMonthlyTokenLimitPerUser - newMonthlyTokenLimitPerUser);

				// Add or subtract the difference in tokens from all current users
				foreach (var user in _allUsers.Values)
				{
					user.AvailableTokens -= diff;
					if (user.AvailableTokens < 0) user.AvailableTokens = 0;
				}

				// If user was added, add them with their full monthly limit. Otherwise, remove them
				if (userAdded)
				{
					_allUsers[id] = new ChatGPTUser { AvailableTokens = (int)Math.Ceiling(newMonthlyTokenLimitPerUser) };
				}
				else
				{
					_allUsers.Remove(id);
				}

				// Update the users file
				_fileAccessHelper.SaveFileJSON(ChatGPTUsersFile, _allUsers);
			}
		}

		private bool UserHasTokens(ulong id)
		{
			/*
				Each user will have a full bucket of tokens at the start of each month. based on amount of users I currently have. If users join
				or leave, I will adjust everyone's buckets accordingly. When a user does an action that requires tokens, I will deduct them as needed.
				If certain users are active and use their tokens up, further token requests will "steal" from the least active users' buckets divided evenly.
				The borrowed tokens will remain in the affected users' buckets but be flagged as borrowed. If/when those users use their own tokens, they will
				use their unborrowed tokens first, but they may use tokens flagged as borrowed if they run out of unborrowed tokens.
				Active users will only be able to borrow unborrowed tokens.
			*/

			// If a user has at least 100 tokens OR the total amount of unborrowed tokens > 1000
			return _allUsers[id].AvailableTokens >= 100 || _allUsers.Where(u => u.Key != id).Sum(u => u.Value!.BorrowableTokens) > 1000;
		}

		private void DeductUserTokens(ulong id, int tokens)
		{
			// Ensure only one thread at a time can make changes to our dictionary values' properties
			lock (_allUsers)
			{
				// First, log the usage
				_allUsers[id].TokensUsed.Add(DateTime.Now, tokens);

				// If they have enough available, deduct from that.
				if (_allUsers[id].AvailableTokens >= tokens)
				{
					_allUsers[id].AvailableTokens -= tokens;
				}
				// If not, deduct what we can and borrow the rest
				else
				{
					tokens -= _allUsers[id].AvailableTokens;
					_allUsers[id].AvailableTokens = 0;

					// Borrow from ALL least active users with borrowable tokens. Group and sort by rounding AvailableTokens down to the nearest 1,000.
					var borroweeGroup = _allUsers
						.Where(u => u.Key != id && u.Value.BorrowableTokens > 0)
						.GroupBy(u => Math.Floor(u.Value.AvailableTokens / 1000f))
						.Order()
						.First().ToList();

					// Borrow the floor of the divided amount from each user, then just loop through adding 1 until the remainder is finished
					float dividedAmt = (float)tokens / borroweeGroup.Count;
					borroweeGroup.ForEach(b => b.Value!.BorrowedTokens += (int)Math.Floor(dividedAmt));
					int remainder = tokens - ((int)Math.Floor(dividedAmt) * borroweeGroup.Count);
					int curIdx = 0;
					while (remainder > 0)
					{
						borroweeGroup[curIdx++].Value!.BorrowedTokens += remainder--;
						if (curIdx >= borroweeGroup.Count) curIdx = 0;
					}
				}

				// Finally, save the file
				_fileAccessHelper.SaveFileJSON(ChatGPTUsersFile, _allUsers);
			}
		}
	}
}