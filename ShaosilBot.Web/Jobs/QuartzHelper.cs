using Microsoft.Data.Sqlite;
using Quartz;
using ShaosilBot.Web.Jobs;

public static class QuartzHelper
{
	public const string FillMonthlyChatGPTTokensJobIdentity = "FillMonthlyChatGPTTokens";

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

	public static void SetupPersistantJobs(IScheduler scheduler, IConfiguration configuration)
	{
		// Once a month ChatGPT token reset
		var chatGPTTokensKey = new JobKey(FillMonthlyChatGPTTokensJobIdentity);
		if (configuration.GetValue<bool>("ChatGPTEnabled"))
		{
			var job = JobBuilder.Create<FillMonthlyChatGPTTokensJob>().WithIdentity(chatGPTTokensKey).Build();
			var trigger = TriggerBuilder.Create().WithIdentity(FillMonthlyChatGPTTokensJobIdentity).WithCronSchedule("0 0 0 1 * ? *", s => s.WithMisfireHandlingInstructionFireAndProceed()).Build();

			scheduler.ScheduleJob(job, trigger);
		}
		else
		{
			scheduler.DeleteJob(chatGPTTokensKey).Wait();
		}
	}
}