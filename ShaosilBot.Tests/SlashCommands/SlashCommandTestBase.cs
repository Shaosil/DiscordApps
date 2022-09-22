using Microsoft.Extensions.Logging;
using Moq;
using ShaosilBot.Interfaces;
using ShaosilBot.SlashCommands;
using ShaosilBot.Tests.Endpoints;
using ShaosilBot.Tests.Models;
using System.Net;
using System.Text.Json;

namespace ShaosilBot.Tests.SlashCommands
{
    [TestClass]
    public abstract class SlashCommandTestBase<T> : InteractionsTestsBase where T : BaseCommand
    {
        protected T SlashCommandSUT { get; private set; }

        protected static Mock<ILogger<T>> CommandLoggerMock { get; private set;}
        protected static Mock<IDataBlobProvider> DataBlobProviderMock { get; private set; }

        [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
        public static new void ClassInitialize(TestContext context)
        {
            CommandLoggerMock = new Mock<ILogger<T>>();
            DataBlobProviderMock = new Mock<IDataBlobProvider>();
        }

        [TestInitialize]
        public override void TestInitialize()
        {
            base.TestInitialize();

            if (SlashCommandSUT == null)
                SlashCommandSUT = GetInstance();

            if (!SlashCommandProviderMock.Setups.Any())
                SlashCommandProviderMock.Setup(m => m.GetSlashCommandHandler(It.IsAny<string>())).Returns(SlashCommandSUT);
        }

        protected abstract T GetInstance();

        [TestMethod]
        public async Task InteractionParses()
        {
            // Arrange
            var interaction = DiscordInteraction.CreateSlash(SlashCommandSUT);
            var request = CreateInteractionRequest(interaction);

            // Act
            var response = await InteractionsSUT.Run(request);

            // Assert
            string responseBody = GetResponseBody(response);
            var responseObj = JsonSerializer.Deserialize<DiscordInteractionResponse>(responseBody);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsNotNull(responseObj!.data);
            Assert.IsFalse(string.IsNullOrWhiteSpace(responseObj.data.content));
        }
    }
}