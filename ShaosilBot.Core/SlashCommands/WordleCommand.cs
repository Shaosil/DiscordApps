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

		public override string HelpDetails => $@"/{CommandName} (play (string guess) || (remind) || (reset-game) || (stats ([User user, bool reset]))

SUBCOMMANDS:
* play (guess)
    Starts or continues a game of Wordle. Play automatically continues if you have an active game.

* remind
	Displays your current active game and its guesses, if any.

* reset-game
	Clears any active game guesses you may be working on and starts a new game next time you use /{CommandName} play.

* stats ([user, reset])
	Displays a user's (defaults to yours) or resets (only your own) overall Wordle statistics with count details.";

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
						Name = "reset-game",
						Description = "Clears any active game guesses and word you may be working on.",
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
								Name = "user",
								Description = "An optional other user to display.",
								Type = ApplicationCommandOptionType.User,
								IsRequired = false
							},
							new SlashCommandOptionBuilder
							{
								Name = "reset",
								Description = "Wipes all your Wordle gameplay statistics.",
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
				IUser statsUser = (IUser?)subcmd.Options.FirstOrDefault(o => o.Name == "user")?.Value ?? cmdWrapper.Command.User;

				var allStats = _sqliteProvider.GetDataRecords<WordleStat>(s => s.UserID == statsUser.Id);
				var wonGames = allStats.Where(s => s.Solved).ToList();
				if (allStats.Count == 0)
				{
					return Task.FromResult(cmdWrapper.Respond($"{statsUser.Username} has no Wordle stats yet.", ephemeral: true));
				}

				if ((bool?)subcmd.Options.FirstOrDefault(o => o.Name == "reset")?.Value ?? false)
				{
					if (statsUser == cmdWrapper.Command.User)
					{
						int numDeleted = allStats.Count;
						bool isPlural = numDeleted > 1;
						_sqliteProvider.DeleteDataRecords(allStats.ToArray());
						return Task.FromResult(cmdWrapper.Respond($"Your {numDeleted} Wordle statistic{(isPlural ? "s" : "")} {(isPlural ? "have" : "has")} been reset!", ephemeral: true));
					}
					else
					{
						return Task.FromResult(cmdWrapper.Respond($"Um excuse you, only {statsUser.Username} can reset their own stats.", ephemeral: true));
					}
				}

				sb.AppendLine($"{statsUser.Mention}'s Wordle Stats:");
				sb.AppendLine();
				sb.AppendLine("```");
				sb.AppendLine($"GAMES PLAYED: {allStats.Count}");
				sb.AppendLine($"GAMES WON: {wonGames.Count} ({(int)Math.Round(((float)wonGames.Count / allStats.Count) * 100)}%)");
				if (wonGames.Any())
				{
					sb.AppendLine($"AVG ATTEMPTS: {Math.Round((float)wonGames.Sum(s => s.NumGuesses) / wonGames.Count, 2)}");
				}

				if (wonGames.Any())
				{
					int numMax = wonGames.Count(g => g.NumGuesses == wonGames.Max(w => w.NumGuesses));

					sb.AppendLine();
					sb.AppendLine("Guess Distribution:");
					sb.AppendLine();
					for (int i = 1; i <= 6; i++)
					{
						float wonWithThisNumCount = wonGames.Count(s => s.NumGuesses == i);
						float pctOfMax = wonWithThisNumCount / numMax; ;
						int numBarChars = (int)Math.Round(20 * pctOfMax);

						sb.AppendLine($"{i}: {wonWithThisNumCount} {new string('=', numBarChars)} ({Math.Round((wonWithThisNumCount / wonGames.Count) * 100, 2)}%)");
					}
				}
				sb.AppendLine("```");

				return Task.FromResult(cmdWrapper.Respond(sb.ToString()));
			}
			else if (subcmd.Name == "play")
			{
				// Store variables
				var allWords = _sqliteProvider.GetDataRecords<WordleWord>();
				var validSolutionWords = allWords.Where(w => w.CanBeSolution).ToList();
				var existingGuesses = _sqliteProvider.GetDataRecords<WordleGuess>(g => g.UserID == cmdWrapper.Command.User.Id);
				bool isNewGame = !existingGuesses.Any();
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
			else if (subcmd.Name == "reset-game")
			{
				var existingGuesses = _sqliteProvider.GetDataRecords<WordleGuess>(g => g.UserID == cmdWrapper.Command.User.Id);
				if (!existingGuesses.Any())
				{
					return Task.FromResult(cmdWrapper.Respond("You have no active Wordle game. Go ahead and start a new one!", ephemeral: true));
				}
				else
				{
					// Count this as a loss and delete guesses
					_sqliteProvider.UpsertDataRecords(new WordleStat
					{
						UserID = cmdWrapper.Command.User.Id,
						WordID = existingGuesses.First().WordID,
						Solved = false,
						NumGuesses = existingGuesses.Count
					});
					_sqliteProvider.DeleteDataRecords(existingGuesses.ToArray());
					return Task.FromResult(cmdWrapper.Respond("Existing game cleared and counted as a loss. Playing again will generate a new word.", ephemeral: true));
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