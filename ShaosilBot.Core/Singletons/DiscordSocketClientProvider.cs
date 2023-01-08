using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using System.Text;

namespace ShaosilBot.Core.Singletons
{
	public class DiscordSocketClientProvider : IDiscordSocketClientProvider
	{
		private readonly ILogger<DiscordSocketClientProvider> _logger;
		private readonly IConfiguration _configuration;
		private readonly IDiscordGatewayMessageHandler _messageHandler;
		public DiscordSocketClient Client { get; private set; }

		public DiscordSocketClientProvider(ILogger<DiscordSocketClientProvider> logger,
			IConfiguration configuration,
			DiscordSocketConfig config,
			IDiscordGatewayMessageHandler messageHandler)
		{
			_logger = logger;
			_configuration = configuration;
			_messageHandler = messageHandler;
			Client = new DiscordSocketClient(config);
		}

		public void Init()
		{
			// Initialize bot and login
			Client.Log += async (msg) => await Task.Run(() => LogSocketMessage(msg));
			Client.Ready += async () => await Client.SetGameAsync("/help");
			//_client.MessageReceived += messageHandler.MessageReceived;
			Client.ReactionAdded += _messageHandler.ReactionAdded;
			Client.ReactionRemoved += _messageHandler.ReactionRemoved;

			Client.LoginAsync(TokenType.Bot, _configuration["BotToken"]).GetAwaiter().GetResult();
			Client.StartAsync().GetAwaiter().GetResult();
		}

		public void CleanupNoNoZone()
		{
			// Delete all messages that are older than 12 hours
			var channel = Client.GetGuild(628019972316069890)?.GetTextChannel(1022371866272346112);
			var messages = channel?.GetMessagesAsync().FlattenAsync().GetAwaiter().GetResult() ?? new List<IMessage>();
			var oldMessages = messages.Where(m => m.CreatedAt.AddHours(12) < DateTimeOffset.Now);

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