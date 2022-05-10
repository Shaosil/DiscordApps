using Discord;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ShaosilBot.DependencyInjection
{
    public class DiscordRestClientProvider
    {
        private readonly static DiscordRestClient _client = new DiscordRestClient();

        public static DiscordRestClient GetRestClient(IServiceProvider provider)
        {
            if (_client.LoginState != LoginState.LoggedIn)
            {
                bool ready = false;
                var logger = provider.GetService<ILogger<DiscordRestClient>>();
                // Todo: will this get hit more than once?
                _client.Log += async (msg) => await Task.Run(() => logger.LogInformation($"REST CLIENT: {msg}"));
                _client.LoggedIn += async () => await Task.Run(() => ready = true);
                _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BotToken")).GetAwaiter().GetResult();

                // Stall here until socket client is ready
                while (!ready) { Thread.Sleep(10); }
            }

            return _client;
        }
    }
}
