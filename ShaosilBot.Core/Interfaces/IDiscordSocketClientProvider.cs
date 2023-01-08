using Discord.WebSocket;

namespace ShaosilBot.Core.Interfaces
{
	public interface IDiscordSocketClientProvider
	{
		DiscordSocketClient Client { get; }

		void CleanupNoNoZone();
		void Init();
	}
}