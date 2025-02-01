using Discord;
using OpenAI.Chat;
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
		private Mock<IChatGPTConnection> _chatGPTConnectionMock;

		private ChatGPTUser _sutUser;
		private Mock<IMessage> _sutMessage;
		private List<ChatMessage> _capturedChatMessages;
		private ChatCompletion _fakeChatCompletionResponse;
		private Dictionary<ulong, ChatGPTUser> _fakeUsers;
		private List<ChatGPTChannelMessage> _fakeHistoryList;

		[TestInitialize]
		public void TestInit()
		{
			_restClientProviderMock = new Mock<IDiscordRestClientProvider>();
			_fileAccessHelperMock = new Mock<IFileAccessHelper>();
			_chatGPTConnectionMock = new Mock<IChatGPTConnection>();
			_fakeChatCompletionResponse = OpenAIChatModelFactory.ChatCompletion(content: [ChatMessageContentPart.CreateTextPart("Test response.")]);

			// Prepare our mocked openAI calls
			_chatGPTConnectionMock.Setup(m => m.SendMessages(It.IsAny<List<ChatMessage>>()))
				.Callback<List<ChatMessage>>(m => _capturedChatMessages = m)
				.Returns(_fakeChatCompletionResponse);

			// Always start with our test user in the fake user list, and populate important properties of the message object
			var _fakeMessageAuthor = new Mock<IUser>();
			_fakeMessageAuthor.SetupGet(m => m.Username).Returns("Shaosil");
			_sutMessage = new Mock<IMessage> { DefaultValue = DefaultValue.Mock };
			_sutMessage.SetupGet(m => m.Content).Returns("!c Test message content");
			_sutMessage.SetupGet(m => m.Author).Returns(_fakeMessageAuthor.Object);
			_sutUser = new ChatGPTUser { AvailableInputTokens = 1000, AvailableOutputTokens = 500 };
			_fakeUsers = new() { { 0, _sutUser } };
			_fakeHistoryList = new();
			_fileAccessHelperMock.Setup(m => m.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTProvider.ChatGPTUsersFile, false)).Returns(() => _fakeUsers);
			_fileAccessHelperMock.Setup(m => m.LoadFileJSON<Dictionary<ulong, Queue<ChatGPTChannelMessage>>>(ChatGPTProvider.ChatLogFile, false))
				.Returns(() => new Dictionary<ulong, Queue<ChatGPTChannelMessage>> { { 0, new Queue<ChatGPTChannelMessage>(_fakeHistoryList) } });

			SUT = new ChatGPTProvider(Logger, _chatGPTConnectionMock.Object, _restClientProviderMock.Object, _fileAccessHelperMock.Object, Configuration);
		}

		[TestMethod]
		[DataRow(1000000, 333333)]
		[DataRow(987654321, 1234567)]
		public async Task ResetBuckets_CalculatesCorrectlyAsync(int allowedMonthlyInputTokens, int allowedMonthlyOutputTokens)
		{
			// Arrange - Create a fake guild and 10 users
			Configuration["ChatGPTMonthlyTokenInputLimit"] = $"{allowedMonthlyInputTokens}";
			Configuration["ChatGPTMonthlyTokenOutputLimit"] = $"{allowedMonthlyOutputTokens}";
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
			int calculatedInputTokens = (int)Math.Ceiling((float)allowedMonthlyInputTokens / mockUsers.Count);
			int calculatedOutputTokens = (int)Math.Ceiling((float)allowedMonthlyOutputTokens / mockUsers.Count);
			Assert.IsTrue(capturedContent.All(c => c.Value.AvailableInputTokens == calculatedInputTokens));
			Assert.IsTrue(capturedContent.All(c => c.Value.AvailableOutputTokens == calculatedOutputTokens));
		}

		[TestMethod]
		public async Task ChatRequest_BlocksUsersWithNoTokens()
		{
			// Arrange - Make sure our sut user who has no tokens
			_sutUser.AvailableInputTokens = 0;
			_sutUser.AvailableOutputTokens = 0;

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Assert - Verify NO calls to openAI occurred but a message was still sent to the channel
			_chatGPTConnectionMock.VerifyNoOtherCalls();
			_sutMessage.Verify(m => m.Channel.SendMessageAsync(It.IsAny<string>(), false, null, null, null, null, null, null, null, It.IsAny<MessageFlags>(), null), Times.Once);
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
			_chatGPTConnectionMock.VerifyAll();
			string content = _sutMessage.Object.Content[3..]; // Remove "!c "
			Assert.IsTrue(_capturedChatMessages.Any(m => m is UserChatMessage && m.Content[0].Text.Contains(content)));
		}

		[TestMethod]
		public async Task ChatRequest_SendsSystemMessageIfProvided()
		{
			// Arrange - Set up a system message string
			string systemMessage = "This is a test system message";

			// Act - Call chat twice - one with no system message, one with
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);
			var firstCapturedRequest = _capturedChatMessages;
			Configuration["ChatGPTSystemMessage"] = systemMessage;
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);
			var secondCapturedRequest = _capturedChatMessages;

			// Assert - Make sure chat was called twice, and the captured requests contain a system message if specified
			_chatGPTConnectionMock.Verify(m => m.SendMessages(It.IsAny<List<ChatMessage>>()), Times.Exactly(2));
			Assert.AreEqual(1, firstCapturedRequest.Count);
			Assert.AreEqual(2, secondCapturedRequest.Count);
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
			Assert.IsTrue(_capturedChatMessages.Any(m => m.Content[0].Text.Contains(_sutUser.CustomUserPrompt)));
			Assert.IsTrue(_capturedChatMessages.Any(m => m.Content[0].Text.Contains(_sutUser.CustomAssistantPrompt)));
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
			var recentXHistory = _fakeHistoryList.TakeLast(pairsToKeep).ToList();
			for (int i = 0; i < recentXHistory.Count; i++)
			{
				Assert.IsTrue(_capturedChatMessages.Any(m => m.Content[0].Text.Contains(recentXHistory[i].Message)));
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
			Assert.AreEqual(1, _capturedChatMessages.Count);
			_fileAccessHelperMock.Verify(m => m.SaveFileJSON(ChatGPTProvider.ChatLogFile, It.IsAny<Dictionary<ulong, Queue<ChatGPTChannelMessage>>>(), It.IsAny<bool>()), Times.Never);
		}

		[TestMethod]
		[DataRow(250, 100)]
		[DataRow(500, 250)]
		public async Task ChatRequest_DeductsUserTokensWhenAvailable(int responseInputTokenCost, int responseOutputTokenCost)
		{
			// Arrange - Store starting available and tell response to cost passed amount
			int startingInputTokens = _sutUser.AvailableInputTokens;
			int startingOutputTokens = _sutUser.AvailableOutputTokens;
			_fakeChatCompletionResponse = OpenAIChatModelFactory.ChatCompletion(
				usage: OpenAIChatModelFactory.ChatTokenUsage(responseOutputTokenCost, responseInputTokenCost, responseInputTokenCost + responseOutputTokenCost)
			);
			_chatGPTConnectionMock.Setup(c => c.SendMessages(It.IsAny<List<ChatMessage>>())).Returns(_fakeChatCompletionResponse);

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Arrange - Make sure the SUT user's tokens have been adjusted
			Assert.AreEqual(startingInputTokens - responseInputTokenCost, _sutUser.AvailableInputTokens);
			Assert.AreEqual(startingOutputTokens - responseOutputTokenCost, _sutUser.AvailableOutputTokens);
			_fileAccessHelperMock.Verify(m => m.SaveFileJSON(ChatGPTProvider.ChatGPTUsersFile, It.IsAny<Dictionary<ulong, ChatGPTUser>>(), It.IsAny<bool>()), Times.Once);
		}

		[TestMethod]
		[DataRow(333, 100)]
		[DataRow(500, 200)]
		[DataRow(1000, 500)]
		public async Task ChatRequest_BorrowsTokensFromInactiveUsers(int responseInputTokenCost, int responseOutputTokenCost)
		{
			// Arrange - Start with only 100/200 tokens, and set up other users (active and inactive)
			_sutUser.AvailableInputTokens = 100;
			_sutUser.AvailableOutputTokens = 50;
			List<ChatGPTUser> activeUsers = new();
			List<ChatGPTUser> inactiveUsers = new();
			for (int i = 0; i < 5; i++)
			{
				activeUsers.Add(new() { AvailableInputTokens = Random.Shared.Next(500, 1000), AvailableOutputTokens = Random.Shared.Next(200, 500) });
				_fakeUsers.Add(Random.Shared.NextULong(), activeUsers.Last());
			}
			for (int i = 0; i < 5; i++)
			{
				inactiveUsers.Add(new() { AvailableInputTokens = Random.Shared.Next(2000, 3000), AvailableOutputTokens = Random.Shared.Next(1000, 2000) });
				_fakeUsers.Add(Random.Shared.NextULong(), inactiveUsers.Last());
			}
			_fakeChatCompletionResponse = OpenAIChatModelFactory.ChatCompletion(
				usage: OpenAIChatModelFactory.ChatTokenUsage(responseOutputTokenCost, responseInputTokenCost, responseInputTokenCost + responseOutputTokenCost)
			);
			_chatGPTConnectionMock.Setup(c => c.SendMessages(It.IsAny<List<ChatMessage>>())).Returns(_fakeChatCompletionResponse);

			// Act - Call chat
			await SUT.HandleChatRequest(_sutMessage.Object, IChatGPTProvider.eMessageType.Message);

			// Assert - Ensure we have 0 tokens left, the active users' tokens were untouched, and the inactive users tokens were evenly borrowed
			Assert.AreEqual(0, _sutUser.AvailableInputTokens);
			Assert.AreEqual(0, _sutUser.AvailableOutputTokens);
			int totalBorrowedInputTokens = _fakeUsers.Sum(u => u.Value.LentInputTokens.Sum(t => t.Value));
			int totalBorrowedOutputTokens = _fakeUsers.Sum(u => u.Value.LentOutputTokens.Sum(t => t.Value));
			Assert.AreEqual(responseInputTokenCost - 100, totalBorrowedInputTokens);
			Assert.AreEqual(responseOutputTokenCost - 50, totalBorrowedOutputTokens);
			foreach (var activeUser in activeUsers)
			{
				Assert.IsTrue(activeUser.AvailableInputTokens >= 500);
				Assert.IsTrue(activeUser.AvailableOutputTokens >= 200);
				Assert.AreEqual(0, activeUser.LentInputTokens.Count);
				Assert.AreEqual(0, activeUser.LentOutputTokens.Count);
			}
			int dividedInputTokens = (int)Math.Floor((responseInputTokenCost - 100f) / inactiveUsers.Count);
			int dividedOutputTokens = (int)Math.Floor((responseOutputTokenCost - 200f) / inactiveUsers.Count);
			foreach (var inactiveUser in inactiveUsers)
			{
				Assert.AreNotEqual(inactiveUser.BorrowableTokens, inactiveUser.TotalAvailableTokens);
				Assert.AreEqual(1, inactiveUser.LentInputTokens.Count);
				Assert.AreEqual(1, inactiveUser.LentOutputTokens.Count);
			}
		}

		[TestMethod]
		[DataRow(1000000, 333333, true)]
		[DataRow(1000000, 333333, false)]
		[DataRow(123456, 1234, true)]
		[DataRow(123456, 1234, false)]
		public void AdjustBuckets_AddOrRemoveUserCalculatesCorrectly(int monthlyInputTokenLimit, int monthlyOutputTokenLimit, bool userAdded)
		{
			// Arrange - Set monthly limit, create 9 more users, and set everyone's initial tokens to some random amount
			Configuration["ChatGPTMonthlyTokenInputLimit"] = monthlyInputTokenLimit.ToString();
			Configuration["ChatGPTMonthlyTokenOutputLimit"] = monthlyOutputTokenLimit.ToString();
			for (int i = 0; i < 9; i++) _fakeUsers.Add(Random.Shared.NextULong(), new());
			Dictionary<ulong, KeyValuePair<int, int>> startingTokens = new();
			foreach (var user in _fakeUsers)
			{
				user.Value.AvailableInputTokens = Random.Shared.Next(10000, 100000);
				user.Value.AvailableOutputTokens = Random.Shared.Next(5000, 7000);
				startingTokens[user.Key] = new KeyValuePair<int, int>(user.Value.AvailableInputTokens, user.Value.AvailableOutputTokens);
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
			float oldDividedInputTokens = monthlyInputTokenLimit / (userAdded ? 10f : 11f);
			float newDividedInputTokens = monthlyInputTokenLimit / (userAdded ? 11f : 10f);
			float oldDividedOutputTokens = monthlyOutputTokenLimit / (userAdded ? 10f : 11f);
			float newDividedOutputTokens = monthlyOutputTokenLimit / (userAdded ? 11f : 10f);
			int inputDiff = (int)Math.Ceiling(oldDividedInputTokens - newDividedInputTokens);
			int outputDiff = (int)Math.Ceiling(oldDividedOutputTokens - newDividedOutputTokens);
			if (userAdded)
			{
				Assert.AreEqual(Math.Ceiling(newDividedInputTokens), capturedUsers[newUserID].AvailableInputTokens);
				Assert.AreEqual(Math.Ceiling(newDividedOutputTokens), capturedUsers[newUserID].AvailableOutputTokens);
			}
			foreach (var user in capturedUsers.Where(u => u.Key != newUserID))
			{
				Assert.AreEqual(startingTokens[user.Key].Key - inputDiff, user.Value.AvailableInputTokens);
				Assert.AreEqual(startingTokens[user.Key].Value - outputDiff, user.Value.AvailableOutputTokens);
			}
		}
	}
}