using Discord;
using Discord.Rest;

namespace ShaosilBot.Core.Interfaces
{
	public interface IDiscordRestClientProvider
	{
		IUser BotUser { get; }
		IReadOnlyCollection<IGuild> Guilds { get; }

		Task Init();
		Task<IUser> GetUserAsync(ulong userID);
		Task<ITextChannel> GetChannelAsync(ulong channelID);
		Task<RestInteraction> ParseHttpInteractionAsync(string publicKey, string signature, string timestamp, string body);
		Task DMShaosil(string message);
	}
}