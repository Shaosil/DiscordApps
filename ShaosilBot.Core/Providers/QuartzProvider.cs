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

		public QuartzProvider(ISchedulerFactory schedulerFactory, IConfiguration configuration)
		{
			_scheduler = schedulerFactory.GetScheduler().Result;
			_configuration = configuration;
		}

		public static void EnsureSchemaExists(string connString)
		{
			using (var conn = new SqliteConnection(connString))
			{
				conn.Open();
				var cmd = conn.CreateCommand();

				// Check for the existance of any tables
				cmd.CommandText = "SELECT COUNT(*) FROM sqlite_schema";
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
			if (_isDevelopment) return;

			var key = new JobKey($"DeleteMessage-{message.Id}");
			var dataMap = new JobDataMap(new Dictionary<string, string>
			{
				{ SelfDestructMessageJob.DataMapKeys.ChannelID, message.Channel.Id.ToString() },
				{ SelfDestructMessageJob.DataMapKeys.MessageID, message.Id.ToString() }
			});
			var job = JobBuilder.Create<SelfDestructMessageJob>().WithIdentity(key).StoreDurably(false).UsingJobData(dataMap).Build();
			var trigger = TriggerBuilder.Create().StartAt(DateTime.Now.AddHours(hours)).Build();

			_scheduler.ScheduleJob(job, trigger);
		}
	}
}