using Discord;
using Discord.Rest;
using System.IO;
using System.Threading.Tasks;

namespace ShaosilBot.Providers
{
	public class SlashCommandWrapper
	{
		private RestSlashCommand _slashCommand;

		public void SetSlashCommand(RestSlashCommand slash)
		{
			_slashCommand = slash;
		}

		public IApplicationCommandInteractionData Data => _slashCommand.Data;

		public virtual IGuild Guild => _slashCommand.Guild;

		public virtual IRestMessageChannel Channel => _slashCommand.Channel;

		public virtual IUser User => _slashCommand.User;

		public string Defer(bool ephemeral = false)
		{
			return _slashCommand.Defer(ephemeral: ephemeral);
		}

		public Task<RestFollowupMessage> FollowupAsync(string text = null, bool ephemeral = false, MessageComponent components = null, Embed embed = null)
		{
			return _slashCommand.FollowupAsync(text, ephemeral: ephemeral, components: components, embed: embed);
		}

		public Task<RestFollowupMessage> FollowupWithFileAsync(Stream fileStream, string fileName, string text = null)
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