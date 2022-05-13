using System;
using System.Net;
using System.Threading.Tasks;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ShaosilBot.DependencyInjection;

namespace ShaosilBot
{
    public class KeepAlive
    {
        private readonly ILogger<KeepAlive> _logger;

        public KeepAlive(ILogger<KeepAlive> logger, DiscordSocketClient socketClient, DiscordRestClient restClient)
        {
            _logger = logger;
        }

        // Pinging this every X minutes (see functioTimeout in host.json) should guarantee we stay online
        [Function("KeepAlive")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            // The client services will resolve in the constructor and trigger their initializations
            await DiscordSocketClientProvider.KeepAlive();
            _logger.LogInformation($"Keep alive function executed at: {DateTime.Now}");
            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}