using System;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ShaosilBot.Interfaces;

namespace ShaosilBot
{
    public class KeepAlive
    {
        private readonly ILogger<KeepAlive> _logger;
        private readonly IDiscordSocketClientProvider _socketClientProvider;
        private readonly IDiscordRestClientProvider _restClientProvider;

        public KeepAlive(ILogger<KeepAlive> logger, IDiscordSocketClientProvider socketClientProvider, IDiscordRestClientProvider restClientProvider)
        {
            _logger = logger;
            _socketClientProvider = socketClientProvider;
            _restClientProvider = restClientProvider;
        }

        // Called by a Google Cloud scheduled job every 5 minutes
        [Function("KeepAlive")]
        public HttpResponseData Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            // The client services will resolve in the constructor and trigger their initializations
            _socketClientProvider.KeepAlive();
            _logger.LogInformation($"Keep alive function executed at: {DateTime.Now}");
            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}