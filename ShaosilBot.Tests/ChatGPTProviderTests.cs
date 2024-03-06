using Discord;
using OpenAI.Interfaces;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels.ResponseModels;
using OpenAI.ObjectModels.SharedModels;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using ShaosilBot.Core.Singletons;

namespace ShaosilBot.Tests
{
	[TestClass]
	public class ChatGPTProviderTests : TestBase<ChatGPTProvider>
	{
		private Mock<IDiscordRestClientProvider> _restClientProviderMock;
		private Mock<IFileAccessHelper> _fileAccessHelperMock;
		private Mock<IChatCompletionService> _openAIChatCompletionServiceMock;

		private ChatGPTUser _sutUser;
		private Mock<IMessage> _sutMessage;
		private ChatCompletionCreateRequest _capturedChatRequest;
		private ChatCompletionCreateResponse _fakeChatResponse;
		private Dictionary<ulong, ChatGPTUser> _fakeUsers;
		private List<ChatGPTChannelMessage> _fakeHistoryList;

		[TestInitialize]
		public void TestInit()
		{
			_restClientProviderMock = new Mock<IDiscordRestClientProvider>();
			_fileAccessHelperMock = new Mock<IFileAccessHelper>();

			// Prepare our mocked openAI calls
			var openAIServiceMock = new Mock<IOpenAIService>();
			_fakeChatResponse = new() { Choices = new() };
			_fakeChatResponse.Choices.Add(new ChatChoiceResponse { Message = new ChatMessage(StaticValues.ChatMessageRoles.Assistant, "This is a test response message") });
			_openAIChatCompletionServiceMock = new Mock<IChatCompletionService>();
			_openAIChatCompletionServiceMock.Setup(m => m.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), default))
				.Callback<ChatCompletionCreateRequest, string, CancellationToken>((r, m, c) => _capturedChatRequest = r).ReturnsAsync(() => _fakeChatResponse);
			openAIServiceMock.SetupGet(m => m.ChatCompletion).Returns(_openAIChatCompletionServiceMock.Object);

