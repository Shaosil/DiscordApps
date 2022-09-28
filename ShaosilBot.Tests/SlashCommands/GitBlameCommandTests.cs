using Discord;
using Moq;
using ShaosilBot.Models;
using ShaosilBot.SlashCommands;
using ShaosilBot.Tests.Models;
using System.Text.Json;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
    public class GitBlameCommandTests : SlashCommandTestBase<GitBlameCommand>
    {
		private static List<SimpleDiscordUser> _blameables = new List<SimpleDiscordUser>();
		private List<string> _preppedResponses = new List<string>();

		protected override GitBlameCommand GetInstance() => new GitBlameCommand(CommandLoggerMock.Object, HttpUtilitiesMock.Object, DataBlobProviderMock.Object);

		[ClassInitialize]
		public static new void ClassInitialize(TestContext context)
		{
			// Init blameables and return them serialized when asked
			for (int i = 0; i < 10; i++)
				_blameables.Add(new SimpleDiscordUser { ID = Random.Shared.NextULong(), FriendlyName = $"Friendly name {i + 1}" });
			DataBlobProviderMock.Setup(m => m.GetBlobTextAsync(GitBlameCommand.BlameablesFilename, It.IsAny<bool>())).ReturnsAsync(JsonSerializer.Serialize(_blameables));
		}

		[TestInitialize]
		public override void TestInitialize()
		{
			base.TestInitialize();

			// Fake URL for response image
			string fakeImageUrl = "THIS IS A FAKE IMAGE URL FOR UNIT TESTING";
			HttpUtilitiesMock.Setup(m => m.GetRandomGitBlameImage()).ReturnsAsync(fakeImageUrl);

			// Fake response texts
			_preppedResponses = new List<string>();
			for (int i = 0; i < 10; i++) _preppedResponses.Add($"{{USER}} BLAME RESPONSE {i + 1}");
			DataBlobProviderMock.Setup(m => m.GetBlobTextAsync(GitBlameCommand.ResponsesFilename, It.IsAny<bool>())).ReturnsAsync(string.Join(Environment.NewLine, _preppedResponses));
		}

		[TestMethod]
		public async Task SimpleBlame_Succeeds()
		{
			// Arrange - Build command with no options
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(DataBlobProviderMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.Text);

			// Act
			await RunInteractions(request);

			// Assert - A prepared user and response should both be contained in the captured followup response
			Assert.IsTrue(preppedUsers.Any(u => FollowupResponseCapture.Contains(u.FriendlyName)));
			Assert.IsTrue(_preppedResponses.Any(r => FollowupResponseCapture.Contains(r.Replace("{USER}", string.Empty))));
		}

		[TestMethod]
		public async Task NoBlameablesCanViewChannel_FailsSmoothly()
		{
			// Arrange - Build command with no options and ensure users have no permissions
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(DataBlobProviderMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.None);

			// Act
			await RunInteractions(request);

			// Assert - Response should still contain a message but NOT contain a user or response
			Assert.IsFalse(string.IsNullOrEmpty(FollowupResponseCapture));
			Assert.IsFalse(preppedUsers.Any(u => FollowupResponseCapture.Contains(u.FriendlyName)));
			Assert.IsFalse(_preppedResponses.Any(r => FollowupResponseCapture.Contains(r.Replace("{USER}", string.Empty))));
		}

		[TestMethod]
		public async Task ErrorFetchingImages_FailsSmoothly()
		{
			// Arrange - Build command with no options and ensure HTTP call throws exception
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);
			HttpUtilitiesMock.Setup(m => m.GetRandomGitBlameImage()).Throws(new Exception());
			Helpers.GenerateSimpleDiscordUsers(DataBlobProviderMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.None);

			// Act
			await RunInteractions(request);

			// Assert - Response should contain a message anyway
			Assert.IsFalse(string.IsNullOrWhiteSpace(FollowupResponseCapture));
		}

		[TestMethod]
		public async Task TargetedBlame_WorksAndNotifies()
		{
			// Arrange - Build command with mocked target option
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(DataBlobProviderMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.Text);
			var randomSimpleUser = preppedUsers[Random.Shared.Next(preppedUsers.Count)];
			var targetUser = GuildMock.Object.GetUserAsync(randomSimpleUser.ID).Result;
			AddOption("target-user", targetUser);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);

			// Act
			await RunInteractions(request);

			// Assert - Ensure response contains target user, a random response "image", and the word "Targeted"
			Assert.IsTrue(FollowupResponseCapture.Contains(targetUser.Mention));
			Assert.IsTrue(_preppedResponses.Any(r => FollowupResponseCapture.Contains(r.Replace("{USER}", string.Empty))));
			Assert.IsTrue(FollowupResponseCapture.Contains("Targeted"));
		}

		[TestMethod]
		public async Task TargetedNoAccessUser_DoesNotBlame()
		{
			// Arrange - Build command with mock target and ensure users have no permissions
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(DataBlobProviderMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.None);
			var randomSimpleUser = preppedUsers[Random.Shared.Next(preppedUsers.Count)];
			var targetUser = GuildMock.Object.GetUserAsync(randomSimpleUser.ID).Result;
			AddOption("target-user", targetUser);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);

			// Act
			await RunInteractions(request);

			// Assert - Response should still contain a message with the target user but NOT contain a response URL
			Assert.IsFalse(string.IsNullOrEmpty(FollowupResponseCapture));
			Assert.IsTrue(FollowupResponseCapture.Contains(targetUser.Mention));
			Assert.IsFalse(_preppedResponses.Any(r => FollowupResponseCapture.Contains(r.Replace("{USER}", string.Empty))));
		}

		[TestMethod]
		public async Task TargetedSelf_SpecialBlame()
		{
			// Arrange - Build command with mock users and ensure target user is the caller
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(DataBlobProviderMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.Text);
			AddOption("target-user", UserMock.Object);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);

			// Act
			await RunInteractions(request);

			// Assert - Response should still contain a message with the target user but NOT contain a response URL or any other user
			Assert.IsFalse(string.IsNullOrEmpty(FollowupResponseCapture));
			Assert.IsTrue(FollowupResponseCapture.Contains(UserMock.Object.Mention));
			Assert.IsFalse(preppedUsers.Any(u => FollowupResponseCapture.Contains(u.FriendlyName)));
			Assert.IsFalse(_preppedResponses.Any(r => FollowupResponseCapture.Contains(r.Replace("{USER}", string.Empty))));
		}

		[TestMethod]
		public async Task ListBlameables_Functions()
		{
			// Arrange - Pass the functions option
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(DataBlobProviderMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.Text);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);
			AddOption("functions", 1);

			// Act
			var response = await RunInteractions(request);

			// Assert - All blameables are contained in the response and there is no followup message
			var responseObj = DeserializeResponse(response);
			Assert.IsTrue(preppedUsers.All(u => responseObj.data.content.Contains(u.FriendlyName)));
			Assert.IsTrue(string.IsNullOrEmpty(FollowupResponseCapture));
		}

		[TestMethod]
		public async Task AddAndRemoveBlameables_Works()
		{
			// Arrange - Prepare blameables and pass functions option, and capture calls to SaveBlob
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(DataBlobProviderMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.Text);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);
			var savedBlobs = new List<string>();
			DataBlobProviderMock.Setup(m => m.SaveBlobTextAsync(GitBlameCommand.BlameablesFilename, It.IsAny<string>(), It.IsAny<bool>()))
				.Callback<string, string, bool>((file, content, lease) => savedBlobs.Add(content));

			// Act 1 - Toggle add for ourselves
			AddOption("functions", 0);
			AddOption("target-user", UserMock.Object);
			var addResponse = await RunInteractions(request);

			// Act 2 - Toggle remove for an existing user as an admin
			UserMock.Setup(m => m.GuildPermissions).Returns(GuildPermissions.All);
			var randomSimpleUser = preppedUsers[Random.Shared.Next(preppedUsers.Count)];
			var targetUser = GuildMock.Object.GetUserAsync(randomSimpleUser.ID).Result;
			ClearOptions();
			AddOption("functions", 0);
			AddOption("target-user", targetUser);
			request.Body.Position = 0;
			var removeResponse = await RunInteractions(request);

			// Assert - Verify the blob provider was called with the expected arguments, the word "success" exists, and there is no followup message.
			var addResponseObj = DeserializeResponse(addResponse);
			var removeResponseObj = DeserializeResponse(removeResponse);
			DataBlobProviderMock.Verify(m => m.SaveBlobTextAsync(GitBlameCommand.BlameablesFilename, It.IsAny<string>(), It.IsAny<bool>()), Times.Exactly(2));
			Assert.AreEqual(2, savedBlobs.Count);
			Assert.IsTrue(savedBlobs[0].Contains(UserMock.Object.Id.ToString()));
			Assert.IsFalse(savedBlobs[1].Contains(targetUser.Id.ToString()));
			Assert.IsTrue(addResponseObj.data.content.ToLower().Contains("success"));
			Assert.IsTrue(removeResponseObj.data.content.ToLower().Contains("success"));
			Assert.IsNull(FollowupResponseCapture);
		}
	}
}