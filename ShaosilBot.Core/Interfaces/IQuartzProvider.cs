using Discord.Rest;
using Discord.WebSocket;
using Quartz;

namespace ShaosilBot.Core.Interfaces
{
	public interface IQuartzProvider
	{
		Task SetupPersistantJobs();
		void SelfDestructMessage(SocketMessage message, int hours);
		Dictionary<IJobDetail, ITrigger> GetUserReminders(ulong userID);
		bool DeleteUserReminder(JobKey key);
		Task ScheduleUserReminder(ulong userID, ulong commandID, ulong channelID, DateTimeOffset targetDate, bool isPrivate, string msg, RestMessage? referenceMessage = null);
	}
}