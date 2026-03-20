using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PuppeteerSharp;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.ITAD;
using ShaosilBot.Core.Models.SQLite;
using static ShaosilBot.Core.Models.ITAD.DealResponse;

namespace ShaosilBot.Core.Providers
{
	public class GameDealSearchProvider : IGameDealSearchProvider
	{
		private readonly ILogger<GameDealSearchProvider> _logger;
		private readonly IConfiguration _configuration;
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly ISQLiteProvider _sqliteProvider;
		private readonly HttpClient _httpClient;

		private IReadOnlyList<Shop> _allShops = new List<Shop>();

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
			// First get the full shops list to associate with IDs
			_logger.LogInformation("Calling isthereanydeal shops endpoint");
			var shopsResponse = await _httpClient.GetAsync("https://api.isthereanydeal.com/service/shops/v1");
			var shopsResponseString = await shopsResponse.Content.ReadAsStringAsync();
			if (shopsResponse.IsSuccessStatusCode)
			{
				_allShops = JsonConvert.DeserializeObject<IReadOnlyList<Shop>>(shopsResponseString)!;
				_logger.LogInformation($"Success! Storing {_allShops.Count} shops.");
			}
			else
			{
				_logger.LogWarning($"Error while querying ITAD shops. Response: {shopsResponseString}");
			}

			_logger.LogInformation("Calling isthereanydeal deals endpoint...");
			var dealsUri = new UriBuilder("https://api.isthereanydeal.com/deals/v2");
			dealsUri.Query = $"key={_configuration["IsThereAnyDealAPIKey"]}&filter={Uri.EscapeDataString(_configuration["IsThereAnyDealFilter"]!)}";
			var response = await _httpClient.GetAsync(dealsUri.Uri);
			var responseString = await response.Content.ReadAsStringAsync();

			if (response.IsSuccessStatusCode)
			{
				var dealResponse = JsonConvert.DeserializeObject<DealResponse>(responseString);
				_logger.LogInformation($"Success! Found {dealResponse?.Deals?.Count} matching deals.");

				var foundGames = new List<GameSale>();
				var deals = dealResponse?.Deals
					.Concat(await GenerateGiveawayMockResponses()).ToList();
					//.Concat(await GenerateSteamMockResponses()).ToList(); // Uncomment if ITAD doesn't return free Steam games again

				_logger.LogInformation($"Found {deals?.Count ?? 0} games matching filters. Parsing into DB object(s)...");
				if (deals != null)
				{
					// Load useful information into new table records
					foreach (var dealRoot in deals)
					{
						var foundGame = new GameSale
						{
							ID = dealRoot.ID,
							Slug = dealRoot.Slug,
							Title = dealRoot.Title,
							IsThereAnyDealLink = $"https://isthereanydeal.com/game/{dealRoot.Slug}/info/",
							BestPrice = dealRoot.Deal?.Price?.Amount ?? 0,
							BestPercentOff = dealRoot.Deal?.Cut ?? 100,
							BestPercentStore = dealRoot.Deal?.Shop?.Name,
							BestPercentStoreRegularPrice = dealRoot.Deal?.Regular?.Amount,
							BestPercentStoreLink = dealRoot.Deal?.URL,
							AddedOn = DateTimeOffset.Now,
							ExpiresOn = dealRoot.Deal?.Expiry
						};
						foundGames.Add(foundGame);
					}
				}

				_logger.LogInformation($"Parsing complete. Loading existing DB records...");
				var allExistingGames = _sqliteProvider.GetDataRecords<GameSale>();
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
						if ((await loadedChannels[missingGame.DiscordChannelID!.Value].GetMessageAsync(missingGame.DiscordMessageID!.Value)) != null)
						{
							await loadedChannels[missingGame.DiscordChannelID!.Value].DeleteMessageAsync(missingGame.DiscordMessageID!.Value);
						}
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

							var allComponents = new List<IMessageComponentBuilder>();

							// Basic embed fields
							string store = $"{(string.IsNullOrWhiteSpace(newGame.BestPercentStore) ? string.Empty : $" on {newGame.BestPercentStore}")}";
							embed.Title = $"{newGame.Title} - {newGame.BestPrice:C} ({newGame.BestPercentOff}% off)";
							string regPrice = $"{(newGame.BestPercentStoreRegularPrice.HasValue ? $"Regular price **{newGame.BestPercentStoreRegularPrice:C}**" : string.Empty)}";
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
								allComponents.Add(ButtonBuilder.CreateLinkButton("Store Page", newGame.BestPercentStoreLink, new Emoji("💲")));
							}

