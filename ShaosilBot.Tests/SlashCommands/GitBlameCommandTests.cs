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

		protected override GitBlameCommand GetInstance() => new GitBlameCommand(CommandLoggerMock.Object, HttpClientFactoryMock.Object, DataBlobProviderMock.Object);

		[ClassInitialize]
		public static new void ClassInitialize(TestContext context)
		{
			// Init blameables and return them serialized when asked
			for (int i = 0; i < 10; i++)
				_blameables.Add(new SimpleDiscordUser { ID = Random.Shared.NextULong(), FriendlyName = $"Friendly name {i + 1}" });
			DataBlobProviderMock.Setup(m => m.GetBlobTextAsync(GitBlameCommand.BlameablesFileName, It.IsAny<bool>())).ReturnsAsync(JsonSerializer.Serialize(_blameables));
		}

		[TestMethod]
		public async Task SimpleBlame_Succeeds()
		{
			// Arrange - Build command with no options and
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);
			var users = Helpers.GenerateSimpleDiscordUsers(DataBlobProviderMock, GuildMock, GitBlameCommand.BlameablesFileName);

			// WIP

			// Act
			//await RunInteractions(request);

			// Assert
			//var responseObj = DeserializeResponse(response);
			//Assert.IsNotNull(responseObj);
		}
	}
}