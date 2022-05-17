using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ShaosilBot.Singletons;

namespace ShaosilBot
{
    public class KeepAlive
    {
        private readonly ILogger<KeepAlive> _logger;
        private readonly DiscordSocketClientProvider _socketClientProvider;
        private readonly DiscordRestClientProvider _restClientProvider;

        public KeepAlive(ILogger<KeepAlive> logger, DiscordSocketClientProvider socketClientProvider, DiscordRestClientProvider restClientProvider)
        {
            _logger = logger;
            _socketClientProvider = socketClientProvider;
            _restClientProvider = restClientProvider;
        }

        // Pinging this every X minutes (see functioTimeout in host.json) should guarantee we stay online
        [Function("KeepAlive")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            // The client services will resolve in the constructor and trigger their initializations
            await _socketClientProvider.KeepAlive();
            _logger.LogInformation($"Keep alive function executed at: {DateTime.Now}");
            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}