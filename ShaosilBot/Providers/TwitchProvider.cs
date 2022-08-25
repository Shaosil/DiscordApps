using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Singletons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShaosilBot.Providers
{
    public class TwitchProvider
    {
        private readonly ILogger<TwitchProvider> _logger;
        private readonly DiscordRestClientProvider _restClientProvider;
        private readonly DataBlobProvider _blobProvider;
        private readonly HttpClient _httpClient;

        public TwitchProvider(ILogger<TwitchProvider> logger, IHttpClientFactory httpClientFactory, DataBlobProvider blobProvider, DiscordRestClientProvider restClientProvider)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _blobProvider = blobProvider;
            _restClientProvider = restClientProvider;
        }

        public async Task HandleNotification(TwitchPayload payload)
        {
            _logger.LogInformation($"Received twitch payload type '{payload.subscription.type}' for streamer '{payload.event_type.broadcaster_user_login}'.");

            // Prep some variables
            string oauthToken = await GetOAuthAccessToken();
            string clientId = Environment.GetEnvironmentVariable("TwitchClientID");
            var discordChannel = await _restClientProvider.Client.GetChannelAsync(786668753407705160) as RestTextChannel;
            RestUserMessage lastMessage = null;
            string twitchLink = $"https://twitch.tv/{payload.event_type.broadcaster_user_login}";
            var embed = new EmbedBuilder
            {
                Color = new Color(0x7c0089),
                Url = twitchLink
            };

            // Helper API call function
            Func<string, Task<string>> getHttpResponse = async (url) =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", $"Bearer {oauthToken}");
                request.Headers.Add("Client-Id", clientId);
                return await (await _httpClient.SendAsync(request)).Content.ReadAsStringAsync();
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
                string channelResponse = await getHttpResponse($"https://api.twitch.tv/helix/channels?broadcaster_id={payload.event_type.broadcaster_user_id}");
                var channelInfo = JsonSerializer.Deserialize<ChannelInfoRoot>(channelResponse).Channels.First();

                // Game image URL
                string gameResponse = await getHttpResponse($"https://api.twitch.tv/helix/games?id={payload.event_type.category_id ?? channelInfo.game_id}");
                string gameUrl = JsonDocument.Parse(gameResponse).RootElement.GetProperty("data")[0].GetProperty("box_art_url").GetString().Replace("-{width}x{height}", string.Empty);

                // Always set image and description from these events
                embed.ImageUrl = gameUrl;
                embed.Description = $"**{payload.event_type.category_name ?? channelInfo.game_name}**\n\n{payload.event_type.title ?? channelInfo.title}";
            }

            // Load last live message for this Twitch user
            _logger.LogInformation("Attempting to find last announcement message for this streamer.");
            var messages = (await discordChannel.GetMessagesAsync(25).FlattenAsync()).OrderByDescending(m => m.Timestamp).ToList();
            lastMessage = messages.FirstOrDefault(m => m.Author.Username == "ShaosilBot" && new Regex($"^(🔴 \\[LIVE\\] )?{payload.event_type.broadcaster_user_name} ").IsMatch(m.Embeds.First().Title)) as RestUserMessage;
            double hoursSinceLastMessage = (DateTimeOffset.UtcNow - (lastMessage?.Timestamp.UtcDateTime ?? new DateTimeOffset())).TotalHours;

            if (isOnlineEvent && hoursSinceLastMessage >= 1)
            {
                // Get user thumbnail URL
                _logger.LogInformation("Getting user information for thumbnail.");
                string userResponse = await getHttpResponse($"https://api.twitch.tv/helix/users?id={payload.event_type.broadcaster_user_id}");
                string userThumbnail = JsonDocument.Parse(userResponse).RootElement.GetProperty("data")[0].GetProperty("profile_image_url").GetString();

                // Online-specific embed details
                embed.Title = $"🔴 [LIVE] {payload.event_type.broadcaster_user_name} started streaming on Twitch <t:{DateTimeOffset.Parse(payload.event_type.started_at).ToUnixTimeSeconds()}:R>!";
                embed.ThumbnailUrl = userThumbnail;
            }
            else if (isOfflineEvent)
            {
                // Get time spent description
                string startTimeEpoch = Regex.Match(lastMessage.Embeds.First().Title, "<t:(\\d+):R>").Groups[1].Value;
                var startDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(startTimeEpoch));
                var streamLength = DateTimeOffset.UtcNow - startDate;
                int hours = 0, minutes = (int)Math.Round(streamLength.TotalMinutes);
                while (minutes >= 60)
                {
                    hours++;
                    minutes -= 60;
                }
                string timeDesc = $"{(hours > 0 ? ($"{hours} hour{(hours == 1 ? string.Empty : "s")} and ") : string.Empty)}{minutes} minute{(minutes == 1 ? string.Empty : "s")}";
                embed.Title = $"{payload.event_type.broadcaster_user_name} was live on Twitch <t:{startTimeEpoch}:R> for {timeDesc}.";
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
                await discordChannel.SendMessageAsync(components: component, embed: embed.Build());
            }
            // Update any existing one within an hour unless this is a channel update even without a live channel message
            else if (lastMessage != null && (!isChannelUpdateEvent || embed.Title.Contains("[LIVE]")))
            {
                _logger.LogInformation("Sending update message");
                await (lastMessage.ModifyAsync(p => { p.Embed = embed.Build(); }) ?? Task.CompletedTask);
            }
            else
                _logger.LogInformation("Either existing message not found, or channel updated without LIVE message. End processing.");
        }

        private async Task<string> GetOAuthAccessToken()
        {
            var oauthInfo = JsonSerializer.Deserialize<OAuthInfo>(await _blobProvider.GetBlobTextAsync("TwitchOAuth.txt"));
            if (oauthInfo.Expires.UtcDateTime.AddSeconds(-10) < DateTime.UtcNow)
            {
                // Request new token and save
                var request = new HttpRequestMessage(HttpMethod.Post, "https://id.twitch.tv/oauth2/token");
                request.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", Environment.GetEnvironmentVariable("TwitchClientID")),
                    new KeyValuePair<string, string>("client_secret", Environment.GetEnvironmentVariable("TwitchClientSecret")),
                    new KeyValuePair<string, string>("grant_type", "client_credentials")
                });
                var response = await _httpClient.SendAsync(request);

                var bodyData = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
                oauthInfo.Token = bodyData.GetProperty("access_token").GetString();
                oauthInfo.Expires = DateTimeOffset.Now.AddSeconds(bodyData.GetProperty("expires_in").GetInt32());
                await _blobProvider.SaveBlobTextAsync("TwitchOAuth.txt", JsonSerializer.Serialize(oauthInfo));
            }

            return oauthInfo.Token;
        }

        private class OAuthInfo
        {
            public string Token { get; set; }

            public DateTimeOffset Expires { get; set; }
        }

        public class TwitchPayload
        {
            public string challenge { get; set; }
            public Subscription subscription { get; set; }
            [JsonPropertyName("event")]
            public Event event_type { get; set; }

            public class Subscription
            {
                public string id { get; set; }
                public string status { get; set; }
                public string type { get; set; }
                public string version { get; set; }
                public int cost { get; set; }
                public Condition condition { get; set; }
                public Transport transport { get; set; }
                public DateTime created_at { get; set; }

                public class Condition
                {
                    public string broadcaster_user_id { get; set; }
                }

                public class Transport
                {
                    public string method { get; set; }
                    public string callback { get; set; }
                }
            }

            public class Event
            {
                public string user_id { get; set; }
                public string user_login { get; set; }
                public string user_name { get; set; }
                public string broadcaster_user_id { get; set; }
                public string broadcaster_user_login { get; set; }
                public string broadcaster_user_name { get; set; }
                public string started_at { get; set; }
                public string title { get; set; }
                public string category_name { get; set; }
                public string category_id { get; set; }
            }
        }

        public class ChannelInfoRoot
        {
            [JsonPropertyName("data")]
            public List<ChannelInfo> Channels { get; set; } = new List<ChannelInfo>();

            public class ChannelInfo
            {
                public string broadcaster_id { get; set; }
                public string broadcaster_login { get; set; }
                public string broadcaster_name { get; set; }
                public string broadcaster_language { get; set; }
                public string game_id { get; set; }
                public string game_name { get; set; }
                public string title { get; set; }
                public int delay { get; set; }
            }
        }
    }
}