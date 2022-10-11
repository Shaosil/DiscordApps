using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using ShaosilBot.Interfaces;
using ShaosilBot.Models.Twitch;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShaosilBot
{
	public class TwitchCallback
    {
        private readonly ILogger _logger;
        private readonly ITwitchProvider _twitchProvider;

        public TwitchCallback(ILoggerFactory loggerFactory, ITwitchProvider twitchProvider)
        {
            _logger = loggerFactory.CreateLogger<TwitchCallback>();
            _twitchProvider = twitchProvider;
        }

        [Function("TwitchCallback")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            // Get message type header and body payload
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "text/plain");
            req.Headers.TryGetValues("Twitch-Eventsub-Message-Type", out var messageType);
            var payload = JsonSerializer.Deserialize<TwitchPayload>(req.ReadAsString());

            // Handle the various event types
            switch (messageType.FirstOrDefault()?.ToLower())
            {
                case "webhook_callback_verification":
                    // Just respond with challenge
                    _logger.LogInformation("Twitch webhook callback verification request received.");
                    response.WriteString(payload.challenge);
                    break;

                case "notification":
                    // Handle the event
                    await _twitchProvider.HandleNotification(payload);
                    break;

                case "revocation":
                    // Simply log it and return 200 as usual
                    _logger.LogWarning($"Twitch revocation occured! Reason: {payload.subscription.status}");
                    break;
            }

            return response;
        }
    }
}