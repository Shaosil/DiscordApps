using ShaosilBot.SlashCommands;
using ShaosilBot.Tests.Models;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
    public class TestCommandTests : SlashCommandTestBase<TestCommand>
    {
        protected override TestCommand GetInstance() => new TestCommand(CommandLoggerMock.Object);

		[TestMethod]
		public async Task ReturnsSuccess()
		{
			// Arrange
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);

			// Act
			var response = await RunInteractions(request);

			// Assert
			var responseObj = DeserializeResponse(response);
			Assert.IsNotNull(responseObj?.data);
			Assert.AreEqual("Test command successful", responseObj.data.content);
		}
    }
}