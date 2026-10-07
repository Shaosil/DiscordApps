using Microsoft.Extensions.Logging;
using Quartz;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Core.Jobs
{
	public class GameSaleNotifierJob : IJob
	{
		private readonly ILogger<GameSaleNotifierJob> _logger;
		private readonly IGameDealSearchProvider _gameDealSearchProvider;

		public GameSaleNotifierJob(ILogger<GameSaleNotifierJob> logger, IGameDealSearchProvider gameDealSearchProvider)
		{
			_logger = logger;
			_gameDealSearchProvider = gameDealSearchProvider;
		}

		public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
		{
			try
			{
				await _gameDealSearchProvider.DoDefaultSearch();
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Error searching for game deals");
				// TODO - Retry in 5 minutes (up to 3 times)
			}
		}
	}
}