							// Link button component
							allComponents.Add(ButtonBuilder.CreateLinkButton("Deal Page", newGame.IsThereAnyDealLink, new Emoji("ℹ️")));

							// Try to retrieve the image URL of the game. Since the site doesn't fully load, we can scrape the script it returns
							response = await _httpClient.GetAsync(newGame.IsThereAnyDealLink);
							if (response.IsSuccessStatusCode)
							{
								responseString = await response.Content.ReadAsStringAsync();
								var matches = Regex.Match(responseString, "banner[3-6]\\d{2}\":\"([^\"]+)\"");
								embed.ImageUrl = matches.Groups.Count > 1 ? matches.Groups[1].Value.Replace("\\/", "/") : null;
							}

							// Send the message
							string msg = $"**{newGame.Title}** is **{(newGame.BestPrice <= 0 ? "FREE" : $"{newGame.BestPrice:C}")}** right now{store}!\n";
							if (newGame.ExpiresOn.HasValue && newGame.ExpiresOn > DateTimeOffset.Now)
							{
								msg += $"\nDeal expires <t:{newGame.ExpiresOn.Value.ToUnixTimeSeconds()}:R>\n";
							}
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

		private async Task<IEnumerable<DealOverview>> GenerateGiveawayMockResponses()
		{
			var fakeDealResponses = new List<DealOverview>();

			// Load via the unlisted API
			_logger.LogInformation("Retrieving ITAD giveaways.");
			var content = new StringContent(@"{""offset"":0,""filter"":null,""sort"":null}", MediaTypeHeaderValue.Parse("application/json"));
			content.Headers.Add("ITAD-SessionToken", "ShaosilBot");
			content.Headers.Add("Cookie", "sess2=ShaosilBot");
			var response = await _httpClient.PostAsync("https://isthereanydeal.com/giveaways/api/list/?tab=live", content);

			if (response.StatusCode == HttpStatusCode.OK)
			{
				var data = JsonConvert.DeserializeObject<GiveawayResponse>(await response.Content.ReadAsStringAsync())!;

				// Filter to non expired giveaways from our specified stores
				var filterStoreIDs = _configuration.GetValue<string>("IsThereAnyDealGiveawayStores")!.Split(',').Select(s => int.Parse(s)).ToArray();
				var matchingGiveaways = data.Items.Where(i => (!i.Expiry.HasValue || i.Expiry.Value > DateTimeOffset.Now) && filterStoreIDs.Any(s => s == i.ShopID)).ToList();
				_logger.LogInformation($"Success! Found {matchingGiveaways.Count} matching giveaways.");

				// Set the store and deal URL for every game
				foreach (var giveaway in matchingGiveaways)
				{
					var matchingShop = _allShops.FirstOrDefault(s => s.ID == giveaway.ShopID);

					foreach (var game in giveaway.Games)
					{
						// Manually set the shop name and deal URL
						var updatedGame = game with
						{
							Deal = new DealOverview.DealDetails
							(
								Shop: matchingShop != null ? new GenericObj(matchingShop!.ID, matchingShop!.Name) : null,
								null, null, 0, string.Empty, null, null, string.Empty, [], [], DateTime.Now,	// Useless props
								Expiry: giveaway.Expiry,
								URL: giveaway.ShopID == 35 ? "https://www.gog.com/en/#giveaway" : giveaway.URL	// Custom URL for GOG, pass others (Not yet implemented)
							)
						};

						// Most other properties match up between responses so add the rest here
						fakeDealResponses.Add(updatedGame);
					}
				}

				// TODO: Filter to things usually over $5
			}
			else
			{
				_logger.LogWarning($"Error retrieving isthereanydeal giveaways! Response: {await response.Content.ReadAsStringAsync()}");
			}

			return fakeDealResponses;
		}

