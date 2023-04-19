using Discord.Rest;
using Discord.WebSocket;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Quartz;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Jobs;

namespace ShaosilBot.Core.Providers
{
	public class QuartzProvider : IQuartzProvider
	{
		public const string FillMonthlyChatGPTTokensJobIdentity = "FillMonthlyChatGPTTokens";

		private bool _isDevelopment = true;
		private readonly IScheduler _scheduler;
		private readonly IConfiguration _configuration;
		private readonly string _connectionString;

		public QuartzProvider(ISchedulerFactory schedulerFactory, IConfiguration configuration)
		{
			_scheduler = schedulerFactory.GetScheduler().Result;
			_configuration = configuration;
			_connectionString = SQLiteProvider.ConnectionString;

			// Call once on first instantiation (we should be a singleton)
			EnsureSchemaExists();
		}

		private void EnsureSchemaExists()
		{
			using (var conn = new SqliteConnection(_connectionString))
			{
				conn.Open();
				var cmd = conn.CreateCommand();

				// Check for the existance of any quartz tables
				cmd.CommandText = "SELECT COUNT(*) FROM sqlite_schema WHERE type = 'table' AND name LIKE 'QRTZ_%'";
				long results = (long)cmd.ExecuteScalar()!;

				// If nothing exists, run the initialization script
				if (results < 1)
				{
					cmd.CommandText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Files/quartz-tables.sql"));
					cmd.ExecuteNonQuery();
				}
			}
		}

		public void SetupPersistantJobs()
		{
			_scheduler.Start().Wait();

			// Once a month ChatGPT token reset
			var chatGPTTokensKey = new JobKey(FillMonthlyChatGPTTokensJobIdentity);
			if (_configuration.GetValue<bool>("ChatGPTEnabled"))
			{
				var job = JobBuilder.Create<FillMonthlyChatGPTTokensJob>().WithIdentity(chatGPTTokensKey).Build();
				var trigger = TriggerBuilder.Create().WithIdentity(FillMonthlyChatGPTTokensJobIdentity).WithCronSchedule("0 0 0 1 * ? *", s => s.WithMisfireHandlingInstructionFireAndProceed()).Build();

				_scheduler.ScheduleJob(job, trigger);
			}
			else
			{
				_scheduler.DeleteJob(chatGPTTokensKey).Wait();
			}

			// If we are here, we are NOT in development
			_isDevelopment = false;
		}

		public void SelfDestructMessage(SocketMessage message, int hours)
		{
			var key = new JobKey($"DeleteMessage-{message.Id}");
			var dataMap = new JobDataMap(new Dictionary<string, string>
			{
				{ SelfDestructMessageJob.DataMapKeys.ChannelID, message.Channel.Id.ToString() },
				{ SelfDestructMessageJob.DataMapKeys.MessageID, message.Id.ToString() }
			});
			var job = JobBuilder.Create<SelfDestructMessageJob>().WithIdentity(key).UsingJobData(dataMap).Build();
			var trigger = TriggerBuilder.Create().StartAt(DateTime.Now.AddHours(hours)).Build();

			_scheduler.ScheduleJob(job, trigger);
		}

		public Dictionary<IJobDetail, ITrigger> GetUserReminders(ulong userID)
		{
			List<string> jobKeys = new List<string>();

			// Manually query the DB by job name and data to retrieve keys
			using (var conn = new SqliteConnection(_connectionString))
			{
				conn.Open();
				var cmd = conn.CreateCommand();
				cmd.CommandText = $"SELECT JOB_NAME FROM QRTZ_JOB_DETAILS WHERE JOB_NAME LIKE 'Reminder-%' AND JOB_DATA LIKE '%\"{ReminderJob.DataMapKeys.UserID}\":\"{userID}\"%'";
				var reader = cmd.ExecuteReader();
				while (reader.Read()) jobKeys.Add(reader.GetString(0));
			}

			// Retrieve job details by found keys
			var userJobs = new Dictionary<IJobDetail, ITrigger>();
			foreach (var jobKey in jobKeys.Select(k => new JobKey(k)))
			{
				var job = _scheduler.GetJobDetail(jobKey).Result!;
				var trigger = _scheduler.GetTriggersOfJob(jobKey).Result.First();
				userJobs.Add(job, trigger);
			}

			return userJobs;
		}

		public bool DeleteUserReminder(JobKey key)
		{
			// Do not do this in develop
			if (_isDevelopment) return true;

			return _scheduler.DeleteJob(key).Result;
		}

		public void ScheduleUserReminder(ulong userID, ulong commandID, ulong channelID, DateTimeOffset targetDate, bool isPrivate, string msg, RestMessage? referenceMessage = null)
		{
			// Do not do this in develop
			if (_isDevelopment) return;

			var key = new JobKey($"Reminder-{commandID}");
			var dataMap = new JobDataMap(new Dictionary<string, string>
			{
				{ ReminderJob.DataMapKeys.UserID, userID.ToString() },
				{ ReminderJob.DataMapKeys.Message, msg }
			});
			if (!isPrivate)
			{
				// Omit channel if this is a private reminder
				dataMap.Add(ReminderJob.DataMapKeys.ChannelID, channelID.ToString());
			}
			if (referenceMessage != null)
			{
				// Link to original message if one was included (via a Message Command)
				dataMap.Add(ReminderJob.DataMapKeys.ReferenceMessageID, referenceMessage.Id.ToString());
				dataMap.Add(ReminderJob.DataMapKeys.ReferenceMessageContent, referenceMessage.Content);
			}

			var job = JobBuilder.Create<ReminderJob>().WithIdentity(key).UsingJobData(dataMap).Build();
			var trigger = TriggerBuilder.Create().WithIdentity(key.Name).StartAt(targetDate).Build();

			// Upsert
			_scheduler.ScheduleJob(job, new[] { trigger }, true);
		}

		public void SchedulePollEnd(ulong channelID, ulong responseMessageID, DateTimeOffset targetTime)
		{
			// Do not do this in develop
			if (_isDevelopment) return;

			var key = new JobKey($"PollEnd-{responseMessageID}");
			var dataMap = new JobDataMap(new Dictionary<string, string>
			{
				{ PollEndJob.DataMapKeys.ChannelID, $"{channelID}" },
				{ PollEndJob.DataMapKeys.MessageID, $"{responseMessageID}" }
			});

			var job = JobBuilder.Create<PollEndJob>().WithIdentity(key).UsingJobData(dataMap).Build();
			var trigger = TriggerBuilder.Create().WithIdentity(key.Name).StartAt(targetTime).Build();

			_scheduler.ScheduleJob(job, trigger);
		}
	}
}