using Microsoft.Extensions.Logging;
using Moq;
using ShaosilBot.Interfaces;
using ShaosilBot.SlashCommands;
using ShaosilBot.Tests.Endpoints;
using ShaosilBot.Tests.Models;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
    public abstract class SlashCommandTestBase<T> : InteractionsTestsBase where T : BaseCommand
    {
        protected T SlashCommandSUT { get; private set; }

        protected static Mock<ILogger<T>> CommandLoggerMock { get; private set;}
		protected static Mock<IHttpClientFactory> HttpClientFactoryMock { get; private set; }
        protected static Mock<IDataBlobProvider> DataBlobProviderMock { get; private set; }

        [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
        public static new void ClassInitialize(TestContext context)
        {
            CommandLoggerMock = new Mock<ILogger<T>>();
			HttpClientFactoryMock = new Mock<IHttpClientFactory>();
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

			// Act - ignore exceptions, we don't care if the handling actually worked
			try { await InteractionsSUT.Run(request); } catch { }

			// Assert - Ensure it was parsed as a slash command
			SlashCommandProviderMock.Verify(m => m.GetSlashCommandHandler(SlashCommandSUT.CommandName), Times.Once);
        }
    }
}