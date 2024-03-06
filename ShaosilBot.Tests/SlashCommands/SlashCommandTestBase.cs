using Discord;
using Discord.Rest;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Tests.Endpoints;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
	public abstract class SlashCommandTestBase<T> : InteractionsTestsBase where T : BaseCommand
	{
		private List<IApplicationCommandInteractionDataOption> _optionsMocks;

		protected Mock<IGuildHelper> GuildHelperMock { get; private set; }
		protected Mock<IHttpUtilities> HttpUtilitiesMock { get; private set; }
		protected Mock<IGuildUser> UserMock { get; private set; }
		protected Mock<IGuild> GuildMock { get; private set; }
		protected Mock<IRestMessageChannel> ChannelMock { get; private set; }
		protected Mock<IFileAccessHelper> FileAccessProviderMock { get; private set; }
		protected string FollowupResponseCapture { get; private set; }

		[TestInitialize]
		public override void TestInitialize()
		{
			base.TestInitialize();

			GuildHelperMock = new Mock<IGuildHelper>();
			HttpUtilitiesMock = new Mock<IHttpUtilities>();
			FileAccessProviderMock = new Mock<IFileAccessHelper>();
			SUT = GetInstance();
			SlashCommandProviderMock.Setup(m => m.GetSlashCommandHandler(SUT.CommandName)).Returns(SUT);

			string testUserName = "UnitTester";
			UserMock = new Mock<IGuildUser>();
			UserMock.SetupGet(m => m.Id).Returns(Random.Shared.NextULong());
			UserMock.SetupGet(m => m.DisplayName).Returns(testUserName);
			UserMock.SetupGet(m => m.Username).Returns(testUserName);
			UserMock.SetupGet(m => m.Mention).Returns($"@{testUserName}");
			UserMock.Setup(m => m.GetPermissions(It.IsAny<IGuildChannel>())).Returns(ChannelPermissions.Text);

			GuildMock = new Mock<IGuild>();
			ChannelMock = new Mock<IRestMessageChannel>();
			RestClientProviderMock.SetupGet(m => m.Guilds).Returns(new List<IGuild> { GuildMock.Object });


			var dataMock = new Mock<IApplicationCommandInteractionData>();
			_optionsMocks = new List<IApplicationCommandInteractionDataOption>();
			dataMock.SetupGet(m => m.Options).Returns(_optionsMocks);
			var commandMock = new Mock<ISlashCommandInteraction>();
			commandMock.Setup(m => m.Data).Returns(dataMock.Object);
			commandMock.Setup(m => m.User).Returns(UserMock.Object);
			commandMock.Setup(m => m.ChannelId).Returns(0);
			commandMock.Setup(m => m.GuildId).Returns(0);
			SlashCommandWrapperMock.Setup(m => m.Command).Returns(commandMock.Object);

			SlashCommandWrapperMock.Setup(m => m.DeferWithCode(It.IsAny<Func<Task>>(), It.IsAny<bool>()))
					.Returns<Func<Task>, bool>((f, b) => { f(); return Task.FromResult(""); }); // Don't call defer when running unit tests
			SlashCommandWrapperMock.Setup(m => m.Command.FollowupAsync(It.IsAny<string>(), It.IsAny<Embed[]>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<AllowedMentions>(), It.IsAny<MessageComponent>(), It.IsAny<Embed>(), null))
				.Callback<string, Embed[], bool, bool, AllowedMentions, MessageComponent, Embed, RequestOptions>((s, _, _, _, _, _, _, _) =>
				{
					FollowupResponseCapture = SlashCommandWrapperMock.Object.Respond(s);
				});
			SlashCommandWrapperMock.Setup(m => m.Command.FollowupWithFileAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Embed[]>(),
				It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<AllowedMentions>(), It.IsAny<MessageComponent>(), It.IsAny<Embed>(), null))
				.Callback<Stream, string, string, Embed[], bool, bool, AllowedMentions, MessageComponent, Embed, RequestOptions>((_, _, text, _, _, _, _, _, _, _) =>
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