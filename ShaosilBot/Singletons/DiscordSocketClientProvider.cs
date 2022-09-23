using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using ShaosilBot.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShaosilBot.Singletons
{
	public class DiscordSocketClientProvider : IDiscordSocketClientProvider
    {
        private readonly ILogger<DiscordSocketClientProvider> _logger;
        private readonly DiscordSocketClient _client;

        public DiscordSocketClientProvider(ILogger<DiscordSocketClientProvider> logger, DiscordSocketConfig config, ISlashCommandProvider slashCommandProvider)
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
			CleanupNoNoZone();
		}

		private void CleanupNoNoZone()
		{
			// Delete all messages that are older than one hour
			var channel = _client?.GetChannelAsync(1022371866272346112).GetAwaiter().GetResult() as RestTextChannel;
			var messages = channel?.GetMessagesAsync().FlattenAsync().GetAwaiter().GetResult() ?? new List<RestMessage>();
			var oldMessages = messages.Where(m => m.CreatedAt.AddHours(1) < DateTimeOffset.Now);

			if (oldMessages.Any())
				channel.DeleteMessagesAsync(oldMessages).GetAwaiter().GetResult();
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