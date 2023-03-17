using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;

namespace ShaosilBot.Core.Providers
{
	public class SlashCommandWrapper
	{
		private RestSlashCommand _slashCommand;
		private readonly ILogger<SlashCommandWrapper> _logger;

		public SlashCommandWrapper(ILogger<SlashCommandWrapper> logger)
		{
			_logger = logger;
		}

		public void SetSlashCommand(RestSlashCommand slash)
		{
			_slashCommand = slash;
		}

		public virtual IApplicationCommandInteractionData Data => _slashCommand.Data;

		public virtual IGuild Guild => _slashCommand.Guild;

		public virtual IRestMessageChannel Channel => _slashCommand.Channel;

		public virtual IUser User => _slashCommand.User;

		public virtual Task<string> DeferWithCode(Func<Task> code)
		{
			// Run the code asynchronously before returning a defer response to the client
			Task.Run(code).ConfigureAwait(false);
			return Task.FromResult(_slashCommand.Defer());
		}

		public virtual Task<RestFollowupMessage> FollowupAsync(string text = null, bool ephemeral = false, MessageComponent components = null, Embed embed = null)
		{
			return _slashCommand.FollowupAsync(text, ephemeral: ephemeral, components: components, embed: embed);
		}

		public virtual Task<RestFollowupMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string text = null)
		{
			return _slashCommand.FollowupWithFileAsync(fileStream, fileName, text);
		}

		public Task<RestInteractionMessage> GetOriginalResponseAsync()
		{
			return _slashCommand.GetOriginalResponseAsync();
		}

		public string Respond(string text = null, bool ephemeral = false, MessageComponent components = null, Embed embed = null)
		{
			return _slashCommand.Respond(text, ephemeral: ephemeral, components: components, embed: embed);
		}
	}
}