		private async Task<IEnumerable<DealOverview>> GenerateSteamMockResponses()
		{
			var fakeDealResponses = new List<DealOverview>();
			var steamGames = new List<SteamResult>();

			// Make sure puppeteer browser is downloaded
			var installedBrowser = await new BrowserFetcher(new BrowserFetcherOptions { Path = _configuration.GetValue<string>("FilesBasePath") }).DownloadAsync();
			using (IBrowser browser = await Puppeteer.LaunchAsync(new LaunchOptions { ExecutablePath = installedBrowser.GetExecutablePath(), Headless = true }))
			{
				using (var page = await browser.NewPageAsync())
				{
					_logger.LogInformation("Retrieving free Steam games.");
					await page.GoToAsync(_configuration.GetValue<string>("SteamFreeGamesURL"));

					// Retrieve items via a dynamic JS object
					string objSelector = "{ return { Title: r.querySelector('span').innerText, URL: r.href, AppID: r.dataset.dsAppid, ImgURL: r.querySelector('img')?.src, OrigPrice: r.querySelector('.discount_original_price')?.innerText} }";
					string jsonData = await page.EvaluateExpressionAsync<string>($"Array.from(document.querySelectorAll('a.search_result_row')).slice(0, 5).map(r => {objSelector})")!;
					steamGames = JsonConvert.DeserializeObject<List<SteamResult>>(jsonData)!;

					_logger.LogInformation($"Found {steamGames.Count} free Steam game{(steamGames.Count == 1 ? "" : "s")}.");

					foreach (var game in steamGames.Where(g => !string.IsNullOrWhiteSpace(g.ImgURL)))
					{
						// Update the image URL to use the header instead of the tiny thumbnail
						game.ImgURL = Regex.Replace(game.ImgURL, "(.+)/(.+\\.jpg.*)", "$1/header.jpg");
					}
				}
			}

			// Look up the ITAD info for each game in order to grab the correct IDs so other metadata can be downloaded later
			foreach (var game in steamGames)
			{
				var uriBuilder = new UriBuilder("https://api.isthereanydeal.com/games/lookup/v1");
				uriBuilder.Query = $"key={_configuration.GetValue<string>("IsThereAnyDealAPIKey")}&{(string.IsNullOrWhiteSpace(game.AppID) ? $"title={game.Title}" : $"appid={game.AppID})")}";

				_logger.LogInformation($"Looking up ITAD info for '{game.Title}' by {(string.IsNullOrWhiteSpace(game.AppID) ? "title" : "App ID")}.");
				var response = await _httpClient.GetAsync(uriBuilder.Uri);
				string responseString = await response.Content.ReadAsStringAsync();
				if (response.IsSuccessStatusCode)
				{
					var lookup = JsonConvert.DeserializeObject<GameLookupResponse>(responseString)!;
					if (lookup.Found && lookup.Game != null)
					{
						// Build fake deal responses
						fakeDealResponses.Add(new DealOverview
						(
							lookup.Game.Id,
							lookup.Game.Slug,
							lookup.Game.Title,
							lookup.Game.Type,
							new DealOverview.DealDetails
							(
								new GenericObj(0, "Steam"),
								new Price(0, 0, string.Empty),
								new Price(double.Parse(string.IsNullOrWhiteSpace(game.OrigPrice) ? "0" : Regex.Replace(game.OrigPrice, @"[^\d\.]", "")), 0, string.Empty),
								100, string.Empty, null, null, string.Empty, [], [], DateTime.Now, null, game.URL
							)
						));
					}
					else
					{
						_logger.LogWarning($"Could not find information for '{game.Title}'!");
					}
				}
				else
				{
					_logger.LogWarning($"Error during lookup! Response: {responseString}");
				}
			}

			return fakeDealResponses;
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

		private class SteamResult
		{
			public string Title { get; set; }
			public string AppID { get; set; }
			public string URL { get; set; }
			public string ImgURL { get; set; }
			public string OrigPrice { get; set; }
		}
	}
}