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
		private readonly DiscordSocketClient _client;

		public DiscordSocketClientProvider(ILogger<DiscordSocketClientProvider> logger,
			IConfiguration configuration,
			DiscordSocketConfig config,
			IDiscordGatewayMessageHandler messageHandler,
			ISlashCommandProvider slashCommandProvider)
		{
			_logger = logger;
			_configuration = configuration;
			_messageHandler = messageHandler;
			_slashCommandProvider = slashCommandProvider;
			_client = new DiscordSocketClient(config);
		}

		public async Task Init(bool isDevelopment)
		{
			// Initialize bot and login
			_client.Log += async (msg) => await Task.Run(() => LogSocketMessage(msg));
			_client.Ready += () => Task.Factory.StartNew(async () =>
			{
				try
				{
					// Start long running commands on a new thread
					await _client.SetGameAsync("/help for info, !c to chat");
					await _slashCommandProvider.BuildGuildCommands();
					await _slashCommandProvider.BuildAppCommands();
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Error during command building.");
				}
			}, TaskCreationOptions.LongRunning);

			// Only handle guild events in production
			if (!isDevelopment)
			{
				_client.UserJoined += _messageHandler.UserJoined;
				_client.UserLeft += _messageHandler.UserLeft;
				_client.MessageReceived += _messageHandler.MessageReceived;
				_client.ReactionAdded += _messageHandler.ReactionAdded;
				_client.ReactionRemoved += _messageHandler.ReactionRemoved;
			}

			await _client.LoginAsync(TokenType.Bot, _configuration["BotToken"]);
			await _client.StartAsync();
		}

		public async Task<bool> UserIsInVC(ulong userId)
		{
			// TODO: This doesn't load as SocketGuildUsers, so it always returns false
			return ((await _client.GetUserAsync(userId)) as SocketGuildUser)?.VoiceChannel != null;
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