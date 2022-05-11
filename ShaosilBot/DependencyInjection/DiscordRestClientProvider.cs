using Discord;
using Discord.Rest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
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
                var logger = provider.GetService<ILogger<DiscordRestClient>>();
                // Todo: will this get hit more than once?
                _client.Log += async (msg) => await Task.Run(() => logger.LogInformation($"REST CLIENT: {msg}"));
                _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BotToken")).GetAwaiter().GetResult();
            }

            return _client;
        }
    }
}
