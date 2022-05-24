using System;
using System.Net;
using System.Threading.Tasks;
using Discord.Rest;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ShaosilBot.Singletons;

namespace ShaosilBot
{
    public class SendText
    {
        private readonly ILogger<KeepAlive> _logger;
        private readonly DiscordRestClientProvider _restClientProvider;

        public SendText(ILogger<KeepAlive> logger, DiscordRestClientProvider restClientProvider)
        {
            _logger = logger;
            _restClientProvider = restClientProvider;
        }

        [Function("SendText")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Function, "get", Route = null)] HttpRequestData req)
        {
            _logger.LogInformation($"Send text function executed at: {DateTime.Now}");

            // Default to the bot-test channel unless specified in Text-Channel header
            if (!ulong.TryParse(req.Headers.GetValues("Text-Channel").ToString(), out var channelId))
                channelId = 971047774311288983;

            // Return no content if no message was provided
            string message = await req.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(message))
                return req.CreateResponse(HttpStatusCode.NoContent);

            // Send message to channel
            var channel = await _restClientProvider.Client.GetChannelAsync(channelId) as RestTextChannel;
            await channel.SendMessageAsync(message);
            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}