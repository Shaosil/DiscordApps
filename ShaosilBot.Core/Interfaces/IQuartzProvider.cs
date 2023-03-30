using Discord.WebSocket;

namespace ShaosilBot.Core.Interfaces
{
	public interface IQuartzProvider
	{
		void SetupPersistantJobs();
		void SelfDestructMessage(SocketMessage message, int hours);
	}
}