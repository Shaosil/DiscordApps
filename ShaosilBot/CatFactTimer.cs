using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Discord;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ShaosilBot.Providers;
using ShaosilBot.Singletons;

namespace ShaosilBot
{
    public class CatFactTimer
    {
        private readonly ILogger _logger;
        private readonly DiscordSocketClientProvider _socketClientProvider;
        private readonly CatFactsProvider _catFactsProvider;

        public CatFactTimer(ILogger<CatFactTimer> logger, DiscordSocketClientProvider socketClientProvider, CatFactsProvider catFactsProvider)
        {
            _logger = logger;
            _socketClientProvider = socketClientProvider;
            _catFactsProvider = catFactsProvider;
        }

        [Function("CatFactTimer")]
        public async Task Run([TimerTrigger("0 0 0-2,14-23 * * *", RunOnStartup = false)] TimerInfo myTimer)
        {
            // Skip while debugging
            if (Debugger.IsAttached) return;

            _logger.LogInformation($"CatFactTimer triggered at: {DateTime.Now}");

            // Broadcast cat facts to all!
            var currentSubscribers = await _catFactsProvider.GetSubscribersAsync();
            foreach (var subscriber in currentSubscribers)
            {
                var user = await _socketClientProvider.Client.GetUserAsync(subscriber.ID);
                string randomCatFact = await _catFactsProvider.GetRandomCatFact();
                await user.SendMessageAsync($"Heyo neighbor! It's time for your hourly cat fact digest! *Did you know?*\n\n**\"{randomCatFact}\"**\n\nSee you next time!\nText STOP to unsubscribe at any time.");
            }
        }
    }
}