using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class CatFactsCommand : BaseCommand
    {
        private readonly HttpClient _client;

        public CatFactsCommand(ILogger logger, HttpClient client) : base(logger)
        {
            _client = client;
        }

        public override async Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            // Get current subscribers asynchronously and add this one to the list if they do not exist
            _ = Task.Run(async () =>
            {
                var currentSubscribers = await GetSubscribersAsync(_client);
                if (!currentSubscribers.Any(s => s.IDNum == command.User.Id))
                {
                    currentSubscribers.Add(new Subscriber { ID = command.User.Id.ToString(), FriendlyName = command.User.Username, DateSubscribed = DateTimeOffset.Now, TimesUnsubscribed = 0 });
                    await _client.PutAsync(Environment.GetEnvironmentVariable("CatFactsSubscribers"), JsonContent.Create(currentSubscribers));
                }
            });

            return await Task.FromResult(command.Respond(DataBlobProvider.RandomCatFact));
        }

        public static async Task<List<Subscriber>> GetSubscribersAsync(HttpClient client)
        {
            string subscribersUrl = Environment.GetEnvironmentVariable("CatFactsSubscribers");
            var response = await client.GetAsync(subscribersUrl);
            string content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<Subscriber>>(content);
        }

        public static async Task UpdateSubscribersAsync(List<Subscriber> subscribers, HttpClient client)
        {
            string json = JsonSerializer.Serialize(subscribers);
            var content = new StringContent(json, null, MediaTypeNames.Application.Json);
            await client.PutAsync(Environment.GetEnvironmentVariable("CatFactsSubscribers"), content);
        }

        public class Subscriber
        {
            [JsonPropertyName("id")]
            public string ID { get; set; } // Store this as a string because jsonblob.com is zeroing out the end of ulongs for some reason

            [JsonIgnore]
            public ulong IDNum => ulong.Parse(ID);

            [JsonPropertyName("friendlyName")]
            public string FriendlyName { get; set; }

            [JsonPropertyName("dateSubscribed")]
            public DateTimeOffset DateSubscribed { get; set; }

            [JsonPropertyName("timesUnsubscribed")]
            public int TimesUnsubscribed { get; set; }
        }
    }
}