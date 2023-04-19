using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;

namespace ShaosilBot.Core.Providers
{
	public class SlashCommandWrapper
	{
		private RestSlashCommand _slashCommand;
		private readonly ILogger<SlashCommandWrapper> _logger;

		public virtual ISlashCommandInteraction Command => _slashCommand;

		public SlashCommandWrapper(ILogger<SlashCommandWrapper> logger)
		{
			_logger = logger;
		}

		public void SetSlashCommand(RestSlashCommand slash)
		{
			_slashCommand = slash;
		}

		public virtual Task<string> DeferWithCode(Func<Task> code, bool ephermal = false)
		{
			// Run the code asynchronously before returning a defer response to the client
			Task.Factory.StartNew(() =>
			{
				try
				{
					code().GetAwaiter().GetResult();
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, $"Error in {nameof(DeferWithCode)}");
				}
			}, TaskCreationOptions.LongRunning).ConfigureAwait(false);
			return Task.FromResult(_slashCommand.Defer(ephermal));
		}

		public string Respond(string? text = null, bool ephemeral = false, MessageComponent? components = null, Embed? embed = null)
		{
			return _slashCommand.Respond(text, ephemeral: ephemeral, components: components, embed: embed);
		}
	}
}