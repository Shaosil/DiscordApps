using Discord.Rest;
using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using System.Net.Mime;

namespace ShaosilBot.Web.Controllers
{
	public class InteractionsController : Controller
	{
		private readonly ILogger<InteractionsController> _logger;
		private readonly IConfiguration _configuration;
		private readonly ISlashCommandProvider _slashCommandProvider;
		private readonly IMessageCommandProvider _messageCommandProvider;
		private readonly SlashCommandWrapper _slashCommandWrapper;
		private readonly IDiscordRestClientProvider _restClientProvider;

		public InteractionsController(ILogger<InteractionsController> logger,
			IConfiguration configuration,
			ISlashCommandProvider slashCommandProvider,
			IMessageCommandProvider messageCommandProvider,
			SlashCommandWrapper slashCommandWrapper,
			IDiscordRestClientProvider restClientProvider)
		{
			_logger = logger;
			_configuration = configuration;
			_slashCommandWrapper = slashCommandWrapper;
			_restClientProvider = restClientProvider;
			_slashCommandProvider = slashCommandProvider;
			_messageCommandProvider = messageCommandProvider;
		}

		[HttpPost("/interactions")]
		public async Task<IActionResult> Interactions()
		{
			// Get signature headers and body, client will handle the rest
			Request.Headers.TryGetValue("X-Signature-Ed25519", out var signature);
			Request.Headers.TryGetValue("X-Signature-Timestamp", out var timestamp);
			string body = await new StreamReader(Request.Body).ReadToEndAsync();

			RestInteraction interaction;
			Response.ContentType = MediaTypeNames.Application.Json;
			try
			{
				_logger.LogInformation("Parsing new interaction");
				interaction = await _restClientProvider.ParseHttpInteractionAsync(_configuration["PublicKey"]!, signature!, timestamp!, body);
			}
			catch (Exception ex) when (ex is BadSignatureException || ex is ArgumentException)
			{
				// Thrown by the client when the signature is invalid
				return Unauthorized(ex.GetType().Name);
			}

			// Pass off to other handlers based on interaction type
			switch (interaction)
			{
				case RestPingInteraction ping:
					_logger.LogInformation("Acknowledging ping");
					return Content(ping.AcknowledgePing());

				case RestSlashCommand slash:
					_logger.LogInformation("Parsing slash command");
					var commandHandler = _slashCommandProvider.GetSlashCommandHandler(slash.Data.Name);
					if (commandHandler != null)
					{
						_slashCommandWrapper.SetSlashCommand(slash);
						_logger.LogInformation("Executing slash command");
						string slashCommandResult = await commandHandler.HandleCommand(_slashCommandWrapper);
						_logger.LogInformation($"Received slash command result - sending response:\n\t{slashCommandResult}");
						return Content(slashCommandResult);
					}

					return NotFound();

				case RestMessageCommand message:
					string messageCommandResult = _messageCommandProvider.HandleMessageCommand(message);
					return Content(messageCommandResult);

				case RestMessageComponent messageComponent:
					string messageComponentResult = await _messageCommandProvider.HandleMessageComponent(messageComponent);
					return Content(messageComponentResult);

				case RestModal modal:
					string modalResult = await _messageCommandProvider.HandleModel(modal);
					return Content(modalResult);

				default:
					return NotFound();
			}
		}
	}
}