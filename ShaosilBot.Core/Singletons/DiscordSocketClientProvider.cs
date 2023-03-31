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
		private readonly ISlashCommandProvider _slashCommandProvider;
		private readonly IMessageCommandProvider _messageCommandProvider;

		public static DiscordSocketClient Client { get; private set; }

		public DiscordSocketClientProvider(ILogger<DiscordSocketClientProvider> logger,
			IConfiguration configuration,
			DiscordSocketConfig config,
			IDiscordGatewayMessageHandler messageHandler,
			ISlashCommandProvider slashCommandProvider,
			IMessageCommandProvider messageCommandProvider)
		{
			_logger = logger;
			_configuration = configuration;
			_messageHandler = messageHandler;
			_slashCommandProvider = slashCommandProvider;
			_messageCommandProvider = messageCommandProvider;
			Client = new DiscordSocketClient(config);
		}

		public void Init(bool isDevelopment)
		{
			// Initialize bot and login
			Client.Log += async (msg) => await Task.Run(() => LogSocketMessage(msg));
			Client.Ready += async () =>
			{
				await Client.SetGameAsync("/help for info, !c to chat");
				await _slashCommandProvider.BuildGuildCommands();
				await _messageCommandProvider.BuildMessageCommands();
			};

			if (!isDevelopment)
			{
				Client.UserJoined += _messageHandler.UserJoined;
				Client.UserLeft += _messageHandler.UserLeft;
				Client.MessageReceived += _messageHandler.MessageReceived;
				Client.ReactionAdded += _messageHandler.ReactionAdded;
				Client.ReactionRemoved += _messageHandler.ReactionRemoved;
			}

			Client.LoginAsync(TokenType.Bot, _configuration["BotToken"]).GetAwaiter().GetResult();
			Client.StartAsync().GetAwaiter().GetResult();
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