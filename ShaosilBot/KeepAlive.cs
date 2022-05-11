using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ShaosilBot.DependencyInjection;

namespace ShaosilBot
{
    public class KeepAlive
    {
        private readonly ILogger<KeepAlive> _logger;

        public KeepAlive(ILogger<KeepAlive> logger)
        {
            _logger = logger;
        }

        [Function("KeepAlive")]
        public async Task Run([TimerTrigger("0 */9 * * * *", RunOnStartup = true)]TimerInfo myTimer)
        {
            await DiscordSocketClientProvider.KeepAlive();
            _logger.LogInformation($"Keep alive function executed at: {DateTime.Now}");
        }
    }
}