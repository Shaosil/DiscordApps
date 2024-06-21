using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.SQLite;
using ShaosilBot.Core.Providers;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.SlashCommands
{
	public class WordleCommand : BaseCommand
	{
		private readonly ISQLiteProvider _sqliteProvider;

		public WordleCommand(ILogger<BaseCommand> logger, ISQLiteProvider sqliteProvider) : base(logger)
		{
			_sqliteProvider = sqliteProvider;
		}

		public override string CommandName => "wordle";

		public override string HelpSummary => "Starts or continues a game of wordle.";

		public override string HelpDetails => $"/{CommandName} (string guess], [bool new-game])\n\nUsing this command will either start a new game of wordle or continue an existing one, unless specifically requesting a new game.";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new List<SlashCommandOptionBuilder>
				{

					new SlashCommandOptionBuilder
					{
						Name = "guess",
						Description = "Your current guess",
						Type = ApplicationCommandOptionType.String,
						IsRequired = true,
						MinLength = 5,
						MaxLength = 5
					},
					new SlashCommandOptionBuilder
					{
						Name = "new-game",
						Description = "Forces a new game to start",
						Type = ApplicationCommandOptionType.Boolean,
						IsRequired = false
					}
				}
			}.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			// Store variables
			var allWords = _sqliteProvider.GetDataRecords<WordleWord>();
			var validSolutionWords = allWords.Where(w => w.CanBeSolution).ToList();
			var existingGuesses = _sqliteProvider.GetDataRecords<WordleGuess>(g => g.UserID == cmdWrapper.Command.User.Id);
			bool isNewGame = ((bool?)cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "new-game")?.Value ?? false) || !existingGuesses.Any();
			var activeWord = (isNewGame ? validSolutionWords[Random.Shared.Next(validSolutionWords.Count)].Word : existingGuesses[0].WordID).ToUpper();
			var sb = new StringBuilder();

			// Validate the guess
			string guess = cmdWrapper.Command.Data.Options.First(o => o.Name == "guess").Value.ToString()!.ToUpper();
			if (!Regex.IsMatch(guess, "[a-zA-Z]{5}"))
			{
				return Task.FromResult(cmdWrapper.Respond("Invalid guess provided. Please only use the characters A-Z. No action taken.", ephemeral: true));
			}
			else if (!allWords.Any(w => w.Word.Equals(guess, StringComparison.OrdinalIgnoreCase)))
			{
				return Task.FromResult(cmdWrapper.Respond("Invalid word provided. No action taken.", ephemeral: true));
			}
			else if (existingGuesses.Any(g => g.Guess.Equals(guess, StringComparison.OrdinalIgnoreCase)))
			{
				return Task.FromResult(cmdWrapper.Respond("You already tried that word. No action taken.", ephemeral: true));
			}

			if (isNewGame)
			{
				// Clear any old guesses
				_sqliteProvider.DeleteDataRecords(existingGuesses.ToArray());

				sb.AppendLine($"{cmdWrapper.Command.User.Mention} has started a new wordle game!");
			}
			else
			{
				sb.AppendLine($"{cmdWrapper.Command.User.Mention} is continuing their current wordle game.");
			}
			sb.AppendLine();

			// Insert the new guess
			var newGuess = new WordleGuess
			{
				UserID = cmdWrapper.Command.User.Id,
				WordID = activeWord,
				Guess = guess
			};
			_sqliteProvider.UpsertDataRecords(newGuess);
			existingGuesses = _sqliteProvider.GetDataRecords<WordleGuess>(g => g.UserID == cmdWrapper.Command.User.Id);

			// Build the return message
			sb.AppendLine("```");
			sb.AppendLine($"Guess 1: {BuildColorSquaresFromGuess(activeWord, existingGuesses[0].Guess)}");
			sb.AppendLine($"Guess 2: {BuildColorSquaresFromGuess(activeWord, existingGuesses.ElementAtOrDefault(1)?.Guess)}");
			sb.AppendLine($"Guess 3: {BuildColorSquaresFromGuess(activeWord, existingGuesses.ElementAtOrDefault(2)?.Guess)}");
			sb.AppendLine($"Guess 4: {BuildColorSquaresFromGuess(activeWord, existingGuesses.ElementAtOrDefault(3)?.Guess)}");
			sb.AppendLine($"Guess 5: {BuildColorSquaresFromGuess(activeWord, existingGuesses.ElementAtOrDefault(4)?.Guess)}");
			sb.AppendLine($"Guess 6: {BuildColorSquaresFromGuess(activeWord, existingGuesses.ElementAtOrDefault(5)?.Guess)}");
			sb.AppendLine("```");

			// Check for fail
			if (existingGuesses.Count >= 6)
			{
				sb.AppendLine($"Game over! The solution was '{activeWord.ToUpper()}'.");
				_sqliteProvider.DeleteDataRecords(existingGuesses.ToArray());
			}
			else if (guess.Equals(activeWord, StringComparison.OrdinalIgnoreCase))
			{
				sb.AppendLine($"Correct! The solution is '{activeWord.ToUpper()}'.");
				_sqliteProvider.DeleteDataRecords(existingGuesses.ToArray());
			}

			return Task.FromResult(cmdWrapper.Respond(sb.ToString()));
		}

		private string BuildColorSquaresFromGuess(string answer, string? guess)
		{
			if (string.IsNullOrWhiteSpace(guess)) return "⬛⬛⬛⬛⬛";

			var sb = new StringBuilder();
			var correctLetterCount = new Dictionary<char, int>();
			for (int i = 0; i < 5; i++)
			{
				int curCorrectLettercount = correctLetterCount.GetValueOrDefault(guess[i]);

				if (answer.Count(w => w == guess[i]) > curCorrectLettercount)
				{
					// If the answer contains the current letter and we have not yet tracked it, show green or yellow squares
					if (answer[i] == guess[i])
					{
						sb.Append("🟩");
					}
					else
					{
						sb.Append("🟨");
					}

					correctLetterCount[guess[i]] = curCorrectLettercount + 1;
				}
				else
				{
					sb.Append("🟥");
				}
			}

			sb.Append($" ({guess})");

			return sb.ToString();
		}
	}
}