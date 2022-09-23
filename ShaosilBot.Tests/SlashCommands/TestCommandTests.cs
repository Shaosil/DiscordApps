using ShaosilBot.SlashCommands;

namespace ShaosilBot.Tests.SlashCommands
{
    [TestClass]
    public class TestCommandTests : SlashCommandTestBase<TestCommand>
    {
        protected override TestCommand GetInstance() => new TestCommand(CommandLoggerMock.Object);

		[TestMethod]
		public void FailTest()
		{
			Assert.Fail("Test fail message!");
		}
    }
}