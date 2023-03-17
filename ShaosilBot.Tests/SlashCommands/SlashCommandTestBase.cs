using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using Moq;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Tests.Endpoints;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
	public abstract class SlashCommandTestBase<T> : InteractionsTestsBase where T : BaseCommand
	{
		private List<IApplicationCommandInteractionDataOption> _optionsMocks;

		protected T SlashCommandSUT { get; private set; }
		protected Mock<IGuildUser> UserMock { get; private set; }
		protected Mock<IGuild> GuildMock { get; private set; }
		protected Mock<IRestMessageChannel> ChannelMock { get; private set; }
		protected string FollowupResponseCapture { get; private set; }

		protected static Mock<ILogger<T>> CommandLoggerMock { get; private set; }
		protected static Mock<IGuildHelper> GuildHelperMock { get; private set; }
		protected static Mock<IHttpUtilities> HttpUtilitiesMock { get; private set; }
		protected static Mock<IFileAccessHelper> FileAccessProviderMock { get; private set; }

		[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
		public static new void ClassInitialize(TestContext context)
		{
			CommandLoggerMock = new Mock<ILogger<T>>();
			GuildHelperMock = new Mock<IGuildHelper>();
			HttpUtilitiesMock = new Mock<IHttpUtilities>();
			FileAccessProviderMock = new Mock<IFileAccessHelper>();
		}

		[TestInitialize]
		public override void TestInitialize()
		{
			base.TestInitialize();

			SlashCommandSUT = GetInstance();
			SlashCommandProviderMock.Setup(m => m.GetSlashCommandHandler(SlashCommandSUT.CommandName)).Returns(SlashCommandSUT);

			string testUserName = "UnitTester";
			UserMock = new Mock<IGuildUser>();
			UserMock.SetupGet(m => m.Id).Returns(Random.Shared.NextULong());
			UserMock.SetupGet(m => m.DisplayName).Returns(testUserName);
			UserMock.SetupGet(m => m.Username).Returns(testUserName);
			UserMock.SetupGet(m => m.Mention).Returns($"@{testUserName}");
			UserMock.Setup(m => m.GetPermissions(It.IsAny<IGuildChannel>())).Returns(ChannelPermissions.Text);

			GuildMock = new Mock<IGuild>();

			ChannelMock = new Mock<IRestMessageChannel>();

			var dataMock = new Mock<IApplicationCommandInteractionData>();
			_optionsMocks = new List<IApplicationCommandInteractionDataOption>();
			dataMock.SetupGet(m => m.Options).Returns(_optionsMocks);

			SlashCommandWrapperMock.Setup(m => m.Data).Returns(dataMock.Object);
			SlashCommandWrapperMock.Setup(m => m.User).Returns(UserMock.Object);
			SlashCommandWrapperMock.Setup(m => m.Guild).Returns(GuildMock.Object);
			SlashCommandWrapperMock.Setup(m => m.Channel).Returns(ChannelMock.Object);
			SlashCommandWrapperMock.Setup(m => m.DeferWithCode(It.IsAny<Func<Task>>())).Returns<Func<Task>>(f => { f(); return Task.FromResult(""); }); // Don't call defer when running unit tests
			SlashCommandWrapperMock.Setup(m => m.FollowupAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<MessageComponent>(), It.IsAny<Embed>()))
				.Callback<string, bool, MessageComponent, Embed>((s, b, c, e) =>
				{
					FollowupResponseCapture = SlashCommandWrapperMock.Object.Respond(s);
				});
			SlashCommandWrapperMock.Setup(m => m.FollowupWithFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>()))
				.Callback<Stream, string, string>((stream, fileName, text) =>
				{
					FollowupResponseCapture = SlashCommandWrapperMock.Object.Respond(text);
				});
		}

		protected void AddOption(string name, object value)
		{
			var newMockedOption = new Mock<IApplicationCommandInteractionDataOption>();
			newMockedOption.SetupGet(m => m.Name).Returns(name);
			newMockedOption.SetupGet(m => m.Value).Returns(value);
			_optionsMocks.Add(newMockedOption.Object);
		}

		protected void ClearOptions() => _optionsMocks?.Clear();

		protected abstract T GetInstance();
	}
}