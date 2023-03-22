namespace ShaosilBot.Core.Interfaces
{
	public interface IDiscordSocketClientProvider
	{
		void CleanupNoNoZone();
		void Init(bool isDevelopment);
	}
}