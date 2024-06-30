using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.SQLite;
using ShaosilBot.Core.Providers;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.SlashCommands
{
	public class WordleCommand : BaseCommand
	{
		private readonly IConfiguration _configuration;
		private readonly ISQLiteProvider _sqliteProvider;

		public WordleCommand(ILogger<BaseCommand> logger, IConfiguration configuration, ISQLiteProvider sqliteProvider) : base(logger)
		{
			_configuration = configuration;
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

				var sb = new StringBuilder();
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
					int numMostGuesses = wonGames.GroupBy(w => w.NumGuesses).OrderByDescending(g => g.Count()).First().Count();

					sb.AppendLine();
					sb.AppendLine("Guess Distribution:");
					sb.AppendLine();
					for (int i = 1; i <= 6; i++)
					{
						float wonWithThisNumCount = wonGames.Count(s => s.NumGuesses == i);
						float pctOfMax = wonWithThisNumCount / numMostGuesses;
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
				var activeGame = _sqliteProvider.GetDataRecords<WordleGame>(g => g.UserID == cmdWrapper.Command.User.Id).FirstOrDefault();
				var activeWord = (activeGame == null ? validSolutionWords[Random.Shared.Next(validSolutionWords.Count)].Word : activeGame!.WordID).ToUpper();

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
				else if (activeGame != null && activeGame.Guesses.Any(g => g.Guess.Equals(guess, StringComparison.OrdinalIgnoreCase)))
				{
					return Task.FromResult(cmdWrapper.Respond("You already tried that word. No action taken.", ephemeral: true));
				}

				// Defer so we can properly send an attachment and take time to do Puppetteer stuff
				return cmdWrapper.DeferWithCode(async () =>
				{
					// Start the embed
					var embedBuilder = new EmbedBuilder()
					{
						Color = new Color(0x7c0089),
						ImageUrl = "attachment://wordle.jpg"
					};

					bool justReminding = false;
					if (activeGame == null)
					{
						// Clear the active game if it exists. This will cascade to the guesses
						if (activeGame != null)
						{
							_sqliteProvider.DeleteDataRecords(activeGame);
						}

						// Insert new game
						_sqliteProvider.UpsertDataRecords(new WordleGame
						{
							UserID = cmdWrapper.Command.User.Id,
							WordID = activeWord,
							StartedTimestamp = DateTimeOffset.Now
						});
						activeGame = _sqliteProvider.GetDataRecords<WordleGame>(g => g.UserID == cmdWrapper.Command.User.Id).FirstOrDefault();

						embedBuilder.Title = "Starting a new Wordle game!";
					}
					else
					{
						// Find the most recent activity (reminder or guess) and determine whether to simply remind the user
						List<DateTimeOffset> recentActivities = activeGame.Guesses.Select(g => g.GuessTimestamp).OrderDescending().Take(1).ToList();
						if (activeGame.RemindedTimestamp.HasValue) recentActivities.Add(activeGame.RemindedTimestamp.Value);
						justReminding = DateTimeOffset.Now - recentActivities.OrderDescending().First() > TimeSpan.FromHours(1);

						embedBuilder.Title = justReminding ? "It's been a while since your last guess, so your guess was not taken this time. Here's your active game."
						 : "Continuing your current Wordle game.";
					}

					if (justReminding)
					{
						// Update the reminded timestamp
						activeGame!.RemindedTimestamp = DateTimeOffset.Now;
						_sqliteProvider.UpsertDataRecords(activeGame);
					}
					else
					{
						// Insert the new guess
						var newGuess = new WordleGuess
						{
							UserID = cmdWrapper.Command.User.Id,
							WordleGameID = activeGame!.ID,
							Guess = guess,
							GuessTimestamp = DateTimeOffset.Now
						};
						_sqliteProvider.UpsertDataRecords(newGuess);

						// Check for game end
						bool won = guess.Equals(activeWord, StringComparison.OrdinalIgnoreCase);
						if (won || (activeGame != null && activeGame.Guesses.Count >= 6))
						{
							embedBuilder.Title = $"{(won ? "Correct!" : "Game over!")} The solution is '{activeWord.ToUpper()}'.";

							// Add a stat and delete active game (cascades to guesses)
							_sqliteProvider.UpsertDataRecords(new WordleStat
							{
								UserID = cmdWrapper.Command.User.Id,
								WordID = activeWord,
								Solved = won,
								NumGuesses = activeGame!.Guesses.Count,
								EndedTimestamp = DateTimeOffset.Now
							});
							_sqliteProvider.DeleteDataRecords(activeGame);
						}
					}

					// Generate the HTML image stream (FileAttachment will dispose it automatically
					var imageStream = await GenerateGuessTableImage(activeWord, activeGame!.Guesses);
					using (var attachment = new FileAttachment(imageStream, "wordle.jpg"))
					{
						// Load the original message to make sure it exists
						await cmdWrapper.GetOriginalMessage();

						// Add the attachment and embed to the followup message
						await cmdWrapper.Command.FollowupWithFileAsync(attachment, embed: embedBuilder.Build());
					}
				}, true);
			}
			else if (subcmd.Name == "remind")
			{
				var activeGame = _sqliteProvider.GetDataRecords<WordleGame>(g => g.UserID == cmdWrapper.Command.User.Id).FirstOrDefault();
				if (activeGame == null)
				{
					return Task.FromResult(cmdWrapper.Respond("You have no active Wordle game. Go ahead and start a new one!", ephemeral: true));
				}
				else
				{
					// Start the embed
					var embedBuilder = new EmbedBuilder()
					{
						Title = $"Your current Wordle game is {activeGame.Guesses.Count} guess{(activeGame.Guesses.Count > 1 ? "es" : "")} in:",
						Color = new Color(0x7c0089),
						ImageUrl = "attachment://wordle.jpg"
					};

					// Update the reminded timestamp
					activeGame.RemindedTimestamp = DateTimeOffset.Now;
					_sqliteProvider.UpsertDataRecords(activeGame);

					return cmdWrapper.DeferWithCode(async () =>
					{
						var imageStream = await GenerateGuessTableImage(activeGame.WordID, activeGame.Guesses);
						using (var attachment = new FileAttachment(imageStream, "wordle.jpg"))
						{
							await cmdWrapper.Command.FollowupWithFileAsync(attachment, embed: embedBuilder.Build());
						}
					}, true);
				}
			}
			else if (subcmd.Name == "reset-game")
			{
				var activeGame = _sqliteProvider.GetDataRecords<WordleGame>(g => g.UserID == cmdWrapper.Command.User.Id).FirstOrDefault();
				if (activeGame == null)
				{
					return Task.FromResult(cmdWrapper.Respond("You have no active Wordle game. Go ahead and start a new one!", ephemeral: true));
				}
				else
				{
					// Count this as a loss and delete game (cascades to guesses)
					_sqliteProvider.UpsertDataRecords(new WordleStat
					{
						UserID = cmdWrapper.Command.User.Id,
						WordID = activeGame.WordID,
						Solved = false,
						NumGuesses = activeGame.Guesses.Count,
						EndedTimestamp = DateTimeOffset.Now
					});
					_sqliteProvider.DeleteDataRecords(activeGame);
					return Task.FromResult(cmdWrapper.Respond("Existing game cleared and counted as a loss. Playing again will generate a new word.", ephemeral: true));
				}
			}
			else
			{
				return Task.FromResult(cmdWrapper.Respond("ERROR: Unknown Wordle subcommand!", ephemeral: true));
			}
		}

		private async Task<Stream> GenerateGuessTableImage(string answer, List<WordleGuess> guesses)
		{
			HashSet<char> outChars = new HashSet<char>(), presentChars = new HashSet<char>(), correctChars = new HashSet<char>();

			// Make sure puppeteer browser is downloaded (this could cause an interaction timeout the first time it is run)
			var installedBrowser = await new BrowserFetcher(new BrowserFetcherOptions { Path = _configuration.GetValue<string>("FilesBasePath") }).DownloadAsync();
			using (var browser = await Puppeteer.LaunchAsync(new LaunchOptions { ExecutablePath = installedBrowser.GetExecutablePath(), Headless = true }))
			{
				using (var page = await browser.NewPageAsync())
				{
					await page.SetViewportAsync(new ViewPortOptions { Width = 345, Height = 575 });
					await page.GoToAsync(@$"file:///{Path.Combine(AppContext.BaseDirectory, "Files/Wordle.html")}");

					// Update guesses and styling
					var scriptSB = new StringBuilder();
					for (int i = 0; i < guesses.Count; i++)
					{
						var guess = guesses[i].Guess;

						scriptSB.AppendLine($"var guess{i + 1} = document.getElementById('guess{i + 1}');");

						// Track the number of yellow letters (exist but not exact)
						var correctLetterCount = guess.Distinct().Select(g => new KeyValuePair<char, int>(g, answer.Select((a, ai) => a == g && a != guess[ai] ? 1 : 0).Sum()))
							.ToDictionary(k => k.Key, v => v.Value);

						for (int c = 0; c < 5; c++)
						{
							// Set current letter in HTML and remove the empty class from this tile
							scriptSB.AppendLine($"guess{i + 1}.children[{c}].innerText = '{guess[c]}';");
							scriptSB.AppendLine($"guess{i + 1}.children[{c}].classList.remove('empty');");

							// If the answer contains the current letter, show green or yellow squares
							if (answer[c] == guess[c])
							{
								scriptSB.AppendLine($"guess{i + 1}.children[{c}].classList.add('correct');"); // Green
								correctChars.Add(guess[c]);
							}
							else if (correctLetterCount[guess[c]] > 0)
							{
								scriptSB.AppendLine($"guess{i + 1}.children[{c}].classList.add('present');"); // Yellow
								presentChars.Add(guess[c]);

								// Decrement so we can show the correct amount of yellow squares if there is more than one
								correctLetterCount[guess[c]]--;
							}
							else
							{
								scriptSB.AppendLine($"guess{i + 1}.children[{c}].classList.add('absent');"); // Grey
								outChars.Add(guess[c]);
							}
						}

						// Now set the styling of the keyboard letters
						scriptSB.AppendLine();
						foreach (var c in outChars) scriptSB.AppendLine($"document.getElementById('key{c}').classList.add('absent');");
						foreach (var c in presentChars) scriptSB.AppendLine($"document.getElementById('key{c}').classList.add('present');");
						foreach (var c in correctChars) scriptSB.AppendLine($"document.getElementById('key{c}').classList.add('correct');");
					}

					// Run the script
					await page.EvaluateExpressionAsync(scriptSB.ToString());

					// Take a screenshot and return the stream
					return await page.ScreenshotStreamAsync();
				}
			}
		}
	}
}