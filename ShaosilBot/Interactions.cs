using Discord.Rest;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;

namespace ShaosilBot
{
	public class Interactions
    {
        private readonly ILogger<Interactions> _logger;
        private readonly ISlashCommandProvider _slashCommandProvider;
		private readonly SlashCommandWrapper _slashCommandWrapper;

        // The following are singletons and unused but leaving them here ensures DI will keep them around
        private readonly IDiscordSocketClientProvider _socketClientProvider;
        private readonly IDiscordRestClientProvider _restClientProvider;

		public Interactions(ILogger<Interactions> logger,
			ISlashCommandProvider slashCommandProvider,
			SlashCommandWrapper slashCommandWrapper,
			IDiscordSocketClientProvider socketClientProvider,
			IDiscordRestClientProvider restClientProvider)
		{
			_logger = logger;

			_socketClientProvider = socketClientProvider;
			_slashCommandWrapper = slashCommandWrapper;
			_restClientProvider = restClientProvider;
			_slashCommandProvider = slashCommandProvider;

			_socketClientProvider.KeepAlive();
		}

		[Function("interactions")]
        public async Task<HttpResponseData> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            //LogHttpRequest(req);
            _logger.LogInformation("C# HTTP trigger function processed a request.");

            // Get signature headers and body, client will handle the rest
            string signature = req.Headers.Contains("X-Signature-Ed25519") ? req.Headers.GetValues("X-Signature-Ed25519").First() : string.Empty;
            string timestamp = req.Headers.Contains("X-Signature-Timestamp") ? req.Headers.GetValues("X-Signature-Timestamp").First() : string.Empty;
            string body = req.ReadAsString() ?? string.Empty;

            RestInteraction interaction = null;
            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            response.Headers.Add("Content-Type", MediaTypeNames.Application.Json);
            try
            {
                interaction = await _restClientProvider.ParseHttpInteractionAsync(Environment.GetEnvironmentVariable("PublicKey"), signature, timestamp, body);
            }
            catch (Exception ex) when (ex is BadSignatureException || ex is ArgumentException)
            {
                // Thrown by the client when the signature is invalid
                response.StatusCode = System.Net.HttpStatusCode.Unauthorized;
                response.WriteString(ex.GetType().Name);
                return response;
            }

            // Pass off to other handlers based on interaction type
            switch (interaction)
            {
                case RestPingInteraction ping:
                    await response.WriteStringAsync(ping.AcknowledgePing());
                    break;

                case RestSlashCommand slash:
                    var commandHandler = _slashCommandProvider.GetSlashCommandHandler(slash.Data.Name);
                    if (commandHandler != null)
					{
						_slashCommandWrapper.SetSlashCommand(slash);
						response.WriteString(await commandHandler.HandleCommandAsync(_slashCommandWrapper));
					}
                    else
                        response.StatusCode = System.Net.HttpStatusCode.NotFound;
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