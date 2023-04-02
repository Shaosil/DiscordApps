using Discord;
using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Tests.Models;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
	public class GitBlameCommandTests : SlashCommandTestBase<GitBlameCommand>
	{
		private List<string> _preppedResponses = new List<string>();

		protected override GitBlameCommand GetInstance() => new GitBlameCommand(Logger, GuildHelperMock.Object, HttpUtilitiesMock.Object, FileAccessProviderMock.Object);

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
			FileAccessProviderMock.Setup(m => m.LoadFileText(GitBlameCommand.ResponsesFilename, It.IsAny<bool>())).Returns(string.Join(Environment.NewLine, _preppedResponses));
		}

		[TestMethod]
		public async Task SimpleBlame_Succeeds()
		{
			// Arrange - Build command with no options
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(GuildHelperMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.Text);

			// Act
			await RunInteractions(interaction);

			// Assert - A prepared user and response should both be contained in the captured followup response
			Assert.IsTrue(preppedUsers.Any(u => FollowupResponseCapture.Contains($"{u}")));
			Assert.IsTrue(_preppedResponses.Any(r => FollowupResponseCapture.Contains(r.Replace("{USER}", string.Empty))));
		}

		[TestMethod]
		public async Task NoBlameablesCanViewChannel_FailsSmoothly()
		{
			// Arrange - Build command with no options and ensure users have no permissions
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(GuildHelperMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.None);

			// Act
			await RunInteractions(interaction);

			// Assert - Response should still contain a message but NOT contain a user or response
			Assert.IsFalse(string.IsNullOrEmpty(FollowupResponseCapture));
			Assert.IsFalse(preppedUsers.Any(u => FollowupResponseCapture.Contains($"{u}")));
			Assert.IsFalse(_preppedResponses.Any(r => FollowupResponseCapture.Contains(r.Replace("{USER}", string.Empty))));
		}

		[TestMethod]
		public async Task ErrorFetchingImages_FailsSmoothly()
		{
			// Arrange - Build command with no options and ensure HTTP call throws exception
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			HttpUtilitiesMock.Setup(m => m.GetRandomGitBlameImage()).Throws(new Exception());
			Helpers.GenerateSimpleDiscordUsers(GuildHelperMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.None);

			// Act
			await RunInteractions(interaction);

			// Assert - Response should contain a message anyway
			Assert.IsFalse(string.IsNullOrWhiteSpace(FollowupResponseCapture));
		}

		[TestMethod]
		public async Task TargetedBlame_WorksAndNotifies()
		{
			// Arrange - Build command with mocked target option
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(GuildHelperMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.Text);
			var randomSimpleUser = preppedUsers[Random.Shared.Next(preppedUsers.Count)];
			var targetUser = GuildMock.Object.GetUserAsync(randomSimpleUser).Result;
			AddOption("target-user", targetUser);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);

			// Act
			await RunInteractions(interaction);

			// Assert - Ensure response contains target user, a random response "image", and the word "Targeted"
			Assert.IsTrue(FollowupResponseCapture.Contains(targetUser.Mention));
			Assert.IsTrue(_preppedResponses.Any(r => FollowupResponseCapture.Contains(r.Replace("{USER}", string.Empty))));
			Assert.IsTrue(FollowupResponseCapture.Contains("Targeted"));
		}

		[TestMethod]
		public async Task TargetedNoAccessUser_DoesNotBlame()
		{
			// Arrange - Build command with mock target and ensure users have no permissions
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(GuildHelperMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.None);
			var randomSimpleUser = preppedUsers[Random.Shared.Next(preppedUsers.Count)];
			var targetUser = GuildMock.Object.GetUserAsync(randomSimpleUser).Result;
			AddOption("target-user", targetUser);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);

			// Act
			await RunInteractions(interaction);

			// Assert - Response should still contain a message with the target user but NOT contain a response URL
			Assert.IsFalse(string.IsNullOrEmpty(FollowupResponseCapture));
			Assert.IsTrue(FollowupResponseCapture.Contains(targetUser.Mention));
			Assert.IsFalse(_preppedResponses.Any(r => FollowupResponseCapture.Contains(r.Replace("{USER}", string.Empty))));
		}

		[TestMethod]
		public async Task TargetedSelf_SpecialBlame()
		{
			// Arrange - Build command with mock users and ensure target user is the caller
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(GuildHelperMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.Text);
			AddOption("target-user", UserMock.Object);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);

			// Act
			await RunInteractions(interaction);

			// Assert - Response should still contain a message with the target user but NOT contain a response URL or any other user
			Assert.IsFalse(string.IsNullOrEmpty(FollowupResponseCapture));
			Assert.IsTrue(FollowupResponseCapture.Contains(UserMock.Object.Mention));
			Assert.IsFalse(preppedUsers.Any(u => FollowupResponseCapture.Contains($"{u}")));
			Assert.IsFalse(_preppedResponses.Any(r => FollowupResponseCapture.Contains(r.Replace("{USER}", string.Empty))));
		}

		[TestMethod]
		public async Task ListBlameables_Functions()
		{
			// Arrange - Pass the functions option
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(GuildHelperMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.Text);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			AddOption("functions", 1);

			// Act
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert - All blameables are contained in the response and there is no followup message
			var responseObj = DeserializeResponse(response!.Content);
			Assert.IsTrue(preppedUsers.All(u => responseObj.data.content.Contains($"{u}")));
			Assert.IsTrue(string.IsNullOrEmpty(FollowupResponseCapture));
		}

		[TestMethod]
		[DataRow(true)]
		[DataRow(false)]
		public async Task AddAndRemoveBlameable_Succeeds(bool isAdd)
		{
			// Arrange - Prepare captured savefile. If this is a remove, generate discord users and target one of them
			var preppedUsers = Helpers.GenerateSimpleDiscordUsers(GuildHelperMock, GuildMock, GitBlameCommand.BlameablesFilename, ChannelPermissions.Text);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var savedFiles = new List<List<ulong>>();
			FileAccessProviderMock.Setup(m => m.SaveFileJSON(GitBlameCommand.BlameablesFilename, It.IsAny<List<ulong>>(), It.IsAny<bool>()))
				.Callback<string, List<ulong>, bool>((file, content, lease) => savedFiles.Add(content));
			IGuildUser targetUser;
			if (isAdd)
			{
				targetUser = UserMock.Object;
			}
			else
			{
				ulong randomSimpleUser = preppedUsers[Random.Shared.Next(preppedUsers.Count)];
				targetUser = await GuildMock.Object.GetUserAsync(randomSimpleUser);
			}

			// Act - Toggle target user as a blameable
			AddOption("functions", 0);
			AddOption("target-user", targetUser);
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert - Ensure the word success exists and we either added or removed the target as a blameable
			var deserializedResponse = DeserializeResponse(response!.Content);
			FileAccessProviderMock.Verify(m => m.SaveFileJSON(GitBlameCommand.BlameablesFilename, It.IsAny<List<ulong>>(), It.IsAny<bool>()), Times.Once);
			Assert.AreEqual(1, savedFiles.Count);
			bool containsTarget = savedFiles[0].Contains(targetUser.Id);
			Assert.AreEqual(isAdd, containsTarget);
			Assert.IsTrue(deserializedResponse.data.content.ToLower().Contains("success"));
			Assert.IsNull(FollowupResponseCapture);
		}

		[TestMethod]
		public async Task AddBlameable_Unauthorized_Fails()
		{
			// Arrange - Make sure the guild helper returns unauthorized
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			GuildHelperMock.Setup(m => m.UserCanEditTargetUser(It.IsAny<IGuild>(), It.IsAny<IGuildUser>(), It.IsAny<IGuildUser>())).Returns(false);

			// Act - Attempt to toggle ourselves
			AddOption("functions", 0);
			AddOption("target-user", UserMock.Object);
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert - Make sure we did not succeed
			var deserializedResponse = DeserializeResponse(response!.Content);
			FileAccessProviderMock.Verify(m => m.SaveFileJSON(GitBlameCommand.BlameablesFilename, It.IsAny<List<ulong>>(), It.IsAny<bool>()), Times.Never);
			Assert.IsTrue(deserializedResponse.data.content.ToLower().Contains("permissions"));
			Assert.IsNull(FollowupResponseCapture);
		}
	}
}