using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using ShaosilBot.Providers;
using System;
using System.Text;
using System.Threading.Tasks;

namespace ShaosilBot.Singletons
{
    public class DiscordSocketClientProvider
    {
        private readonly ILogger<DiscordSocketClientProvider> _logger;
        private readonly DiscordSocketClient _client;

        public DiscordSocketClientProvider(ILogger<DiscordSocketClientProvider> logger, DiscordSocketConfig config, SlashCommandProvider slashCommandProvider)
        {
            _logger = logger;
            _client = new DiscordSocketClient(config);

            // Initialize bot and login
            _client.Log += async (msg) => await Task.Run(() => LogSocketMessage(msg));
            _client.Ready += async () =>
            {
                KeepAlive();
                await slashCommandProvider.BuildGuildCommands(_client);
            };
            //Client.MessageReceived += MessageHandler;

            _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BotToken")).GetAwaiter().GetResult();
            _client.StartAsync().GetAwaiter().GetResult();
        }

        public void KeepAlive()
        {
            _client.SetGameAsync("/help").GetAwaiter().GetResult();
        }

        private void LogSocketMessage(LogMessage message)
        {
            var sb = new StringBuilder($"SOCKET CLIENT: {message.Message}");
            if (message.Exception != null)
                _logger.LogError(message.Exception, sb.ToString());
            else
            {
                switch (message.Severity)
                {
                    case LogSeverity.Debug: _logger.LogTrace(sb.ToString()); break;
                    case LogSeverity.Verbose: _logger.LogDebug(sb.ToString()); break;
                    case LogSeverity.Info: _logger.LogInformation(sb.ToString()); break;
                    case LogSeverity.Warning: _logger.LogWarning(sb.ToString()); break;
                    case LogSeverity.Error: _logger.LogError(sb.ToString()); break;
                }
            }
        }
    }
}