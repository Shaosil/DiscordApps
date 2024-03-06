using Discord;
using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Tests.Models;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
	public class HelpCommandTests : SlashCommandTestBase<HelpCommand>
	{
		private static List<Type> _derivedTypes;
		private static Dictionary<string, BaseCommand> _mappedInstances;
		private static Dictionary<string, SlashCommandProperties> _mappedCommands;

		protected override HelpCommand GetInstance() => new HelpCommand(Logger, SlashCommandProviderMock.Object);

		[ClassInitialize]
		public static new void ClassInitialize(TestContext context)
		{
			// Set up a fake service provider to return a
			_derivedTypes = typeof(BaseCommand).Assembly.GetTypes().Where(t => t.BaseType == typeof(BaseCommand) && t != typeof(HelpCommand)).ToList();
			_mappedInstances = new Dictionary<string, BaseCommand>();
			_mappedCommands = new Dictionary<string, SlashCommandProperties>();
			foreach (var type in _derivedTypes)
			{
				// Get parameters for current type's constructor
				var constructor = type.GetConstructors().First();
				var constructorParams = constructor.GetParameters().Select(p => ((Mock)Activator.CreateInstance(typeof(Mock<>).MakeGenericType(p.ParameterType))!).Object).ToArray();
				var instance = (BaseCommand)constructor.Invoke(constructorParams);
				_mappedInstances.Add(instance.CommandName, instance);
				_mappedCommands.Add(instance.CommandName, instance.BuildCommand());
			}
		}

		[TestInitialize]
		public override void TestInitialize()
		{
			base.TestInitialize();

			SlashCommandProviderMock.Setup(m => m.CommandProperties).Returns(_mappedCommands);
			SlashCommandProviderMock.Setup(m => m.GetSlashCommandHandler(It.IsNotIn(SUT.CommandName))).Returns<string>(s => _mappedInstances[s]);
		}

		[TestMethod]
		public async Task HelpCommand_ListsAllCommandsExceptItself()
		{
			// Arrange - Ensure we call the command as admin
			var interaction = DiscordInteraction.CreateSlash(SUT);
			UserMock.Setup(m => m.GuildPermissions).Returns(GuildPermissions.All);

			// Act
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert - Every known command's name and summary should be in the response text
			string responseBody = DeserializeResponse(response!.Content).data.content;
			foreach (var instance in _mappedInstances.Values)
			{
				Assert.IsTrue(responseBody.Contains(instance.CommandName));
				Assert.IsTrue(responseBody.Contains(instance.HelpSummary));
			}
		}

		[TestMethod]
		public async Task HelpCommand_HidesAdminCommands()
		{
			// Arrange - Call as a default nobody
			var interaction = DiscordInteraction.CreateSlash(SUT);

			// Act
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert - Only commands that should be seen by the user are seen
			string responseBody = DeserializeResponse(response!.Content).data.content;
			var allowedInstances = _mappedInstances.Values.Where(v => !_mappedCommands[v.CommandName].DefaultMemberPermissions.IsSpecified
				|| UserMock.Object.GuildPermissions.Has(_mappedCommands[v.CommandName].DefaultMemberPermissions.Value)).ToList();
			foreach (var instance in _mappedInstances.Values)
			{
				Assert.AreEqual(allowedInstances.Contains(instance), responseBody.Contains(instance.CommandName));
				Assert.AreEqual(allowedInstances.Contains(instance), responseBody.Contains(instance.HelpSummary));
			}
		}

		[TestMethod]
		public async Task HelpCommandTarget_ShowsDetails()
		{
			// Arrange - Ensure we call the command as admin
			var interaction = DiscordInteraction.CreateSlash(SUT);
			UserMock.Setup(m => m.GuildPermissions).Returns(GuildPermissions.All);

			// Act for each command
			foreach (var instance in _mappedInstances)
			{
				ClearOptions();
				AddOption("command", instance.Key);

				var response = await RunInteractions(interaction) as ContentResult;

				// Assert - The details should be returned
				string responseBody = DeserializeResponse(response!.Content).data.content;
				Assert.IsTrue(responseBody.Contains(instance.Value.HelpDetails));
			}
		}

		[TestMethod]
		public async Task HelpCommandPartialTarget_AsksForClarification()
		{
			// Arrange - Call as a default nobody with a super basic command request
			var interaction = DiscordInteraction.CreateSlash(SUT);
			AddOption("command", "b");

			// Act
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert - Matching commands should have been returned
			string responseBody = DeserializeResponse(response!.Content).data.content;
			Assert.IsTrue(responseBody.Contains("Multiple"));
			foreach (var matchingInstance in _mappedInstances.Where(i => i.Key.ToLower().Contains("b")))
			{
				Assert.IsTrue(responseBody.Contains(matchingInstance.Key));
			}
		}

		[TestMethod]
		public async Task HelpCommandInvalidTarget_FailsSmoothly()
		{
			// Arrange - Call as a default nobody with a missing command request
			var interaction = DiscordInteraction.CreateSlash(SUT);
			AddOption("command", "THIS IS NOT A VALID COMMAND");

			// Act
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert - The response should contain a warning message
			string responseBody = DeserializeResponse(response!.Content).data.content;
			Assert.IsTrue(responseBody.Contains("does not exist"));
		}
	}
}