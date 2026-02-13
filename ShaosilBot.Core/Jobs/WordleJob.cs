using Microsoft.Extensions.Logging;
using Quartz;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.SQLite;

namespace ShaosilBot.Core.Jobs
{
	public class WordleJob : IJob
	{
		private readonly ILogger<WordleJob> _logger;
		private readonly IDiscordRestClientProvider _discordRestClientProvider;
		private readonly ISQLiteProvider _sqliteProvider;

		public WordleJob(ILogger<WordleJob> logger, IDiscordRestClientProvider discordRestClientProvider, ISQLiteProvider sqliteProvider)
		{
			_logger = logger;
			_discordRestClientProvider = discordRestClientProvider;
			_sqliteProvider = sqliteProvider;
		}

		public async Task Execute(IJobExecutionContext context)
		{
			// Delete all of the previous day's games that were created more than 10 seconds ago
			var activeDailyGames = _sqliteProvider.GetDataRecords<WordleGame>(g => g.IsDaily && g.StartedTimestamp.AddSeconds(10) < DateTimeOffset.Now);
			if (activeDailyGames.Count > 0)
			{
				_logger.LogInformation($"Deleting {activeDailyGames.Count} active daily Wordle games from yesterday.");
				_sqliteProvider.DeleteDataRecords(activeDailyGames.ToArray());
			}
			else
			{
				_logger.LogInformation("No active daily Wordle games found, nothing to delete.");
			}
		}
	}
}