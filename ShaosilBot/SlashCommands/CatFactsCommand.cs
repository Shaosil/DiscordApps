using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class CatFactsCommand : BaseCommand
    {
        public CatFactsCommand(ILogger logger) : base(logger) { }

        public static async Task<string> GetRandomCatFact()
        {
            var _catFacts = (await DataBlobProvider.GetBlobText("CatFacts.txt")).Split(Environment.NewLine);
            return _catFacts[Random.Shared.Next(_catFacts.Length)];
        }

        public override async Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            // Get current subscribers asynchronously and add this one to the list if they do not exist
            _ = Task.Run(async () =>
            {
                var currentSubscribers = await GetSubscribersAsync();
                if (!currentSubscribers.Any(s => s.IDNum == command.User.Id))
                {
                    currentSubscribers.Add(new Subscriber { ID = command.User.Id.ToString(), FriendlyName = command.User.Username, DateSubscribed = DateTimeOffset.Now, TimesUnsubscribed = 0 });
                    await UpdateSubscribersAsync(currentSubscribers);
                }
            });

            return await Task.FromResult(command.Respond(await GetRandomCatFact()));
        }

        public static async Task<List<Subscriber>> GetSubscribersAsync()
        {
            return JsonSerializer.Deserialize<List<Subscriber>>(await DataBlobProvider.GetBlobText("CatFactsSubscribers.json"));
        }

        public static async Task UpdateSubscribersAsync(List<Subscriber> subscribers)
        {
            string json = JsonSerializer.Serialize(subscribers);
            await DataBlobProvider.SaveBlobText("CatFactsSubscribers.json", json);
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