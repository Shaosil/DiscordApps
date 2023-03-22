using Discord.Rest;

namespace ShaosilBot.Core.Interfaces
{
	public interface IDiscordRestClientProvider
	{
		void Init();
		Task<RestTextChannel> GetChannelAsync(ulong channelId);
		Task<RestInteraction> ParseHttpInteractionAsync(string publicKey, string signature, string timestamp, string body);
	}
}