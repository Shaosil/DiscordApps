using Discord.Rest;
using System.Threading.Tasks;

namespace ShaosilBot.Interfaces
{
    public interface IDiscordRestClientProvider
    {
        Task<RestTextChannel> GetChannelAsync(ulong channelId);
        Task<RestInteraction> ParseHttpInteractionAsync(string publicKey, string signature, string timestamp, string body);
    }
}