using Discord;
using Microsoft.AspNetCore.Mvc;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Web.CustomAuth;

namespace ShaosilBot.Web
{
	[ServiceFilter(typeof(UtilitiesAuthorizationAttribute))]
	public class UtilitiesController : Controller
	{
		private readonly ILogger<UtilitiesController> _logger;
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly IChatGPTProvider _chatGPTProvider;

		public UtilitiesController(ILogger<UtilitiesController> logger, IDiscordRestClientProvider restClientProvider, IChatGPTProvider chatGPTProvider)
		{
			_logger = logger;
			_restClientProvider = restClientProvider;
			_chatGPTProvider = chatGPTProvider;
		}

		public record SendTextModel(ulong? Channel, string Message);
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

		public record SendChatModel(ulong? Channel, string prompt);
		[HttpPost("/SendChat")]
		public async Task<IActionResult> SendChat([FromForm] SendChatModel model)
		{
			// Default to the bot-chat channel unless specified in Text-Channel header
			var channelId = model.Channel ?? 1085277525283975318;

			// Return no content if no message was provided
			if (string.IsNullOrWhiteSpace(model.prompt))
				return NoContent();

			// Send prompt
			var channel = await _restClientProvider.GetChannelAsync(channelId) as ITextChannel;
			await _chatGPTProvider.SendChatMessage(channel!, model.prompt);
			return Ok();
		}
	}
}