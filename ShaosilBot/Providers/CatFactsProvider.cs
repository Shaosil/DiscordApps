using ShaosilBot.Singletons;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShaosilBot.Providers
{
    public class CatFactsProvider
    {
        private readonly DataBlobProvider _blobProvider;

        public CatFactsProvider(DataBlobProvider blobProvider)
        {
            _blobProvider = blobProvider;
        }

        public async Task<string> GetRandomCatFact()
        {
            var _catFacts = (await _blobProvider.GetBlobTextAsync("CatFacts.txt")).Split(Environment.NewLine);
            return _catFacts[Random.Shared.Next(_catFacts.Length)];
        }

        public async Task<List<Subscriber>> GetSubscribersAsync()
        {
            return JsonSerializer.Deserialize<List<Subscriber>>(await _blobProvider.GetBlobTextAsync("CatFactsSubscribers.json"));
        }

        public async Task UpdateSubscribersAsync(List<Subscriber> subscribers)
        {
            string json = JsonSerializer.Serialize(subscribers, new JsonSerializerOptions { WriteIndented = true });
            await _blobProvider.SaveBlobTextAsync("CatFactsSubscribers.json", json);
        }

        public class Subscriber
        {
            public ulong ID { get; set; }

            public string FriendlyName { get; set; }

            public DateTimeOffset DateSubscribed { get; set; }

            public int TimesUnsubscribed { get; set; }
        }
    }
}