			// Always start with our test user in the fake user list, and populate important properties of the message object
			var _fakeMessageAuthor = new Mock<IUser>();
			_fakeMessageAuthor.SetupGet(m => m.Username).Returns("Shaosil");
			_sutMessage = new Mock<IMessage> { DefaultValue = DefaultValue.Mock };
			_sutMessage.SetupGet(m => m.Content).Returns("!c Test message content");
			_sutMessage.SetupGet(m => m.Author).Returns(_fakeMessageAuthor.Object);
			_sutUser = new ChatGPTUser { AvailableTokens = 1000 };
			_fakeUsers = new() { { 0, _sutUser } };
			_fakeHistoryList = new();
			_fileAccessHelperMock.Setup(m => m.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTProvider.ChatGPTUsersFile, false)).Returns(() => _fakeUsers);
			_fileAccessHelperMock.Setup(m => m.LoadFileJSON<Dictionary<ulong, Queue<ChatGPTChannelMessage>>>(ChatGPTProvider.ChatLogFile, false))
				.Returns(() => new Dictionary<ulong, Queue<ChatGPTChannelMessage>> { { 0, new Queue<ChatGPTChannelMessage>(_fakeHistoryList) } });

			SUT = new ChatGPTProvider(Logger, _restClientProviderMock.Object, _fileAccessHelperMock.Object, Configuration, openAIServiceMock.Object);
		}

		[TestMethod]
		[DataRow(100000)]
		[DataRow(987654321)]
		public async Task ResetBuckets_CalculatesCorrectlyAsync(int allowedMonthlyTokens)
		{
			// Arrange - Create a fake guild and 10 users
			Configuration["ChatGPTMonthlyTokenLimit"] = $"{allowedMonthlyTokens}";
			var mockChannel = new Mock<ITextChannel>();
			var mockGuild = new Mock<IGuild>();
			var mockUsers = new List<IGuildUser>();
			int targetNumUsers = Random.Shared.Next(10, 101);
			for (int i = 0; i < targetNumUsers; i++)
			{
				var user = new Mock<IGuildUser>();
				ulong randID = Random.Shared.NextULong();
				user.SetupGet(m => m.Id).Returns(randID);
				mockUsers.Add(user.Object);
			}
			mockGuild.Setup(m => m.GetUsersAsync(CacheMode.AllowDownload, null)).ReturnsAsync(mockUsers);
			mockGuild.Setup(m => m.GetChannelAsync(0, CacheMode.AllowDownload, null)).ReturnsAsync(mockChannel.Object);
			_restClientProviderMock.SetupGet(m => m.Guilds).Returns(new List<IGuild> { mockGuild.Object });

			// Act and capture users file
			var capturedContent = new Dictionary<ulong, ChatGPTUser>();
			_fileAccessHelperMock.Setup(m => m.SaveFileJSON(ChatGPTProvider.ChatGPTUsersFile, It.IsAny<Dictionary<ulong, ChatGPTUser>>(), It.IsAny<bool>()))
				.Callback<string, Dictionary<ulong, ChatGPTUser>, bool>((filename, content, releaseLease) => capturedContent = content);
			await SUT.ResetAndFillAllUserBuckets();

			// Assert - User buckets should have been filled evenly with our allowed tokens (rounded up)
			Assert.IsTrue(capturedContent.Count >= 10);
			int calculatedTokens = (int)Math.Ceiling((float)allowedMonthlyTokens / mockUsers.Count);
			Assert.IsTrue(capturedContent.All(c => c.Value.AvailableTokens == calculatedTokens));
		}

		[TestMethod]
		public async Task ChatRequest_BlocksUsersWithNoTokens()
		{
			// Arrange - Make sure our sut user who has no tokens
			_sutUser.AvailableTokens = 0;

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Assert - Verify NO calls to openAI occurred but a message was still sent to the channel
			_openAIChatCompletionServiceMock.VerifyNoOtherCalls();
			_sutMessage.Verify(m => m.Channel.SendMessageAsync(It.IsAny<string>(), false, null, null, null, null, null, null, null, It.IsAny<MessageFlags>()), Times.Once);
		}

		[TestMethod]
		public async Task ChatRequest_SendsAndDisposesTypingSignal()
		{
			// Arrange - N/A

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Assert - Make sure our channel received a call to EnterTypingState and Dispose
			_sutMessage.Verify(m => m.Channel.EnterTypingState(null), Times.Once);
			var mockDisposable = Mock.Get(_sutMessage.Object.Channel.EnterTypingState());
			mockDisposable.Verify(m => m.Dispose(), Times.Once);
		}

		[TestMethod]
		public async Task ChatRequest_CallsOpenAIServce()
		{
			// Arrange - N/A

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Arrange - Make sure we send the request with the specified content
			_openAIChatCompletionServiceMock.VerifyAll();
			string content = _sutMessage.Object.Content[3..]; // Remove "!c "
			Assert.IsTrue(_capturedChatRequest.Messages.Any(m => m.Role == StaticValues.ChatMessageRoles.User && m.Content.Contains(content)));
		}

		[TestMethod]
		public async Task ChatRequest_SendsSystemMessageIfProvided()
		{
			// Arrange - Set up a system message string
			string systemMessage = "This is a test system message";

			// Act - Call chat twice - one with no system message, one with
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);
			var firstCapturedRequest = _capturedChatRequest;
			Configuration["ChatGPTSystemMessage"] = systemMessage;
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);
			var secondCapturedRequest = _capturedChatRequest;

			// Assert - Make sure chat was called twice, and the captured requests contain a system message if specified
			_openAIChatCompletionServiceMock.Verify(m => m.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), default), Times.Exactly(2));
			Assert.AreEqual(1, firstCapturedRequest.Messages.Count);
			Assert.AreEqual(StaticValues.ChatMessageRoles.User, firstCapturedRequest.Messages[0].Role);
			Assert.AreEqual(2, secondCapturedRequest.Messages.Count);
			Assert.AreEqual(StaticValues.ChatMessageRoles.System, secondCapturedRequest.Messages[0].Role);
			Assert.AreEqual(StaticValues.ChatMessageRoles.User, secondCapturedRequest.Messages[1].Role);
		}

		[TestMethod]
		public async Task ChatRequest_SendsTokenLimitIfProvided()
		{
			// Arrange - Set a token limit int
			int tokenLimit = 1000;

			// Act - Call chat twice, one with no limit and one with
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);
			var firstCapturedRequest = _capturedChatRequest;
			Configuration["ChatGPTMessageTokenLimit"] = tokenLimit.ToString();
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);
			var secondCapturedRequest = _capturedChatRequest;

			// Assert - Ensure either no max was sent or the specified limit was
			_openAIChatCompletionServiceMock.Verify(m => m.CreateCompletion(It.IsAny<ChatCompletionCreateRequest>(), It.IsAny<string>(), default), Times.Exactly(2));
			Assert.IsFalse(firstCapturedRequest.MaxTokens.HasValue);
			Assert.AreEqual(tokenLimit, secondCapturedRequest.MaxTokens);
		}

		[TestMethod]
		public async Task ChatRequest_SendsCustomPromptIfExists()
		{
			// Arrange - Provide a custom prompt
			_sutUser.CustomUserPrompt = "Hello from the custom user prompt";
			_sutUser.CustomAssistantPrompt = "Hello from the custom assistant prompt";

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Assert - Make sure our messages contain the custom prompts
			Assert.IsTrue(_capturedChatRequest.Messages.Any(m => m.Role == StaticValues.ChatMessageRoles.User && m.Content.Contains(_sutUser.CustomUserPrompt)));
			Assert.IsTrue(_capturedChatRequest.Messages.Any(m => m.Role == StaticValues.ChatMessageRoles.Assistant && m.Content.Contains(_sutUser.CustomAssistantPrompt)));
		}

		[TestMethod]
		[DataRow(2)]
		[DataRow(10)]
		public async Task ChatRequest_SendsHistoryIfExistsAndEnabled(int pairsToKeep)
		{
			// Arrange - Set history records to X and prepare more history than that
			var botUser = new Mock<IUser>();
			botUser.SetupGet(m => m.Id).Returns(1);
			_restClientProviderMock.SetupGet(m => m.BotUser).Returns(botUser.Object);
			for (int i = 0; i < pairsToKeep + 5; i++) _fakeHistoryList.Add(new ChatGPTChannelMessage { Message = $"{Guid.NewGuid()}" });
			Configuration["ChatGPTMessagePairsToKeep"] = pairsToKeep.ToString();

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Assert - Ensure ONLY X most recent historical records were sent
			var userMessages = _capturedChatRequest.Messages.Where(c => c.Role == StaticValues.ChatMessageRoles.User).ToList();
			var recentXHistory = _fakeHistoryList.TakeLast(pairsToKeep).ToList();
			Assert.AreEqual(pairsToKeep + 1, userMessages.Count);
			for (int i = 0; i < recentXHistory.Count; i++)
			{
				Assert.IsTrue(userMessages.Any(m => m.Content.Contains(recentXHistory[i].Message)));
			}
			_fileAccessHelperMock.Verify(m => m.SaveFileJSON(ChatGPTProvider.ChatLogFile, It.IsAny<Dictionary<ulong, Queue<ChatGPTChannelMessage>>>(), It.IsAny<bool>()), Times.Once);
		}

		[TestMethod]
		public async Task ChatRequest_DoesNotSendHistoryIfDisabled()
		{
			// Arrange - Set history records to 0 (default) and prepare some history
			var botUser = new Mock<IUser>();
			botUser.SetupGet(m => m.Id).Returns(1);
			_restClientProviderMock.SetupGet(m => m.BotUser).Returns(botUser.Object);
			for (int i = 0; i < 3; i++) _fakeHistoryList.Add(new ChatGPTChannelMessage { Message = $"{Guid.NewGuid()}" });

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Assert - No history should have been included or saved
			var userMessages = _capturedChatRequest.Messages.Where(c => c.Role == StaticValues.ChatMessageRoles.User).ToList();
			Assert.AreEqual(1, userMessages.Count);
			_fileAccessHelperMock.Verify(m => m.SaveFileJSON(ChatGPTProvider.ChatLogFile, It.IsAny<Dictionary<ulong, Queue<ChatGPTChannelMessage>>>(), It.IsAny<bool>()), Times.Never);
		}

		[TestMethod]
		[DataRow(250)]
		[DataRow(500)]
		public async Task ChatRequest_DeductsUserTokensWhenAvailable(int responseTokenCost)
		{
			// Arrange - Store starting available and tell response to cost passed amount
			int startingTokens = _sutUser.AvailableTokens;
			_fakeChatResponse.Usage = new() { TotalTokens = responseTokenCost };

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Arrange - Make sure the SUT user's tokens have been adjusted
			Assert.AreEqual(startingTokens - responseTokenCost, _sutUser.AvailableTokens);
			_fileAccessHelperMock.Verify(m => m.SaveFileJSON(ChatGPTProvider.ChatGPTUsersFile, It.IsAny<Dictionary<ulong, ChatGPTUser>>(), It.IsAny<bool>()), Times.Once);
		}

		[TestMethod]
		[DataRow(333)]
		[DataRow(500)]
		[DataRow(1000)]
		public async Task ChatRequest_BorrowsTokensFromInactiveUsers(int responseTokenCost)
		{
			// Arrange - Start with only 100 tokens, and set up other users (active and inactive)
			_sutUser.AvailableTokens = 100;
			List<ChatGPTUser> activeUsers = new();
			List<ChatGPTUser> inactiveUsers = new();
			for (int i = 0; i < 5; i++)
			{
				activeUsers.Add(new() { AvailableTokens = Random.Shared.Next(500, 1000) });
				_fakeUsers.Add(Random.Shared.NextULong(), activeUsers.Last());
			}
			for (int i = 0; i < 5; i++)
			{
				inactiveUsers.Add(new() { AvailableTokens = Random.Shared.Next(2000, 3000) });
				_fakeUsers.Add(Random.Shared.NextULong(), inactiveUsers.Last());
			}
			_fakeChatResponse.Usage = new() { TotalTokens = responseTokenCost };

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Assert - Ensure we have 0 tokens left, the active users' tokens were untouched, and the inactive users tokens were evenly borrowed
			Assert.AreEqual(0, _sutUser.AvailableTokens);
			int totalBorrowedTokens = _fakeUsers.Sum(u => u.Value.LentTokens.Sum(t => t.Value));
			Assert.AreEqual(responseTokenCost - 100, totalBorrowedTokens);
			foreach (var activeUser in activeUsers)
			{
				Assert.IsTrue(activeUser.AvailableTokens >= 500);
				Assert.AreEqual(0, activeUser.LentTokens.Count);
			}
			int dividedTokens = (int)Math.Floor((responseTokenCost - 100f) / inactiveUsers.Count);
			foreach (var inactiveUser in inactiveUsers)
			{
				Assert.IsTrue(Math.Abs(inactiveUser.AvailableTokens - dividedTokens - inactiveUser.BorrowableTokens) < 2);
				Assert.AreEqual(1, inactiveUser.LentTokens.Count);
			}
		}

		[TestMethod]
		[DataRow(100000, true)]
		[DataRow(100000, false)]
		[DataRow(123456, true)]
		[DataRow(123456, false)]
		public void AdjustBuckets_AddOrRemoveUserCalculatesCorrectly(int monthlyTokenLimit, bool userAdded)
		{
			// Arrange - Set monthly limit, create 9 more users, and set everyone's initial tokens to some random amount
			Configuration["ChatGPTMonthlyTokenLimit"] = monthlyTokenLimit.ToString();
			for (int i = 0; i < 9; i++) _fakeUsers.Add(Random.Shared.NextULong(), new());
			Dictionary<ulong, int> startingTokens = new();
			foreach (var user in _fakeUsers)
			{
				user.Value.AvailableTokens = Random.Shared.Next(10000, 100000);
				startingTokens[user.Key] = user.Value.AvailableTokens;
			}

			// Act - Update buckets and capture save
			ulong newUserID = Random.Shared.NextULong();
			if (!userAdded)
			{
				_fakeUsers.Add(newUserID, new());
			}
			var capturedUsers = new Dictionary<ulong, ChatGPTUser>();
			_fileAccessHelperMock.Setup(m => m.SaveFileJSON(ChatGPTProvider.ChatGPTUsersFile, It.IsAny<Dictionary<ulong, ChatGPTUser>>(), It.IsAny<bool>()))
				.Callback<string, Dictionary<ulong, ChatGPTUser>, bool>((file, content, release) => capturedUsers = content);
			SUT.UpdateAllUserBuckets(newUserID, userAdded);

			// Assert - Ensure everyone's tokens were adjusted, the new user has the starting amount, and the file was saved
			float oldDividedTokens = monthlyTokenLimit / (userAdded ? 10f : 11f);
			float newDividedTokens = monthlyTokenLimit / (userAdded ? 11f : 10f);
			int diff = (int)Math.Ceiling(oldDividedTokens - newDividedTokens);
			if (userAdded)
			{
				Assert.AreEqual(Math.Ceiling(newDividedTokens), capturedUsers[newUserID].AvailableTokens);
			}
			foreach (var user in capturedUsers.Where(u => u.Key != newUserID))
			{
				Assert.AreEqual(startingTokens[user.Key] - diff, user.Value.AvailableTokens);
			}
		}
	}
}