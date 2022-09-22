using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Discord.Rest;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ShaosilBot.Interfaces;

namespace ShaosilBot
{
    public class SendText
    {
        private readonly ILogger<KeepAlive> _logger;
        private readonly IDiscordRestClientProvider _restClientProvider;

        public SendText(ILogger<KeepAlive> logger, IDiscordRestClientProvider restClientProvider)
        {
            _logger = logger;
            _restClientProvider = restClientProvider;
        }

        [Function("SendText")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation($"Send text function executed at: {DateTime.Now}");

            // Default to the bot-test channel unless specified in Text-Channel header
            if (!ulong.TryParse(req.Headers.GetValues("Text-Channel").FirstOrDefault(), out var channelId))
                channelId = 971047774311288983;

            // Return no content if no message was provided
            string message = await req.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(message))
                return req.CreateResponse(HttpStatusCode.NoContent);

            // Send message to channel
            var channel = await _restClientProvider.GetChannelAsync(channelId);
            await channel.SendMessageAsync(message);
            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}