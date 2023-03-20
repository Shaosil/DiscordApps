using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Tests.Models;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
	public class TestCommandTests : SlashCommandTestBase<TestCommand>
	{
		protected override TestCommand GetInstance() => new TestCommand(Logger);

		[TestMethod]
		public async Task ReturnsSuccess()
		{
			// Arrange
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);

			// Act
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert
			var responseObj = DeserializeResponse(response!.Content);
			Assert.IsNotNull(responseObj?.data);
			Assert.AreEqual("Test command successful", responseObj!.data.content);
		}
	}
}