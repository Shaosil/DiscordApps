using Moq;
using ShaosilBot.SlashCommands;
using ShaosilBot.Tests.Models;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
    public class CatFactsCommandTests : SlashCommandTestBase<CatFactsCommand>
    {
		protected override CatFactsCommand GetInstance() => new CatFactsCommand(CommandLoggerMock.Object, DataBlobProviderMock.Object);

		[TestMethod]
		public async Task PullsRandomCatFact()
		{
			// Arrange - Create 100 guid strings
			var fakeFacts = new List<string>();
			for (int i = 0; i < 100; i++) fakeFacts.Add(Guid.NewGuid().ToString());
			var serializedFacts = string.Join(Environment.NewLine, fakeFacts);
			DataBlobProviderMock.Setup(m => m.GetBlobTextAsync("CatFacts.txt", It.IsAny<bool>())).Returns(Task.FromResult(serializedFacts));
			var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
			var request = CreateInteractionRequest(interaction);

			// Act
			var response = await InteractionsSUT.Run(request);

			// Assert
			var responseObj = DeserializeResponse(response);
			Assert.IsNotNull(responseObj?.data);
			Assert.IsTrue(fakeFacts.Contains(responseObj.data.content));
		}
    }
}