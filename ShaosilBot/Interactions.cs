using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Discord.Rest;
using System.Net.Mime;
using System.Text;
using ShaosilBot.SlashCommands;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Linq;
using System.Net.Http;
using Discord.WebSocket;
using ShaosilBot.Singletons;
using ShaosilBot.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace ShaosilBot
{
    public class Interactions
    {
        private readonly ILogger<Interactions> _logger;
        private readonly HttpClient _httpClient;
        private readonly CatFactsProvider _catFactsProvider;
        private TwitchProvider _twitchProvider;

        // The following are singletons and unused but leaving them here ensures DI will keep them around
        private readonly DataBlobProvider _blobClient;
        private readonly DiscordSocketClientProvider _socketClientProvider;
        private readonly DiscordRestClientProvider _restClientProvider;

        public Interactions(ILogger<Interactions> logger,
            IHttpClientFactory httpClientFactory,
            CatFactsProvider catFactsProvider,
            TwitchProvider twitchProvider,
            DataBlobProvider blobProvider,
            DiscordSocketClientProvider socketClientProvider,
            DiscordRestClientProvider restClientProvider)
        {
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _catFactsProvider = catFactsProvider;

            _blobClient = blobProvider;
            _socketClientProvider = socketClientProvider;
            _restClientProvider = restClientProvider;
            _twitchProvider = twitchProvider;
        }

        [Function("interactions")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            //LogHttpRequest(req);
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            // Get signature headers and body, client will handle the rest
            string signature = req.Headers.Contains("X-Signature-Ed25519") ? req.Headers.GetValues("X-Signature-Ed25519").First() : string.Empty;
            string timestamp = req.Headers.Contains("X-Signature-Timestamp") ? req.Headers.GetValues("X-Signature-Timestamp").First() : string.Empty;
            string body = req.ReadAsString();

            RestInteraction interaction = null;
            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            response.Headers.Add("Content-Type", MediaTypeNames.Application.Json);
            try
            {
                interaction = await _restClientProvider.Client.ParseHttpInteractionAsync(Environment.GetEnvironmentVariable("PublicKey"), signature, timestamp, body);
            }
            catch (Exception ex) when (ex is BadSignatureException || ex is ArgumentException)
            {
                // Thrown by the client when the signature is invalid
                response.StatusCode = System.Net.HttpStatusCode.Unauthorized;
                return response;
            }

            // Pass off to other handlers based on interaction type
            switch (interaction)
            {
                case RestPingInteraction ping:
                    await response.WriteStringAsync(ping.AcknowledgePing());
                    break;

                case RestSlashCommand slash:
                    BaseCommand command;

                    // Todo: Avoid the service locator pattern, but find a way to avoid passing DI parameters manually...
                    switch (slash.Data.Name)
                    {
                        case "test-command": command = new TestCommand(_logger); break;
                        case "wow": command = new WowCommand(_logger, _httpClient); break;
                        case "cat-fact": command = new CatFactsCommand(_logger, _catFactsProvider); break;
                        case "xkcd": command = new XkcdCommand(_logger, _httpClient); break;
                        case "git-blame": command = new GitBlameCommand(_logger, _httpClient, _blobClient); break;
                        case "random": command = new RandomCommand(_logger); break;
                        case "magic8ball": command = new Magic8BallCommand(_logger); break;
                        case "whackabot": command = new WhackabotCommand(_logger, _blobClient); break;
                        case "twitch": command = new TwitchCommand(_logger, _twitchProvider); break;
                        default: response.StatusCode = System.Net.HttpStatusCode.NotFound; return response;
                    }
                    response.WriteString(await command.HandleCommandAsync(slash));
                    break;

                default:
                    response.StatusCode = System.Net.HttpStatusCode.NotFound;
                    break;
            }

            return response;
        }
        
        private void LogHttpRequest(HttpRequestData request)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"URL: {request.Url}");
            sb.AppendLine("HEADERS:");
            foreach (var header in request.Headers)
                sb.AppendLine($"\t{header.Key}: {header.Value}");
            sb.AppendLine("BODY:");
            sb.AppendLine(new StreamReader(request.Body).ReadToEnd());
            request.Body.Position = 0;

            _logger.LogWarning("New Request");
            _logger.LogInformation(sb.ToString());
        }
    }
}