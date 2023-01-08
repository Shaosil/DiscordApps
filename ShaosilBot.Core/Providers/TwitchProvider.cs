using Discord;
using Discord.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.Twitch;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.Providers
{
	public class TwitchProvider : ITwitchProvider
	{
		private const string OAuthFileName = "TwitchOAuth.json";

		private readonly ILogger<TwitchProvider> _logger;
		private readonly IConfiguration _configuration;
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly IFileAccessHelper _fileAccessHelper;
		private readonly HttpClient _httpClient;

		public TwitchProvider(ILogger<TwitchProvider> logger,
			IConfiguration configuration,
			IHttpClientFactory httpClientFactory,
			IFileAccessHelper fileAccessHelper,
			IDiscordRestClientProvider restClientProvider)
		{
			_logger = logger;
			_configuration = configuration;
			_httpClient = httpClientFactory.CreateClient();
			_fileAccessHelper = fileAccessHelper;
			_restClientProvider = restClientProvider;
		}

		public async Task HandleNotification(TwitchPayload payload)
		{
			_logger.LogInformation($"Received twitch payload type '{payload.subscription.type}' for streamer '{payload.event_type.broadcaster_user_login}'.");

			// Prep some variables
			var discordChannel = await _restClientProvider.GetChannelAsync(786668753407705160);
			RestUserMessage lastMessage = null;
			string twitchLink = $"https://twitch.tv/{payload.event_type.broadcaster_user_login}";
			var embed = new EmbedBuilder
			{
				Color = new Color(0x7c0089),
				Url = twitchLink
			};

			// Set embed properties as needed
			bool isOnlineEvent = payload.subscription.type == "stream.online";
			bool isOfflineEvent = payload.subscription.type == "stream.offline";
			bool isChannelUpdateEvent = payload.subscription.type == "channel.update";

			// Get channel and game info for online and update events
			if (isOnlineEvent || isChannelUpdateEvent)
			{
				// Channel info for game name and ID
				_logger.LogInformation("Getting channel and game information for image and description.");
				string channelResponse = await GetHttpResponseAsync<string>($"https://api.twitch.tv/helix/channels?broadcaster_id={payload.event_type.broadcaster_user_id}");
				var channelInfo = JsonSerializer.Deserialize<ChannelInfoRoot>(channelResponse).Channels.First();

				// Game image URL
				string gameResponse = await GetHttpResponseAsync<string>($"https://api.twitch.tv/helix/games?id={payload.event_type.category_id ?? channelInfo.game_id}");
				string gameUrl = JsonDocument.Parse(gameResponse).RootElement.GetProperty("data")[0].GetProperty("box_art_url").GetString().Replace("-{width}x{height}", string.Empty);

				// Always set image and description from these events
				embed.ImageUrl = gameUrl;
				embed.Description = $"**{payload.event_type.category_name ?? channelInfo.game_name}**\n\n{payload.event_type.title ?? channelInfo.title}";
			}

			// Load last live message for this Twitch user
			_logger.LogInformation("Attempting to find last announcement message for this streamer.");
			var messages = (await discordChannel.GetMessagesAsync(25).FlattenAsync()).OrderByDescending(m => m.Timestamp).ToList();
			lastMessage = messages.FirstOrDefault(m => m.Author.Username == "ShaosilBot" && new Regex($"^(🔴 \\[LIVE\\] )?{payload.event_type.broadcaster_user_name} ").IsMatch(m.Embeds.First().Title)) as RestUserMessage;
			double hoursSinceLastMessage = lastMessage != null ? (DateTimeOffset.UtcNow - (lastMessage.EditedTimestamp ?? lastMessage.Timestamp).UtcDateTime).TotalHours : double.MaxValue;
			string originalStartTime = lastMessage != null ? Regex.Match(lastMessage.Embeds.First().Title, "<t:(\\d+):R>").Groups[1].Value : string.Empty;

			if (isOnlineEvent)
			{
				if (hoursSinceLastMessage >= 1)
				{
					// Get user thumbnail URL
					_logger.LogInformation("Getting user information for thumbnail.");
					var userResponse = await GetUsers(true, payload.event_type.broadcaster_user_id);
					string userThumbnail = userResponse.data.First().profile_image_url;

					// Online-specific embed details
					embed.ThumbnailUrl = userThumbnail;

					originalStartTime = payload.event_type.started_at;
				}

				embed.Title = $"🔴 [LIVE] {payload.event_type.broadcaster_user_name} started streaming on Twitch <t:{DateTimeOffset.Parse(originalStartTime).ToUnixTimeSeconds()}:R>!";
			}
			else if (isOfflineEvent)
			{
				// Get time spent description
				var startDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(originalStartTime));
				var streamLength = DateTimeOffset.UtcNow - startDate;
				int hours = 0, minutes = (int)Math.Round(streamLength.TotalMinutes);
				while (minutes >= 60)
				{
					hours++;
					minutes -= 60;
				}
				string timeDesc = $"{(hours > 0 ? ($"{hours} hour{(hours == 1 ? string.Empty : "s")} and ") : string.Empty)}{minutes} minute{(minutes == 1 ? string.Empty : "s")}";
				embed.Title = $"{payload.event_type.broadcaster_user_name} was live on Twitch <t:{originalStartTime}:R> for {timeDesc}.";
			}

			// Persist old embed info in new builder where needed
			var lastEmbed = lastMessage?.Embeds.First();
			embed.Title = embed.Title ?? lastEmbed?.Title;
			embed.Description = embed.Description ?? lastEmbed?.Description;
			embed.ImageUrl = embed.ImageUrl ?? lastEmbed?.Image.Value.Url;
			embed.ThumbnailUrl = embed.ThumbnailUrl ?? lastEmbed?.Thumbnail.Value.Url;

			// Send new message if the last one was over an hour ago, otherwise update the existing one if found
			if (isOnlineEvent && hoursSinceLastMessage >= 1)
			{
				// Build button component
				var component = ComponentBuilder.FromComponents(new[] { ButtonBuilder.CreateLinkButton($"{payload.event_type.broadcaster_user_name}'s Channel", twitchLink, new Emoji("📺")).Build() }).Build();

				// Send a message to the #twitch-golives channel (hardcoded but... meh)
				_logger.LogInformation("Sending new announcement message");
				await discordChannel.SendMessageAsync("<@&1018601398839037992>", components: component, embed: embed.Build());
			}
			// Update any existing one within an hour unless this is a channel update without a live channel message
			else if (lastMessage != null && (!isChannelUpdateEvent || embed.Title.Contains("[LIVE]")))
			{
				_logger.LogInformation("Sending update message");
				await (lastMessage.ModifyAsync(p => { p.Embed = embed.Build(); }) ?? Task.CompletedTask);
			}
			else
				_logger.LogInformation("Either existing message not found, or channel updated without LIVE message. End processing.");
		}

		public async Task<TwitchSubscriptions> GetSubscriptionsAsync()
		{
			return await GetHttpResponseAsync<TwitchSubscriptions>("https://api.twitch.tv/helix/eventsub/subscriptions");
		}

		public async Task<TwitchUsers> GetUsers(bool byID, params string[] parameters)
		{
			string allParams = string.Join("&", parameters.Select(param => $"{(byID ? "id" : "login")}={param}"));
			return await GetHttpResponseAsync<TwitchUsers>($"https://api.twitch.tv/helix/users?{allParams}");
		}

		public async Task<bool> PostSubscription(string userId)
		{
			string oauthToken = await GetOAuthAccessToken();
			string clientId = _configuration["TwitchClientID"];
			string twitchApiSecret = _configuration["TwitchAPISecret"];

			// Subscribe to stream.online, stream.offline, and channel.update events
			foreach (string twitchEvent in new[] { "stream.online", "stream.offline", "channel.update" })
			{
				var request = new HttpRequestMessage(HttpMethod.Post, "https://api.twitch.tv/helix/eventsub/subscriptions");
				request.Headers.Add("Authorization", $"Bearer {oauthToken}");
				request.Headers.Add("Client-Id", clientId);
				request.Content = new StringContent(JsonSerializer.Serialize(new
				{
					type = twitchEvent,
					version = 1,
					condition = new { broadcaster_user_id = userId },
					transport = new
					{
						method = "webhook",
						callback = "https://shaosilbot.ddnsfree.com/TwitchCallback",
						secret = twitchApiSecret
					}
				}));
				request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(MediaTypeNames.Application.Json);
				var response = await _httpClient.SendAsync(request);
				if (!response.IsSuccessStatusCode) return false;
			}

			return true;
		}

		public async Task<bool> DeleteSubscriptions(List<TwitchSubscriptions.Datum> subscriptions)
		{
			string oauthToken = await GetOAuthAccessToken();
			string clientId = _configuration["TwitchClientID"];

			foreach (var sub in subscriptions)
			{
				var request = new HttpRequestMessage(HttpMethod.Delete, $"https://api.twitch.tv/helix/eventsub/subscriptions?id={sub.id}");
				request.Headers.Add("Authorization", $"Bearer {oauthToken}");
				request.Headers.Add("Client-Id", clientId);
				var response = await _httpClient.SendAsync(request);

				if (!response.IsSuccessStatusCode) return false;
			}

			return true;
		}

		private async Task<T> GetHttpResponseAsync<T>(string url) where T : class
		{
			string oauthToken = await GetOAuthAccessToken();
			string clientId = _configuration["TwitchClientID"];

			var request = new HttpRequestMessage(HttpMethod.Get, url);
			request.Headers.Add("Authorization", $"Bearer {oauthToken}");
			request.Headers.Add("Client-Id", clientId);
			var response = await (await _httpClient.SendAsync(request)).Content.ReadAsStringAsync();
			return (typeof(T) == typeof(string)) ? response as T : JsonSerializer.Deserialize<T>(response);
		}

		private async Task<string> GetOAuthAccessToken()
		{
			var oauthInfo = JsonSerializer.Deserialize<OAuthInfo>(_fileAccessHelper.GetFileText(OAuthFileName, true));
			if (oauthInfo.Expires.UtcDateTime.AddSeconds(-10) < DateTime.UtcNow)
			{
				// Request new token and save
				var request = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token");
				request.Content = new FormUrlEncodedContent(new[]
				{
					new KeyValuePair<string, string>("client_id", _configuration["TwitchClientID"]),
					new KeyValuePair<string, string>("client_secret", _configuration["TwitchClientSecret"]),
					new KeyValuePair<string, string>("grant_type", "client_credentials")
				});
				var response = await _httpClient.SendAsync(request);

				var bodyData = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
				oauthInfo.Token = bodyData.GetProperty("access_token").GetString();
				oauthInfo.Expires = DateTimeOffset.Now.AddSeconds(bodyData.GetProperty("expires_in").GetInt32());
				_fileAccessHelper.SaveFileText(OAuthFileName, JsonSerializer.Serialize(oauthInfo));
			}
			else
				_fileAccessHelper.ReleaseFileLease(OAuthFileName);

			return oauthInfo.Token;
		}
	}
}