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
		private readonly SlashCommandWrapper _slashCommandWrapper;
		private readonly IDiscordRestClientProvider _restClientProvider;

		public InteractionsController(ILogger<InteractionsController> logger,
			IConfiguration configuration,
			ISlashCommandProvider slashCommandProvider,
			SlashCommandWrapper slashCommandWrapper,
			IDiscordRestClientProvider restClientProvider)
		{
			_logger = logger;
			_configuration = configuration;
			_slashCommandWrapper = slashCommandWrapper;
			_restClientProvider = restClientProvider;
			_slashCommandProvider = slashCommandProvider;
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
					return Content(ping.AcknowledgePing());

				case RestSlashCommand slash:
					var commandHandler = _slashCommandProvider.GetSlashCommandHandler(slash.Data.Name);
					if (commandHandler != null)
					{
						_slashCommandWrapper.SetSlashCommand(slash);
						return Content(await commandHandler.HandleCommand(_slashCommandWrapper));
					}

					return NotFound();

				default:
					return NotFound();
			}
		}
	}
}