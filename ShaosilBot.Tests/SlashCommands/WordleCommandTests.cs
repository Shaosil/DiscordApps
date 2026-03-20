using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Core.Models.SQLite;
using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Tests.Models;

namespace ShaosilBot.Tests.SlashCommands
{
	[TestClass]
	public class WordleCommandTests : SlashCommandTestBase<WordleCommand>
	{
		protected override WordleCommand GetInstance() => new WordleCommand(Logger, Configuration, SQLiteProviderMock.Object);

		[TestMethod]
		public async Task PlayDailyGuess_StartsNewGame()
		{
			// Arrange - Prepare a daily wordle game for test user
			var wordOpt = MakeOption("word", "ATEST");
			var guessOpt = MakeOption("guess", subOptions: [wordOpt]);
			AddOptionToBase("play", subOptions: [guessOpt]);
			SQLiteProviderMock.Setup(m => m.GetDataRecords<WordleWord>()).Returns(new List<WordleWord> { new WordleWord { Word = "ATEST", CanBeSolution = true } });
			SQLiteProviderMock.Setup(m => m.GetDataRecords<WordleGame>()).Returns(new List<WordleGame>());

			// Act - Run interaction
			var interaction = DiscordInteraction.CreateSlash(SUT);
			var response = await RunInteractions(interaction) as ContentResult;

			// Assert - Verify a new game was started
			SQLiteProviderMock.Verify(m => m.UpsertDataRecords(It.IsAny<WordleGame>()), Times.Once);
		}
	}
}