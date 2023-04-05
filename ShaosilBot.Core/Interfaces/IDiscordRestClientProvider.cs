using Discord;
using Discord.Rest;

namespace ShaosilBot.Core.Interfaces
{
	public interface IDiscordRestClientProvider
	{
		DiscordRestClient Client { get; }
		IReadOnlyCollection<IGuild> Guilds { get; }

		Task Init();
		Task<RestTextChannel> GetChannelAsync(ulong channelId);
		Task<RestInteraction> ParseHttpInteractionAsync(string publicKey, string signature, string timestamp, string body);
		Task DMShaosil(string message);
	}
}