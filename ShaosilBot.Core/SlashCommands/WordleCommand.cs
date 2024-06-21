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

		public override string HelpDetails => $@"/{CommandName} (play (string guess, [bool new-game]) || (stats)

SUBCOMMANDS:
* play (guess, [new-game])
    Starts or continues a game of Wordle. Play automatically continues if you have an active game unless you force 'new-game' to true.

* remind
	Displays your current active game and its guesses, if any.

* stats ([detailed])
	Displays your overall Wordle statistics, optionally displaying count details.";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new List<SlashCommandOptionBuilder>
				{
					new SlashCommandOptionBuilder
					{
						Name = "play",
						Description = "Starts or continues a game of Wordle.",
						Type = ApplicationCommandOptionType.SubCommand,
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
					},

					new SlashCommandOptionBuilder
					{
						Name = "remind",
						Description = "Displays your currently active game and its guesses.",
						Type = ApplicationCommandOptionType.SubCommand
					},

					new SlashCommandOptionBuilder
					{
						Name = "stats",
						Description = "Displays your running Wordle statistics.",
						Type = ApplicationCommandOptionType.SubCommand,
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder
							{
								Name = "detailed",
								Description = "Shows extra details in your stats.",
								Type = ApplicationCommandOptionType.Boolean,
								IsRequired = false
							}
						}
					}

				}
			}.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			var sb = new StringBuilder();

			// Just return stats if that is what was requested
			var subcmd = cmdWrapper.Command.Data.Options.First();
			if (subcmd.Name == "stats")
			{
				var allStats = _sqliteProvider.GetDataRecords<WordleStat>(s => s.UserID == cmdWrapper.Command.User.Id);
				if (allStats.Count == 0)
				{
					return Task.FromResult(cmdWrapper.Respond("You have no Wordle stats yet! Go ahead and play a few games and come back when you're older.", ephemeral: true));
				}

				int wonGames = allStats.Count(s => s.Solved);

				sb.AppendLine($"{cmdWrapper.Command.User.Mention}'s Wordle Stats:");
				sb.AppendLine();
				sb.AppendLine("```");
				sb.AppendLine($"GAMES PLAYED: {allStats.Count}");
				sb.AppendLine($"GAMES WON: {wonGames} ({(int)Math.Round(((float)wonGames / allStats.Count) * 100)}%)");
				sb.AppendLine($"AVG ATTEMPTS: {Math.Round((float)allStats.Sum(s => s.NumGuesses) / allStats.Count, 2)}");
				sb.AppendLine("```");

				if (((bool?)subcmd.Options.FirstOrDefault(o => o.Name == "detailed")?.Value) ?? false)
				{
					sb.AppendLine("Guess Distribution:");
					sb.AppendLine();
					sb.AppendLine("```");
					for (int i = 1; i <= 6; i++)
					{
						int num = allStats.Count(s => s.NumGuesses == i);
						int pct = (int)Math.Round((float)num / allStats.Count);
						int numBarChars = 20;

						sb.AppendLine($"{i}: {num} {new string('=', numBarChars * pct)} ({pct * 100}%)");
					}
					sb.AppendLine("```");
				}

				return Task.FromResult(cmdWrapper.Respond(sb.ToString()));
			}
			else if (subcmd.Name == "play")
			{
				// Store variables
				var allWords = _sqliteProvider.GetDataRecords<WordleWord>();
				var validSolutionWords = allWords.Where(w => w.CanBeSolution).ToList();
				var existingGuesses = _sqliteProvider.GetDataRecords<WordleGuess>(g => g.UserID == cmdWrapper.Command.User.Id);
				bool isNewGame = ((bool?)subcmd.Options.FirstOrDefault(o => o.Name == "new-game")?.Value ?? false) || !existingGuesses.Any();
				var activeWord = (isNewGame ? validSolutionWords[Random.Shared.Next(validSolutionWords.Count)].Word : existingGuesses[0].WordID).ToUpper();

				// Validate the guess
				string guess = subcmd.Options.First(o => o.Name == "guess").Value.ToString()!.ToUpper();
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

					sb.AppendLine("Starting a new Wordle game!");
				}
				else
				{
					sb.AppendLine("Continuing your current Wordle game.");
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
				sb.Append(BuildGuessTable(activeWord, existingGuesses));

				// Check for game end
				bool won = guess.Equals(activeWord, StringComparison.OrdinalIgnoreCase);
				if (won || existingGuesses.Count >= 6)
				{
					sb.AppendLine($"{(won ? "Correct!" : "Game over!")} The solution is '{activeWord.ToUpper()}'.");

					// Add a stat and delete guesses
					_sqliteProvider.UpsertDataRecords(new WordleStat
					{
						UserID = cmdWrapper.Command.User.Id,
						WordID = activeWord,
						Solved = won,
						NumGuesses = existingGuesses.Count
					});
					_sqliteProvider.DeleteDataRecords(existingGuesses.ToArray());
				}

				return Task.FromResult(cmdWrapper.Respond(sb.ToString(), ephemeral: true));
			}
			else if (subcmd.Name == "remind")
			{
				var existingGuesses = _sqliteProvider.GetDataRecords<WordleGuess>(g => g.UserID == cmdWrapper.Command.User.Id);
				if (!existingGuesses.Any())
				{
					return Task.FromResult(cmdWrapper.Respond("You have no active Wordle game. Go ahead and start a new one!", ephemeral: true));
				}
				else
				{
					string activeWord = existingGuesses.First().WordID;
					sb.AppendLine($"Your current Wordle game is {existingGuesses.Count} guesses in:");
					sb.AppendLine();
					sb.Append(BuildGuessTable(activeWord, existingGuesses));
					sb.AppendLine();
					sb.AppendLine($"Use `/{CommandName} play` to guess again.");

					return Task.FromResult(cmdWrapper.Respond(sb.ToString(), ephemeral: true));
				}
			}
			else
			{
				return Task.FromResult(cmdWrapper.Respond("ERROR: Unknown Wordle subcommand!", ephemeral: true));
			}
		}

		private string BuildGuessTable(string answer, List<WordleGuess> guesses)
		{
			var sb = new StringBuilder();

			sb.AppendLine("```");
			sb.AppendLine($"Guess 1: {BuildColorSquaresFromGuess(answer, guesses[0].Guess)}");
			sb.AppendLine($"Guess 2: {BuildColorSquaresFromGuess(answer, guesses.ElementAtOrDefault(1)?.Guess)}");
			sb.AppendLine($"Guess 3: {BuildColorSquaresFromGuess(answer, guesses.ElementAtOrDefault(2)?.Guess)}");
			sb.AppendLine($"Guess 4: {BuildColorSquaresFromGuess(answer, guesses.ElementAtOrDefault(3)?.Guess)}");
			sb.AppendLine($"Guess 5: {BuildColorSquaresFromGuess(answer, guesses.ElementAtOrDefault(4)?.Guess)}");
			sb.AppendLine($"Guess 6: {BuildColorSquaresFromGuess(answer, guesses.ElementAtOrDefault(5)?.Guess)}");
			sb.AppendLine();
			var invalidLetters = guesses.SelectMany(g => g.Guess).Distinct().Where(g => !answer.Any(w => w == g)).Order();
			sb.AppendLine($"OUT: {string.Join(',', invalidLetters)}");
			sb.AppendLine("```");

			return sb.ToString();
		}

		private string BuildColorSquaresFromGuess(string answer, string? guess)
		{
			if (string.IsNullOrWhiteSpace(guess)) return "⬛⬛⬛⬛⬛";

			var sb = new StringBuilder();
			var correctLetterCount = new Dictionary<char, int>();
			for (int i = 0; i < 5; i++)
			{
				int curCorrectLettercount = correctLetterCount.GetValueOrDefault(guess[i]);

				// If the answer contains the current letter and we have not yet tracked it, show green or yellow squares
				if (answer[i] == guess[i] || answer.Count(w => w == guess[i]) > curCorrectLettercount)
				{
					if (answer[i] == guess[i])
					{
						sb.Append("🟩"); // Green
					}
					else
					{
						sb.Append("🟨"); // Yellow
					}

					correctLetterCount[guess[i]] = curCorrectLettercount + 1;
				}
				else
				{
					sb.Append("🟥"); // Red
				}
			}

			sb.Append($" ({guess})");

			return sb.ToString();
		}
	}
}