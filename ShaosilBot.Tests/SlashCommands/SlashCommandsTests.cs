using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Tests.Endpoints;
using ShaosilBot.Tests.Models;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
	public class SlashCommandTests : InteractionsTestsBase
	{
		[TestMethod]
		public async Task InteractionParses()
		{
			// Arrange
			var testCommand = new TestCommand(Logger);
			var interaction = DiscordInteraction.CreateSlash(testCommand);

			// Act - ignore exceptions, we don't care if the handling actually worked
			try { await RunInteractions(interaction); } catch { }

			// Assert - Ensure it was parsed as a slash command
			SlashCommandProviderMock.Verify(m => m.GetSlashCommandHandler(testCommand.CommandName), Times.Once);
		}
	}
}