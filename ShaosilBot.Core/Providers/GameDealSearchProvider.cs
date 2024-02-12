using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.ITAD;
using ShaosilBot.Core.Models.SQLite;
using System.Text;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.Providers
{
	public class GameDealSearchProvider : IGameDealSearchProvider
	{
		private readonly ILogger<GameDealSearchProvider> _logger;
		private readonly IConfiguration _configuration;
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly ISQLiteProvider _sqliteProvider;
		private readonly HttpClient _httpClient;

		public GameDealSearchProvider(ILogger<GameDealSearchProvider> logger,
			IConfiguration configuration,
			IDiscordRestClientProvider restClientProvider,
			ISQLiteProvider sqliteProvider,
			IHttpClientFactory httpClientFactory)
		{
			_logger = logger;
			_configuration = configuration;
			_restClientProvider = restClientProvider;
			_sqliteProvider = sqliteProvider;
			_httpClient = httpClientFactory.CreateClient();
		}

		public async Task DoDefaultSearch()
		{
			_logger.LogInformation("Calling isthereanydeal deals endpoint...");
			var dealsUri = new UriBuilder("https://api.isthereanydeal.com/deals/v2");
			dealsUri.Query = $"key={_configuration["IsThereAnyDealAPIKey"]}&filter={_configuration["IsThereAnyDealFilter"]}";
			var response = await _httpClient.GetAsync(dealsUri.Uri);
			var responseString = await response.Content.ReadAsStringAsync();

			if (response.IsSuccessStatusCode)
			{
				_logger.LogInformation("Success! Deserializing response...");
				var dealResponse = JsonConvert.DeserializeObject<DealResponse>(responseString);
				_logger.LogInformation($"Success! Parsing into DB object(s)... ({dealResponse})");

				var foundGames = new List<GameSale>();
				var deals = dealResponse?.Deals;
				_logger.LogInformation($"Found {deals?.Count ?? 0} games matching filters.");
				if (deals != null)
				{
					// Load useful information into new table records
					foreach (var dealRoot in deals)
					{
						var deal = dealRoot.Deal;
						var foundGame = new GameSale();
						foundGame.ID = dealRoot.ID;
						foundGame.Slug = dealRoot.Slug;
						foundGame.Title = dealRoot.Title;
						foundGame.IsThereAnyDealLink = $"https://isthereanydeal.com/game/{dealRoot.Slug}/info/";
						foundGame.BestPrice = deal.Price.Amount;
						foundGame.BestPercentStore = deal.Shop?.Name;
						foundGame.BestPercentStoreRegularPrice = deal.Regular?.Amount;
						foundGame.BestPercentStoreLink = deal.URL;
						foundGame.BestPercentOff = deal.Cut;
						foundGame.AddedOn = DateTime.Now;

						foundGames.Add(foundGame);
					}
				}

				_logger.LogInformation($"Parsing complete. Loading existing DB records...");
				var allExistingGames = _sqliteProvider.GetAllDataRecords<GameSale>();
				var missingGames = allExistingGames.Where(ag => !foundGames.Any(fg => fg.ID == ag.ID)).ToArray();
				var differentPriceGames = foundGames.Where(fg => allExistingGames.Any(ag => ag.ID == fg.ID)
					&& allExistingGames.First(ag => ag.ID == fg.ID).BestPrice != fg.BestPrice).ToArray();
				var newGames = foundGames.Where(fg => !allExistingGames.Any(ag => ag.ID == fg.ID)).ToArray();
				_logger.LogInformation($"Found {allExistingGames.Count} existing games. {missingGames.Length} to be deleted, {differentPriceGames.Length} to be updated, and {newGames.Length} to be added.");

				// If there is nothing to do, just return here
				if (!missingGames.Any() && !differentPriceGames.Any() && !newGames.Any())
				{
					return;
				}

				// Load and store channels for later use
				bool hasAnnouncementChannel = ulong.TryParse(_configuration["IsThereAnyDealChannel"], out var announcementChannelID);
				var allChannelIDs = allExistingGames.Where(ag => ag.DiscordChannelID.HasValue).Select(ag => ag.DiscordChannelID!.Value)
					.Concat(new[] { announcementChannelID }).Distinct().ToList();
				var loadedChannels = new Dictionary<ulong, ITextChannel>();
				foreach (var channelID in allChannelIDs)
				{
					loadedChannels.Add(channelID, await _restClientProvider.GetChannelAsync(channelID));
				}
				_logger.LogInformation($"Loaded {loadedChannels.Count} Discord channel(s) for messaging.");

				// Delete any table records that are not in the current sale list, and update the message to say expired
				if (missingGames.Any())
				{
					_logger.LogInformation($"Deleting {missingGames.Length} records from DB and messages...");
					_sqliteProvider.DeleteDataRecords(missingGames);

					foreach (var missingGame in missingGames.Where(mg => mg.DiscordChannelID.HasValue && mg.DiscordMessageID.HasValue))
					{
						await loadedChannels[missingGame.DiscordChannelID!.Value].DeleteMessageAsync(missingGame.DiscordMessageID!.Value);
					}
					_logger.LogInformation("Complete!");
				}

				if (differentPriceGames.Any() || newGames.Any())
				{
					// TODO: If there is a different sale for an existing record, update the message
					if (differentPriceGames.Any())
					{

					}

					// Add any new sales to the table and send a message
					if (newGames.Any() && hasAnnouncementChannel && loadedChannels[announcementChannelID] != null)
					{
						_logger.LogInformation("Building and sending messages with new game deals.");
						var embed = new EmbedBuilder
						{
							Color = new Color(0x7c0089),
						};

						foreach (var newGame in newGames)
						{
							// Get game info for reviews and tags
							string? tags = null, reviews = null;
							_logger.LogInformation($"Getting information about {newGame.Title}...");
							var gameInfoUri = new UriBuilder("https://api.isthereanydeal.com/games/info/v2");
							gameInfoUri.Query = $"key={_configuration["IsThereAnyDealAPIKey"]}&id={newGame.ID}";
							response = await _httpClient.GetAsync(gameInfoUri.Uri);
							if (response.IsSuccessStatusCode)
							{
								responseString = await response.Content.ReadAsStringAsync();
								var gameInfo = JsonConvert.DeserializeObject<GameInfoResponse>(responseString);

								if (gameInfo?.Tags?.Any() ?? false)
								{
									tags = $"*Tags: {string.Join(", ", gameInfo.Tags)}*";
								}

								if (gameInfo?.Reviews?.Any(r => r.Score.HasValue) ?? false)
								{
									var mostReviews = gameInfo.Reviews.Where(r => r.Score.HasValue).OrderByDescending(r => r.Count).First();
									reviews = $"*{GetSteamStyleReviewDescription(mostReviews.Score!.Value, mostReviews.Count)} Reviews*";
								}
							}
							else
							{
								_logger.LogWarning("Unsuccessful call to games/info/v2!");
							}

							var allComponents = new List<IMessageComponent>();

							// Basic embed fields
							string store = $"{(string.IsNullOrWhiteSpace(newGame.BestPercentStore) ? string.Empty : $" on {newGame.BestPercentStore}")}";
							embed.Title = $"{newGame.Title} - ${newGame.BestPrice} ({newGame.BestPercentOff}% off)";
							string regPrice = $"{(newGame.BestPercentStoreRegularPrice.HasValue ? $"Regular price **${newGame.BestPercentStoreRegularPrice}**" : string.Empty)}";
							string details = $"{regPrice}{(!string.IsNullOrWhiteSpace(regPrice) ? "\n\n" : string.Empty)}";
							var detailsBuilder = new StringBuilder();
							foreach (var s in new[] { regPrice, tags, reviews })
							{
								if (!string.IsNullOrWhiteSpace(s))
								{
									if (detailsBuilder.Length > 0) detailsBuilder.Append("\n\n");
									detailsBuilder.Append(s);
								}
							}
							embed.Description = detailsBuilder.ToString();
							if (!string.IsNullOrWhiteSpace(newGame.BestPercentStoreLink))
							{
								embed.Url = newGame.BestPercentStoreLink;

								// Link button component
								allComponents.Add(ButtonBuilder.CreateLinkButton("Store Page", newGame.BestPercentStoreLink, new Emoji("💲")).Build());
							}

							// Link button component
							allComponents.Add(ButtonBuilder.CreateLinkButton("Deal Page", newGame.IsThereAnyDealLink, new Emoji("ℹ️")).Build());

							// Try to retrieve the image URL of the game. Since the site doesn't fully load, we can scrape the script it returns
							response = await _httpClient.GetAsync(newGame.IsThereAnyDealLink);
							if (response.IsSuccessStatusCode)
							{
								responseString = await response.Content.ReadAsStringAsync();
								var matches = Regex.Match(responseString, "banner[3-6]\\d{2}\":\"([^\"]+)\"");
								embed.ImageUrl = matches.Groups.Count > 1 ? matches.Groups[1].Value.Replace("\\/", "/") : null;
							}

							// Send the message
							string msg = $"**{newGame.Title}** is **{(newGame.BestPrice <= 0 ? "FREE" : $"${newGame.BestPrice}")}** right now{store}!";
							var actionRow = ComponentBuilder.FromComponents(new[] { new ActionRowBuilder().WithComponents(allComponents).Build() });
							var sentMessage = await loadedChannels[announcementChannelID].SendMessageAsync(msg, embed: embed.Build(), components: actionRow.Build());

							newGame.DiscordChannelID = announcementChannelID;
							newGame.DiscordMessageID = sentMessage.Id;
						}
						_logger.LogInformation("Complete!");
					}

					var gamesToUpsert = differentPriceGames.Concat(newGames).ToArray();
					_logger.LogInformation($"Upserting {gamesToUpsert.Length} DB records...");
					_sqliteProvider.UpsertDataRecords(gamesToUpsert);
					_logger.LogInformation("Complete!");
				}
			}
			else
			{
				_logger.LogWarning($"Error posting to isthereanydeal! Response: {responseString}");
			}
		}

		private string GetSteamStyleReviewDescription(int score, int numReviews)
		{
			return score switch
			{
				>= 80 => numReviews < 50 ? "Positive" : numReviews < 500 || score < 95 ? "Very Positive" : "Overwhelmingly Positive",
				>= 70 => "Mostly Positive",
				>= 40 => "Mixed",
				>= 20 => "Mostly Negative",
				_ => numReviews < 50 ? "Negative" : numReviews < 500 ? "Very Negative" : "Overwhelmingly Negative"
			};
		}
	}
}