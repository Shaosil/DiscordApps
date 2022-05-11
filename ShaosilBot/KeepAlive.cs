using System;
using System.Threading.Tasks;
using Discord.WebSocket;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using ShaosilBot.DependencyInjection;

namespace ShaosilBot
{
    public class KeepAlive
    {
        private readonly ILogger<KeepAlive> _logger;

        public KeepAlive(ILogger<KeepAlive> logger, DiscordSocketClient socketClient)
        {
            _logger = logger;
        }

        [Function("KeepAlive")]
        public async Task Run([TimerTrigger("0 */9 * * * *", RunOnStartup = true)]TimerInfo myTimer)
        {
            // The socket client service will resolve in the constructor and trigger its initialization
            await DiscordSocketClientProvider.KeepAlive();
            _logger.LogInformation($"Keep alive function executed at: {DateTime.Now}");
        }
    }
}