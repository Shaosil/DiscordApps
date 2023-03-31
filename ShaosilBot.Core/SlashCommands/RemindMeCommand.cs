using Discord;
using Microsoft.Extensions.Logging;
using Quartz;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Jobs;
using ShaosilBot.Core.Providers;

namespace ShaosilBot.Core.SlashCommands
{
	public class RemindMeCommand : BaseCommand
	{
		private readonly IQuartzProvider _quartzProvider;

		private enum eTimeUnits { Seconds, Minutes, Hours, Days, Weeks, Months }

		public RemindMeCommand(ILogger<BaseCommand> logger, IQuartzProvider quartzProvider) : base(logger)
		{
			_quartzProvider = quartzProvider;
		}

		public override string CommandName => "remind-me";

		public override string HelpSummary => "Schedules a public or private reminder that will trigger at the specified time.";

		public override string HelpDetails => @$"/{CommandName} (in (string time-unit, int amount, bool private, string message) | (list | delete (last | id (string id))))

SUBCOMMANDS:
* in (time-unit, amount, private, message)
    Schedules a reminder for [amount] [time-unit]s away. Can be private (DM) or public (channel).

* list
    Lists all of your own upcoming reminders

* delete...
	- last
		Deletes your most recently scheduled reminder.
	- id
		Deletes a specified reminder from your own upcoming schedule. Use `/{CommandName} list` to see IDs.";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new List<SlashCommandOptionBuilder>
				{
					new SlashCommandOptionBuilder
					{
						Type = ApplicationCommandOptionType.SubCommand,
						Name = "in",
						Description = "Set a reminder for X time units away",
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder
							{
								Name = "time-unit",
								Description = "The type of time duration",
								IsRequired = true,
								Choices = Enum.GetValues<eTimeUnits>().Select(c => new ApplicationCommandOptionChoiceProperties { Name = c.ToString(), Value = (int)c }).ToList(),
								Type = ApplicationCommandOptionType.Integer
							},
							new SlashCommandOptionBuilder { Name = "amount", Description = "How many of 'time-unit' to use", IsRequired = true, Type = ApplicationCommandOptionType.Integer, MinValue = 1 },
							new SlashCommandOptionBuilder { Name = "private", Description = "Whether to DM you or this channel", IsRequired = true, Type = ApplicationCommandOptionType.Boolean },
							new SlashCommandOptionBuilder { Name = "message", Description = "The reminder message", IsRequired = true, Type = ApplicationCommandOptionType.String }
						}
					},
					new SlashCommandOptionBuilder
					{
						Name = "list",
						Description = "See your own upcoming reminders",
						Type = ApplicationCommandOptionType.SubCommand
					},
					new SlashCommandOptionBuilder
					{
						Name = "delete",
						Description = "Delete your own reminders",
						Type = ApplicationCommandOptionType.SubCommandGroup,
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder { Name = "last", Description = "Delete your most recently scheduled reminder", Type = ApplicationCommandOptionType.SubCommand },
							new SlashCommandOptionBuilder
							{
								Name = "id",
								Description = "Delete one of your reminders by ID (see manage -> list)",
								Type = ApplicationCommandOptionType.SubCommand,
								Options = new List<SlashCommandOptionBuilder>
								{
									new SlashCommandOptionBuilder { Name = "id", Description = "The ID of the reminder", IsRequired = true, Type = ApplicationCommandOptionType.String  }
								}
							}
						}
					},
				}
			}.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper command)
		{
			// Handle lists and deletes in their own function for better organization
			var subCmd = command.Data.Options.First();
			if (subCmd.Name == "list" || subCmd.Name == "delete") return Task.FromResult(command.Respond(ListOrDelete(command.User.Id, subCmd), ephemeral: true));

			// In X time-units
			bool isPrivate = (bool)(subCmd.Options.FirstOrDefault(o => o.Name == "private")?.Value ?? true);
			string msg = subCmd.Options.FirstOrDefault(o => o.Name == "message")?.Value?.ToString() ?? "ERROR: No Message";
			var timeUnit = Enum.Parse<eTimeUnits>($"{subCmd.Options.FirstOrDefault(o => o.Name == "time-unit")?.Value ?? ((int)eTimeUnits.Minutes)}");
			int amount = int.Parse($"{subCmd.Options.FirstOrDefault(o => o.Name == "amount")?.Value ?? 1}");

			// Validate we are not too far out (a year and a day max)
			var targetDate = timeUnit == eTimeUnits.Seconds ? DateTimeOffset.Now.AddSeconds(amount)
				: timeUnit == eTimeUnits.Minutes ? DateTimeOffset.Now.AddMinutes(amount)
				: timeUnit == eTimeUnits.Hours ? DateTimeOffset.Now.AddHours(amount)
				: timeUnit == eTimeUnits.Days ? DateTimeOffset.Now.AddDays(amount)
				: timeUnit == eTimeUnits.Weeks ? DateTimeOffset.Now.AddDays(amount * 7)
				: DateTimeOffset.Now.AddMonths(amount);

			if ((targetDate - DateTimeOffset.Now).Days > 366) return Task.FromResult(command.Respond("Target date is too far in the future! Please keep it within a year.", ephemeral: true));

			_quartzProvider.ScheduleUserReminder(command.User.Id, command.Data.Id, command.Channel.Id, targetDate, isPrivate, msg);
			return Task.FromResult(command.Respond($"Successfully scheduled {(isPrivate ? "DM" : "public")} reminder for <t:{targetDate.ToUnixTimeSeconds()}>. See you then!", ephemeral: isPrivate));
		}

		private string ListOrDelete(ulong userID, IApplicationCommandInteractionDataOption cmd)
		{
			// Load all user reminders in any case
			var allUserReminders = _quartzProvider.GetUserReminders(userID);

			if (cmd.Name == "list")
			{
				// Get all user reminders
				if (allUserReminders.Count == 0) return "You currently have no reminders scheduled. Schedule one today at a local Discord server near you!";

				return string.Join("\n\n", allUserReminders.Select(r => $"* [{r.Key.Key.Name.Replace("Reminder-", string.Empty)}] - Scheduled for <t:{r.Value.GetNextFireTimeUtc()!.Value.ToUnixTimeSeconds()}>. Message: {r.Key.JobDataMap[ReminderJob.DataMapKeys.Message]}"));
			}
			else
			{
				// Which type of delete?
				var subCmd = cmd.Options.First();
				if (subCmd.Name == "last")
				{
					// Remove last reminder if one exists
					var lastReminder = allUserReminders.LastOrDefault().Key;
					if (lastReminder == null) return "You currently have no reminders scheduled. Schedule one today at a local Discord server near you!";
					bool result = _quartzProvider.DeleteUserReminder(lastReminder.Key);

					if (result) return "Successfully deleted last scheduled reminder.";
					else return "Could not delete last scheduled reminder. Ask Shaosil to check the logs.";
				}
				else
				{
					// Remove specified reminder
					string? id = subCmd.Options.FirstOrDefault(c => c.Name == "id")?.Value?.ToString()?.Trim();
					if (string.IsNullOrWhiteSpace(id)) return "Invalid ID. Please provide the ID of the job you wish to delete.";

					bool result = _quartzProvider.DeleteUserReminder(new JobKey(id));
					if (result) return $"Successfully deleted job ID {id}.";
					else return $"Could not delete job with ID of '{id}'. Are you sure the ID is correct? Use `/{CommandName} list` to see your IDs.";
				}
			}
		}
	}
}