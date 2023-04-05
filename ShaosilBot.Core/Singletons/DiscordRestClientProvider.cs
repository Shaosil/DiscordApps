using Discord;
using Discord.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using System.Text;

namespace ShaosilBot.Core.Singletons
{
	public class DiscordRestClientProvider : IDiscordRestClientProvider
	{
		private readonly ILogger<DiscordRestClientProvider> _logger;
		private readonly IConfiguration _configuration;

		public DiscordRestClient Client { get; private set; }

		// Helper properties
		public IReadOnlyCollection<IGuild> Guilds { get; private set; }

		public DiscordRestClientProvider(ILogger<DiscordRestClientProvider> logger, IConfiguration configuration)
		{
			_logger = logger;
			_configuration = configuration;
			Client = new DiscordRestClient();
		}

		public async Task Init()
		{
			Client.Log += (msg) => Task.Run(() => LogRestMessage(msg));
			await Client.LoginAsync(TokenType.Bot, _configuration["BotToken"]);
			Guilds = await Client.GetGuildsAsync();
		}

		public async Task<RestTextChannel> GetChannelAsync(ulong channelId)
		{
			return (RestTextChannel)await Client.GetChannelAsync(channelId);
		}

		public async Task<RestInteraction> ParseHttpInteractionAsync(string publicKey, string signature, string timestamp, string body)
		{
			return await Client.ParseHttpInteractionAsync(publicKey, signature, timestamp, body);
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

		public async Task DMShaosil(string message)
		{
			var dmChannel = await (await Client.GetUserAsync(392127164570664962)).CreateDMChannelAsync();
			await dmChannel.SendMessageAsync(message);
		}
	}
}