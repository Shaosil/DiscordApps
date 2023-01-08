using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Web.CustomAuth;

namespace ShaosilBot.Web
{
	[ServiceFilter(typeof(UtilitiesAuthorizationAttribute))]
	public class UtilitiesController : Controller
    {
        private readonly ILogger<UtilitiesController> _logger;
		private readonly IDiscordSocketClientProvider _socketClientProvider;
		private readonly IDiscordRestClientProvider _restClientProvider;

		public UtilitiesController(ILogger<UtilitiesController> logger, IDiscordSocketClientProvider socketClientProvider, IDiscordRestClientProvider restClientProvider)
		{
			_logger = logger;
			_socketClientProvider = socketClientProvider;
			_restClientProvider = restClientProvider;
		}

		[HttpPost("/CleanupNoNoZone")]
		public void CleanupNoNoZone()
		{
			_socketClientProvider.CleanupNoNoZone();
		}

		[HttpPost("/SendText")]
		public async Task<IActionResult> SendText([FromForm] SendTextModel model)
		{
			// Default to the bot-test channel unless specified in Text-Channel header
			var channelId = model.Channel ?? 971047774311288983;

			// Return no content if no message was provided
			if (string.IsNullOrWhiteSpace(model.Message))
				return NoContent();

			// Send message to channel
			var channel = await _restClientProvider.GetChannelAsync(channelId);
			await channel.SendMessageAsync(model.Message);
			return Ok();
		}

		public class SendTextModel
		{
			public ulong? Channel { get; set; }
			public string Message { get; set; }
		}
	}
}