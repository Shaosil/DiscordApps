using Discord;
using Discord.Rest;

namespace ShaosilBot.Core.Providers
{
	public class SlashCommandWrapper
	{
		private RestSlashCommand _slashCommand;
		public virtual ISlashCommandInteraction Command => _slashCommand;

		public void SetSlashCommand(RestSlashCommand slash)
		{
			_slashCommand = slash;
		}

		public virtual Task<string> DeferWithCode(Func<Task> code)
		{
			// Run the code asynchronously before returning a defer response to the client
			Task.Factory.StartNew(code, TaskCreationOptions.LongRunning).ConfigureAwait(false);
			return Task.FromResult(_slashCommand.Defer());
		}

		public string Respond(string? text = null, bool ephemeral = false, MessageComponent? components = null, Embed? embed = null)
		{
			return _slashCommand.Respond(text, ephemeral: ephemeral, components: components, embed: embed);
		}
	}
}