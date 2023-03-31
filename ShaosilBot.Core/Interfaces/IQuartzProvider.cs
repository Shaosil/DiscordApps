using Discord.Rest;
using Discord.WebSocket;
using Quartz;

namespace ShaosilBot.Core.Interfaces
{
	public interface IQuartzProvider
	{
		void SetupPersistantJobs();
		void SelfDestructMessage(SocketMessage message, int hours);
		Dictionary<IJobDetail, ITrigger> GetUserReminders(ulong userID);
		bool DeleteUserReminder(JobKey key);
		void ScheduleUserReminder(ulong userID, ulong messageID, ulong channelID, DateTimeOffset targetDate, bool isPrivate, string msg, RestMessage? referenceMessage = null);
	}
}