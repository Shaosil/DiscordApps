using System;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

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
        public void Run([TimerTrigger("0 */9 * * * *", RunOnStartup = false)]TimerInfo myTimer)
        {
            _logger.LogInformation($"Keep alive function executed at: {DateTime.Now}");
        }
    }
}