using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Tests.Models;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
	public class CatFactsCommandTests : SlashCommandTestBase<CatFactsCommand>
	{
		protected override CatFactsCommand GetInstance() => new CatFactsCommand(CommandLoggerMock.Object, FileAccessProviderMock.Object);

		[TestMethod]
		public async Task PullsRandomCatFact()
		{
			// Arrange - Create 100 guid strings
			var fakeFacts = new List<string>();
			for (int i = 0; i < 100; i++) fakeFacts.Add(Guid.NewGuid().ToString());
			var serializedFacts = string.Join(Environment.NewLine, fakeFacts);
			FileAccessProviderMock.Setup(m => m.LoadFileText("CatFacts.txt", It.IsAny<bool>())).Returns(serializedFacts);
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);

			// Act
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert
			var responseObj = DeserializeResponse(response!.Content);
			Assert.IsNotNull(responseObj?.data);
			Assert.IsTrue(fakeFacts.Contains(responseObj!.data.content));
		}
	}
}