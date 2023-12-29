using Discord;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.SQLite;
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
			var items = new List<KeyValuePair<string, string?>>
			{
				new ("offset", "0"),
				new ("limit", "5"),
				new ("filter", "&price/0/0,&regular/5/max,-dlc,-itchio,&last/48"),
				new ("by", "trending:desc"),
				new ("options", null),
				new ("seen", null),
				new ("id", null),
				new ("timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString())
			};
			var content = new FormUrlEncodedContent(items);
			_logger.LogInformation("Posting to isthereanydeal with filters...");
			var response = await _httpClient.PostAsync("https://isthereanydeal.com/ajax/data/lazy.deals.php", content);
			var responseString = await response.Content.ReadAsStringAsync();

			if (response.IsSuccessStatusCode)
			{
				_logger.LogInformation("Success! Deserializing HTML...");
				string html = JsonConvert.DeserializeObject<DealSearchResponse>(responseString)!.data.html;

				var doc = new HtmlDocument();
				doc.LoadHtml(html);
				_logger.LogInformation("Success! Parsing into DB object(s)...");

				var foundGames = new List<GameSale>();
				var gameNodes = doc.DocumentNode.SelectNodes("//div[@class='game']");
				_logger.LogInformation($"Found {gameNodes?.Count ?? 0} games matching filters.");
				if (gameNodes != null)
				{
					// Extract useful information into new table records
					foreach (var game in gameNodes)
					{
						var foundGame = new GameSale();
						foundGame.PlainGameID = game.GetDataAttribute("plain").Value;
						foundGame.Title = game.SelectSingleNode("div[@class='title']/a").InnerText;

						string infoPageLink = game.SelectSingleNode("div[contains(@class, 'overview') and contains(@class, 'exp')]/a[text() = 'Game Page']").GetAttributeValue("href", string.Empty);
						foundGame.IsThereAnyDealLink = $"https://isthereanydeal.com{infoPageLink}";

						string details = game.SelectSingleNode("div[contains(@class, 'overview') and contains(@class, 'def')]").InnerText;
						var reviewMatch = Regex.Match(details, "(.+ \\| )*(.+)&nbsp;");
						if (reviewMatch.Success)
						{
							foundGame.Reviews = reviewMatch.Groups[2].Value;
						}

						var dealNode = game.SelectSingleNode("div[contains(@class, 'deals')]/a[last()]");
						string bestPrice = dealNode.InnerText;
						foundGame.BestPrice = decimal.Parse(Regex.Match(bestPrice, "\\$([\\d\\.]+)").Groups[1].Value);
						foundGame.BestPercentStoreLink = dealNode.GetAttributeValue("href", string.Empty);

						var bestStoreNode = game.SelectSingleNode("div[contains(@class, 'details')]/a[last()]/div");
						foundGame.BestPercentOff = int.Parse(bestStoreNode?.SelectSingleNode("span").InnerText ?? "0");
						if (bestStoreNode != null)
						{
							var storeDetails = Regex.Match(bestStoreNode.InnerText, "(.+) \\$([\\d\\.]+) ");
							foundGame.BestPercentStore = storeDetails.Groups[1].Value;
							if (decimal.TryParse(storeDetails.Groups[2].Value ?? "NULL", out var regularPrice))
							{
								foundGame.BestPercentStoreRegularPrice = regularPrice;
							}
						}

						foundGame.RawHtml = game.OuterHtml;
						foundGame.AddedOn = DateTime.Now;

						foundGames.Add(foundGame);
					}
				}

				_logger.LogInformation($"Parsing complete. Loading existing DB records...");
				var allExistingGames = _sqliteProvider.GetAllDataRecords<GameSale>();
				var missingGames = allExistingGames.Where(ag => !foundGames.Any(fg => fg.PlainGameID == ag.PlainGameID)).ToArray();
				var differentPriceGames = foundGames.Where(fg => allExistingGames.FirstOrDefault(ag => ag.PlainGameID == fg.PlainGameID)?.BestPrice != fg.BestPrice).ToArray();
				var newGames = foundGames.Where(fg => !allExistingGames.Any(ag => ag.PlainGameID == fg.PlainGameID)).ToArray();
				_logger.LogInformation($"Found {allExistingGames.Count} existing games. {missingGames.Length} to be deleted, {differentPriceGames.Length} to be updated, and {newGames.Length} to be added.");

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

				if (foundGames.Any())
				{
					// TODO: If there is a different sale for an existing record, update the message
					if (differentPriceGames.Any())
					{

					}

					// Add any new sales to the table and send a message
					if (newGames.Any() && hasAnnouncementChannel && loadedChannels[announcementChannelID] != null)
					{
						var embed = new EmbedBuilder
						{
							Color = new Color(0x7c0089),
						};

						foreach (var newGame in newGames)
						{
							var allComponents = new List<IMessageComponent>();

							// Basic embed fields
							string store = $"{(string.IsNullOrWhiteSpace(newGame.BestPercentStore) ? string.Empty : $" on {newGame.BestPercentStore}")}";
							embed.Title = $"{newGame.Title} - ${newGame.BestPrice} ({newGame.BestPercentOff}% off)";
							string regPrice = $"{(newGame.BestPercentStoreRegularPrice.HasValue ? $"Regular price **${newGame.BestPercentStoreRegularPrice}**" : string.Empty)}";
							string reviews = $"{(string.IsNullOrWhiteSpace(newGame.Reviews) ? string.Empty : $"*{newGame.Reviews}*")}";
							string details = $"{regPrice}{(!string.IsNullOrWhiteSpace(regPrice) ? "\n\n" : string.Empty)}{reviews}";
							if (!string.IsNullOrWhiteSpace(details))
							{
								embed.Description = details;
							}
							if (!string.IsNullOrWhiteSpace(newGame.BestPercentStoreLink))
							{
								embed.Url = newGame.BestPercentStoreLink;

								// Link button component
								allComponents.Add(ButtonBuilder.CreateLinkButton("Store Page", newGame.BestPercentStoreLink, new Emoji("💲")).Build());
							}

							// Link button component
							allComponents.Add(ButtonBuilder.CreateLinkButton("Deal Page", newGame.IsThereAnyDealLink, new Emoji("ℹ️")).Build());

							// Try to retrieve the image URL of the game
							response = await _httpClient.GetAsync(newGame.IsThereAnyDealLink);
							if (response.IsSuccessStatusCode)
							{
								responseString = await response.Content.ReadAsStringAsync();
								doc.LoadHtml(responseString);

								var gameImg = doc.DocumentNode?.SelectSingleNode("//div[@id='gameHead__img']");
								if (gameImg != null)
								{
									string style = gameImg.GetAttributeValue("style", string.Empty);
									if (!string.IsNullOrWhiteSpace(style))
									{
										var imgUrlMatch = Regex.Match(style, "background-image:url\\('(.+)'\\);");
										if (imgUrlMatch.Success)
										{
											embed.ImageUrl = imgUrlMatch.Groups[1].Value;
										}
									}
								}
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

		private class DealSearchResponse
		{
			public Data data { get; set; }

			public class Data
			{
				public string html { get; set; }
			}
		}
	}
}