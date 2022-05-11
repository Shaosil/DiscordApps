using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Discord.Rest;
using Discord;
using System.Net.Mime;
using System.Text;
using ShaosilBot.SlashCommands;
using Discord.WebSocket;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Linq;
using System.Net.Http;
using System.Threading;

namespace ShaosilBot
{
    public class Interactions
    {
        private readonly ILogger<Interactions> _logger;
        private readonly HttpClient _httpClient;
        private readonly DiscordSocketClient _socketClient = null; // Basically only used for status updates
        private readonly DiscordRestClient _restClient = null; // Handles the actual HTTP requests

        public Interactions(ILogger<Interactions> logger, IHttpClientFactory httpClientFactory, DiscordSocketClient socketClient, DiscordRestClient restClient)
        {
            // Initialize bot and login
            _logger = logger;
            _httpClient = httpClientFactory.CreateClient();
            _socketClient = socketClient;
            _restClient = restClient;

            // Uncomment to refresh commands
            //_socketClient.Ready += async () => await SyncCommands();
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
                interaction = await _restClient.ParseHttpInteractionAsync(Environment.GetEnvironmentVariable("PublicKey"), signature, timestamp, body);
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
                    switch (slash.Data.Name)
                    {
                        case "test-command": response.WriteString(await new TestCommand(_logger).HandleCommandAsync(slash)); break;
                        case "wow": response.WriteString(await new WowCommand(_logger, _httpClient).HandleCommandAsync(slash)); break;
                        case "cat-fact": response.WriteString(await new CatFactsCommand(_logger, _httpClient).HandleCommandAsync(slash)); break;
                        default: response.StatusCode = System.Net.HttpStatusCode.NotFound; break;
                    }
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

        private async Task SyncCommands()
        {
            var guild = _socketClient.GetGuild(628019972316069890);
            await guild.DeleteApplicationCommandsAsync();
            await guild.CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "test-command", Description = "Getting closer to world domination", DefaultMemberPermissions = GuildPermission.Administrator }.Build());
            await guild.CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "wow", Description = "Wow." }.Build());
            await guild.CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "cat-fact", Description = "Thank you for subscribing to cat facts! Text STOP to unsubscribe." }.Build());
        }
    }
}