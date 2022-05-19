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
            _logger.LogInformation($"Received twitch payload type '{payload.subscription.type}'.");

            string oauthToken = await GetOAuthAccessToken();
            string clientId = Environment.GetEnvironmentVariable("TwitchClientID");
            var channel = await _restClientProvider.Client.GetChannelAsync(786668753407705160) as RestTextChannel;

            Func<string, Task<string>> getHttpResponse = async (url) =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Authorization", $"Bearer {oauthToken}");
                request.Headers.Add("Client-Id", clientId);
                return await (await _httpClient.SendAsync(request)).Content.ReadAsStringAsync();
            };

            Func<string, Task<RestUserMessage>> getLastActiveStreamMessage = async (twitchUserId) =>
            {
                var messages = await channel.GetMessagesAsync(25).FlattenAsync();
                return messages.OrderByDescending(m => m.Timestamp).FirstOrDefault(m => m.Author.Username == "ShaosilBot" && m.Embeds.First().Title.StartsWith($"🔴 [LIVE] {twitchUserId}")) as RestUserMessage;
            };

            // Always get current channel information
            string responseBody = await getHttpResponse($"https://api.twitch.tv/helix/channels?broadcaster_id={payload.@event.broadcaster_user_id}");
            var channelData = JsonSerializer.Deserialize<ChannelInfoRoot>(responseBody).Channels.First();
            string channelUrl = $"https://twitch.tv/{channelData.broadcaster_login}";

            // Pre-build the embed since we need it in all cases
            var embed = new EmbedBuilder
            {
                Color = new Color(0x7c0089),
                Description = $"**{channelData.game_name}**\n\n{channelData.title}",
                Url = channelUrl
            };

            switch (payload.subscription.type)
            {
                case "stream.online":
                    // Get current game URL
                    responseBody = await getHttpResponse($"https://api.twitch.tv/helix/games?id={channelData.game_id}");
                    var gameUrl = JsonDocument.Parse(responseBody).RootElement.GetProperty("data")[0].GetProperty("box_art_url").GetString().Replace("-{width}x{height}", string.Empty);

                    // Get user thumbnail URL
                    responseBody = await getHttpResponse($"https://api.twitch.tv/helix/users?id={channelData.broadcaster_id}");
                    var userThumbnail = JsonDocument.Parse(responseBody).RootElement.GetProperty("data")[0].GetProperty("profile_image_url").GetString();

                    // Build response
                    var components = new ComponentBuilder { ActionRows = new List<ActionRowBuilder> { new ActionRowBuilder { Components = new List<IMessageComponent>
                    {
                        ButtonBuilder.CreateLinkButton($"{channelData.broadcaster_name}'s Channel", channelUrl, new Emoji("📺")).Build()
                    } } } }.Build();

                    // Add details to embed
                    embed.Title = $"🔴 [LIVE] {channelData.broadcaster_name} started streaming on Twitch <t:{DateTimeOffset.Parse(payload.@event.started_at).ToUnixTimeSeconds()}:R>!";
                    embed.ThumbnailUrl = userThumbnail;
                    embed.ImageUrl = gameUrl;

                    // Send a message to the #twitch-golives channel (hardcoded but... meh)
                    await channel.SendMessageAsync(components: components, embed: embed.Build());
                    break;

                case "stream.offline":
                    // Get most recent message from bot in channel for broadcaster name
                    var lastMessage = await getLastActiveStreamMessage(channelData.broadcaster_name);

                    // Update embed details
                    string startTimeEpoch = Regex.Match(lastMessage.Embeds.First().Title, "<t:(\\d+):R>").Groups[1].Value;
                    var startDate = DateTimeOffset.FromUnixTimeSeconds(long.Parse(startTimeEpoch));
                    var streamLength = DateTimeOffset.UtcNow - startDate;
                    int hours = 0, minutes = (int)Math.Round(streamLength.TotalMinutes);
                    while (minutes >= 60)
                    {
                        hours++;
                        minutes -= 60;
                    }

                    // Persist the other embed information
                    embed.Title = $"{channelData.broadcaster_name} was live on Twitch <t:{startTimeEpoch}:R> for {($"{(hours > 0 ? $"{hours} hours and " : "")}{minutes} minutes")}!";
                    embed.ThumbnailUrl = lastMessage.Embeds.First().Thumbnail.Value.Url;
                    embed.ImageUrl = lastMessage.Embeds.First().Image.Value.Url;
                    await lastMessage.ModifyAsync(properties =>
                    {
                        properties.Embed = embed.Build();
                    });
                    break;

                case "channel.update":
                    // Get most recent message from bot in channel for broadcaster name
                    lastMessage = await getLastActiveStreamMessage(channelData.broadcaster_name);

                    // If none was found, nevermind
                    if (lastMessage == null) break;

                    // Get current game URL
                    responseBody = await getHttpResponse($"https://api.twitch.tv/helix/games?id={payload.@event.category_id}");
                    gameUrl = JsonDocument.Parse(responseBody).RootElement.GetProperty("data")[0].GetProperty("box_art_url").GetString().Replace("-{width}x{height}", string.Empty);

                    // Update embed details
                    embed.Title = lastMessage.Embeds.First().Title;
                    embed.Description = $"**{payload.@event.category_name}**\n\n{payload.@event.title}";
                    embed.ThumbnailUrl = lastMessage.Embeds.First().Thumbnail.Value.Url;
                    embed.ImageUrl = gameUrl;
                    await lastMessage.ModifyAsync(properties =>
                    {
                        properties.Embed = embed.Build();
                    });
                    break;

                default:
                    _logger.LogError("No handler exists for subscription type!");
                    break;
            }
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
            public Event @event { get; set; }

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
            public List<ChannelInfo> Channels { get; set; }

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