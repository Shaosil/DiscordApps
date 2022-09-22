﻿using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Interfaces;
using System;
using System.Text;
using System.Threading.Tasks;

namespace ShaosilBot.Singletons
{
    public class DiscordRestClientProvider : IDiscordRestClientProvider
    {
        private readonly ILogger<DiscordRestClientProvider> _logger;
        public DiscordRestClient Client { get; private set; }

        public DiscordRestClientProvider(ILogger<DiscordRestClientProvider> logger)
        {
            _logger = logger;
            Client = new DiscordRestClient();

            Client.Log += (msg) => Task.Run(() => LogRestMessage(msg));
            Client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BotToken")).GetAwaiter().GetResult();
        }

        private void LogRestMessage(LogMessage message)
        {
            var sb = new StringBuilder($"REST CLIENT: {message.Message}");
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

        public async Task<RestTextChannel> GetChannelAsync(ulong channelId)
        {
            return await Client.GetChannelAsync(channelId) as RestTextChannel;
        }

        public async Task<RestInteraction> ParseHttpInteractionAsync(string publicKey, string signature, string timestamp, string body)
        {
            return await Client.ParseHttpInteractionAsync(publicKey, signature, timestamp, body);
        }
    }
}