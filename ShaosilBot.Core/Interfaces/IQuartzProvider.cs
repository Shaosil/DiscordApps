using Discord.Rest;
using Discord.WebSocket;
using Quartz;

namespace ShaosilBot.Core.Interfaces
{
	public interface IQuartzProvider
	{
		string ConnectionString { get; }

		void SetupPersistantJobs();
		void SelfDestructMessage(SocketMessage message, int hours);
		Dictionary<IJobDetail, ITrigger> GetUserReminders(ulong userID);
		bool DeleteUserReminder(JobKey key);
		void ScheduleUserReminder(ulong userID, ulong commandID, ulong channelID, DateTimeOffset targetDate, bool isPrivate, string msg, RestMessage? referenceMessage = null);
		void SchedulePollEnd(ulong channelID, ulong responseMessageID, DateTimeOffset targetTime);
	}
}