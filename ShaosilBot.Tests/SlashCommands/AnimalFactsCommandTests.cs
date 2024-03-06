using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Tests.Models;
using System.Reflection;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
	public class AnimalFactsCommandTests : SlashCommandTestBase<AnimalFactsCommand>
	{
		protected override AnimalFactsCommand GetInstance() => new AnimalFactsCommand(Logger, FileAccessProviderMock.Object);

		[TestMethod]
		public async Task PullsRandomUnspecifiedFact()
		{
			// Arrange - Create 100 guid strings for EACH declared file name
			var allProps = SUT.GetType().GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(f => f.Name.EndsWith("FileName")).ToList();
			Dictionary<FieldInfo, List<string>> fieldsToGuids = new Dictionary<FieldInfo, List<string>>();
			foreach (var prop in allProps)
			{
				fieldsToGuids[prop] = new List<string>();
				for (int i = 0; i < 100; i++) fieldsToGuids[prop].Add(Guid.NewGuid().ToString());
				var serializedFacts = string.Join(Environment.NewLine, fieldsToGuids[prop]);
				string fileName = prop.GetValue(null)!.ToString()!;

				FileAccessProviderMock.Setup(m => m.LoadFileText(fileName, It.IsAny<bool>())).Returns(serializedFacts);
			}
			var interaction = DiscordInteraction.CreateSlash(SUT);

			// Act
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert - Ensure the response pulled from one of the fake files
			var responseObj = DeserializeResponse(response!.Content);
			Assert.IsNotNull(responseObj?.data);
			string content = responseObj!.data.content;
			Assert.IsTrue(fieldsToGuids.Any(ftg => ftg.Value.Any(g => content.Contains(g))));
		}

		[TestMethod]
		public async Task PullsRandomCatFact()
		{
			// Arrange - Create 100 guid strings
			var fakeFacts = new List<string>();
			for (int i = 0; i < 100; i++) fakeFacts.Add(Guid.NewGuid().ToString());
			var serializedFacts = string.Join(Environment.NewLine, fakeFacts);
			FileAccessProviderMock.Setup(m => m.LoadFileText(AnimalFactsCommand.CatFactsFileName, It.IsAny<bool>())).Returns(serializedFacts);
			AddOption("type", AnimalFactsCommand.CatFactsFileName);
			var interaction = DiscordInteraction.CreateSlash(SUT);

			// Act
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert
			var responseObj = DeserializeResponse(response!.Content);
			Assert.IsNotNull(responseObj?.data);
			Assert.IsTrue(fakeFacts.Contains(responseObj!.data.content.Replace(":cat: ", "")));
		}

		[TestMethod]
		public async Task PullsRandomDogFact()
		{
			// Arrange - Create 100 guid strings
			var fakeFacts = new List<string>();
			for (int i = 0; i < 100; i++) fakeFacts.Add(Guid.NewGuid().ToString());
			var serializedFacts = string.Join(Environment.NewLine, fakeFacts);
			FileAccessProviderMock.Setup(m => m.LoadFileText(AnimalFactsCommand.DogFactsFileName, It.IsAny<bool>())).Returns(serializedFacts);
			AddOption("type", AnimalFactsCommand.DogFactsFileName);
			var interaction = DiscordInteraction.CreateSlash(SUT);

			// Act
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert
			var responseObj = DeserializeResponse(response!.Content);
			Assert.IsNotNull(responseObj?.data);
			Assert.IsTrue(fakeFacts.Contains(responseObj!.data.content.Replace(":dog: ", "")));
		}
	}
}