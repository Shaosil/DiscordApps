using System;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ShaosilBot.SlashCommands;

namespace ShaosilBot
{
    public class CatFactTimer
    {
        private readonly ILogger _logger;
        private readonly HttpClient _client;
        private readonly DiscordSocketClient _socketClient;

        public CatFactTimer(ILogger<CatFactTimer> logger, IHttpClientFactory httpClientFactory, DiscordSocketClient socketClient)
        {
            _logger = logger;
            _client = httpClientFactory.CreateClient();
            _socketClient = socketClient;
        }

        [Function("CatFactTimer")]
        public async Task Run([TimerTrigger("0 0 0-2,14-23 * * *", RunOnStartup = false)] TimerInfo myTimer)
        {
            _logger.LogInformation($"CatFactTimer triggered at: {DateTime.Now}");

            // Broadcast cat facts to all!
            var currentSubscribers = await CatFactsCommand.GetSubscribersAsync(_client);
            foreach (var subscriber in currentSubscribers)
            {
                var user = await _socketClient.GetUserAsync(subscriber.IDNum);
                await user.SendMessageAsync($"Heyo neighbor! It's time for your hourly cat fact digest! *Did you know?*\n\n**\"{CatFactsCommand.RandomCatFact}\"**\n\nSee you next time!\nText STOP to unsubscribe at any time.");
            }
        }
    }
}