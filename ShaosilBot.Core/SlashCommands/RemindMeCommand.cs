using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using Quartz;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Jobs;
using ShaosilBot.Core.Providers;
using System.Text.RegularExpressions;
using static ShaosilBot.Core.Providers.MessageCommandProvider;

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

		public override string HelpDetails => @$"/{CommandName} (in (string time-unit, int amount, bool private, string message) | (on (int day, string month, int year, string time, string timezone, bool private, string message)) | (list | delete (last | id (string id))))

SUBCOMMANDS:
* in (time-unit, amount, private, message)
    Schedules a reminder for [amount] [time-unit]s away. Can be private (DM) or public (channel).

* on (day, month, year, time, timezone, private, message)
	Schedules a reminder for the date/time specified. Month, year, and time default to current at midnight. Timezone defaults to EST.

* list
    Lists all of your own upcoming reminders.

* delete...
	- last
		Deletes your most recently scheduled reminder.
	- id
		Deletes a specified reminder from your own upcoming schedule. Use `/{CommandName} list` to see IDs.";

		public override SlashCommandProperties BuildCommand()
		{
			var months = new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
			var timezones = new List<string> { "Eastern", "Central", "Mountain", "Pacific" };

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
						Type = ApplicationCommandOptionType.SubCommand,
						Name = "on",
						Description = "Set a reminder for an exact date and time",
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder { Name = "day", Description = "What day of the month. Defaults to current.", Type = ApplicationCommandOptionType.Integer, MinValue = 1, MaxValue = 31 },
							new SlashCommandOptionBuilder
							{
								Name = "month",
								Description = "What month of the year. Defaults to current",
								Type = ApplicationCommandOptionType.Integer,
								Choices = months.Select((m, i) => new ApplicationCommandOptionChoiceProperties { Name = m, Value = i + 1 }).ToList()
							},
							new SlashCommandOptionBuilder { Name = "year", Description = "What year. Defaults to current", Type = ApplicationCommandOptionType.Integer, MinLength = 4, MaxLength = 4 },
							new SlashCommandOptionBuilder { Name = "time", Description = "HH[:mm] [AM/PM] formatted string. Supports 12 or 24 hour. Defaults to 12:00 AM", Type = ApplicationCommandOptionType.String, MaxLength = 8 },
							new SlashCommandOptionBuilder
							{
								Name = "timezone",
								Description = "Which time zone. Defaults to Eastern.",
								Type = ApplicationCommandOptionType.Integer,
								Choices = timezones.Select((t, i) => new ApplicationCommandOptionChoiceProperties { Name = t, Value = i }).ToList()
							},
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

		public override Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			// Handle lists and deletes in their own function for better organization
			var subCmd = cmdWrapper.Command.Data.Options.First();
			if (subCmd.Name == "list" || subCmd.Name == "delete") return Task.FromResult(cmdWrapper.Respond(ListOrDelete(cmdWrapper.Command.User.Id, subCmd), ephemeral: true));

			DateTimeOffset targetDateTime;
			bool isPrivate = (bool)(subCmd.Options.FirstOrDefault(o => o.Name == "private")?.Value ?? true);
			string msg = subCmd.Options.FirstOrDefault(o => o.Name == "message")?.Value?.ToString() ?? "ERROR: No Message";

			// In X time-units
			if (subCmd.Name == "in")
			{
				var timeUnit = Enum.Parse<eTimeUnits>($"{subCmd.Options.FirstOrDefault(o => o.Name == "time-unit")?.Value ?? ((int)eTimeUnits.Minutes)}");
				int amount = int.Parse($"{subCmd.Options.FirstOrDefault(o => o.Name == "amount")?.Value ?? 1}");

				targetDateTime = timeUnit == eTimeUnits.Seconds ? DateTimeOffset.Now.AddSeconds(amount)
					: timeUnit == eTimeUnits.Minutes ? DateTimeOffset.Now.AddMinutes(amount)
					: timeUnit == eTimeUnits.Hours ? DateTimeOffset.Now.AddHours(amount)
					: timeUnit == eTimeUnits.Days ? DateTimeOffset.Now.AddDays(amount)
					: timeUnit == eTimeUnits.Weeks ? DateTimeOffset.Now.AddDays(amount * 7)
					: DateTimeOffset.Now.AddMonths(amount);
			}
			// On exact date
			else
			{
				// Store options, or lack thereof and their defaults
				int day = int.Parse(subCmd.Options.FirstOrDefault(o => o.Name == "day")?.Value.ToString() ?? DateTime.Today.Day.ToString());
				int month = int.Parse(subCmd.Options.FirstOrDefault(o => o.Name == "month")?.Value.ToString() ?? DateTime.Today.ToString("MM"));
				int year = int.Parse(subCmd.Options.FirstOrDefault(o => o.Name == "year")?.Value.ToString() ?? DateTime.Today.Year.ToString());
				string time = (subCmd.Options.FirstOrDefault(o => o.Name == "time")?.Value.ToString() ?? "00:00").Replace(" ", "").ToUpper();
				int timezoneOffset = int.Parse(subCmd.Options.FirstOrDefault(o => o.Name == "timezone")?.Value.ToString() ?? "0");

				// Parse the time first and display an error if unable to parse
				var timeRegex = new Regex(@"(\d{1,2}):?(\d\d)?([AP]M)?").Match(time);
				int.TryParse(timeRegex.Groups.Count > 1 ? timeRegex.Groups[1].Value : "0", out var hour);
				int.TryParse(timeRegex.Groups.Count > 2 ? timeRegex.Groups[2].Value : "0", out var minute);
				if (timeRegex.Groups.Count > 3 && timeRegex.Groups[3].Value == "PM") hour += 12; // Always ensure 24 hour format
				if (hour > 23 || minute > 59)
				{
					return Task.FromResult(cmdWrapper.Respond($"The time you provided parsed to '{hour}:{minute}' (24 hour), which is invalid.", ephemeral: true));
				}

				// Convert all values to DateTimeOffset
				int estOffset = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time").GetUtcOffset(DateTime.UtcNow).Hours;
				string toParse = $"{month:00}/{day:00}/{year} {hour}:{minute} {estOffset - timezoneOffset}";
				if (!DateTimeOffset.TryParse(toParse, out targetDateTime))
				{
					return Task.FromResult(cmdWrapper.Respond($"The date and time you provided could not be parsed ({toParse}). Please input a valid time.", ephemeral: true));
				}
			}

			// Validate we are not in the past or too far out (a year and a day max)
			if (targetDateTime < DateTimeOffset.Now.AddSeconds(10)) return Task.FromResult(cmdWrapper.Respond("Target date is in the past. Please provide a future date.", ephemeral: true));
			else if ((targetDateTime - DateTimeOffset.Now).Days > 366) return Task.FromResult(cmdWrapper.Respond("Target date is too far in the future! Please keep it within a year.", ephemeral: true));

			_quartzProvider.ScheduleUserReminder(cmdWrapper.Command.User.Id, cmdWrapper.Command.Data.Id, cmdWrapper.Command.ChannelId!.Value, targetDateTime, isPrivate, msg);
			return Task.FromResult(cmdWrapper.Respond($"Successfully scheduled {(isPrivate ? "DM" : "public")} reminder for <t:{targetDateTime.ToUnixTimeSeconds()}>. See you then!", ephemeral: isPrivate));
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

		public string HandleRemindMeMessageCommand(RestMessageCommand command)
		{
			// Pass message ID along via component ID so we have a reference later
			var components = new ComponentBuilder()
				.WithButton("15 Minutes", $"{CommandNames.RemindMe}-{command.Data.Message.Id}-{15}")
				.WithButton("1 Day", $"{CommandNames.RemindMe}-{command.Data.Message.Id}-{60 * 24}")
				.WithButton("1 Week", $"{CommandNames.RemindMe}-{command.Data.Message.Id}-{60 * 24 * 7}")
				.WithButton("Custom Hours", $"{CommandNames.RemindMe}-{command.Data.Message.Id}-CustomHours", ButtonStyle.Secondary)
				.WithButton("Custom Days", $"{CommandNames.RemindMe}-{command.Data.Message.Id}-CustomDays", ButtonStyle.Secondary).Build();
			return command.Respond("Remind you when?", components: components, ephemeral: true);
		}

		public async Task<string> HandleReminderTimeButton(RestMessageComponent messageComponent)
		{
			// Parse referenced message ID to pass it along via component ids again if necessary
			string buttonId = messageComponent.Data.CustomId;
			ulong originalMessageId = ulong.Parse(Regex.Match(buttonId, ".+-(\\d+)-").Groups.Values.Last().Value);

			if (buttonId.Contains("-Custom"))
			{
				// Respond with a modal asking for X hours or days
				string desc = buttonId.EndsWith("Hours") ? "hours" : "days";
				var textInput = new TextInputBuilder("Amount", $"amount-{originalMessageId}-{desc}", value: $"{(desc == "hours" ? 1 : 3)}", minLength: 1, maxLength: 3, required: true);
				return messageComponent.RespondWithModal(new ModalBuilder($"How many {desc} do you want to schedule it?", CommandNames.Modals.CustomReminder).AddTextInput(textInput).Build());
			}
			else
			{
				// Schedule and respond
				int minutes = int.Parse(buttonId.Substring(buttonId.LastIndexOf('-') + 1));
				var targetDate = DateTimeOffset.Now.AddMinutes(minutes);
				return await ScheduleReminder(originalMessageId, messageComponent, targetDate);
			}
		}

		public async Task<string> HandleReminderTimeModal(RestModal modal)
		{
			string inputId = modal.Data.Components.First().CustomId;
			bool isHours = inputId.Contains("hours");
			string sanitizedInput = Regex.Replace(modal.Data.Components.First().Value, "[^\\d]", string.Empty);

			// Validation
			if (!int.TryParse(sanitizedInput, out var input)) return modal.Respond("Invalid input. Please use numeric values.", ephemeral: true);
			if (!isHours && input > 365) return modal.Respond("Invalid input. Please keep schedules within a year.", ephemeral: true);

			// Schedule and respond
			ulong originalMessageId = ulong.Parse(Regex.Match(inputId, ".+-(\\d+)-").Groups.Values.Last().Value);
			var targetDate = DateTimeOffset.Now.AddHours(isHours ? input : (input * 24));
			return await ScheduleReminder(originalMessageId, modal, targetDate);
		}

		private async Task<string> ScheduleReminder(ulong originalMessageId, RestInteraction interaction, DateTimeOffset targetDate)
		{
			var originalMessage = await interaction.Channel.GetMessageAsync(originalMessageId);
			string message = $"Reminder: {interaction.User.Mention} wanted to remember this message.";
			bool alreadyScheduled = _quartzProvider.GetUserReminders(interaction.User.Id).Any(r => r.Key.Key.Name.Contains($"{originalMessageId}"));

			// Schedule and respond
			_quartzProvider.ScheduleUserReminder(interaction.User.Id, originalMessageId, interaction.ChannelId!.Value, targetDate, false, message, originalMessage);
			return interaction.Respond($"Reminder {(alreadyScheduled ? "updated" : "scheduled")} successfully for <t:{targetDate.ToUnixTimeSeconds()}>", ephemeral: true);
		}
	}
}