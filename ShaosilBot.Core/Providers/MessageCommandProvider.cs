using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using System.Reflection;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.Providers
{
	public class MessageCommandProvider : IMessageCommandProvider
	{
		private readonly ILogger<SlashCommandProvider> _logger;
		private readonly IQuartzProvider _quartzProvider;
		private readonly IDiscordRestClientProvider _restClientProvider;

		public class MessageCommandNames
		{
			public const string RemindMe = "Remind Me!";
		}

		public MessageCommandProvider(ILogger<SlashCommandProvider> logger, IQuartzProvider quartzProvider, IDiscordRestClientProvider restClientProvider)
		{
			_logger = logger;
			_quartzProvider = quartzProvider;
			_restClientProvider = restClientProvider;
		}

		public async Task BuildMessageCommands()
		{
			var allMessageNames = typeof(MessageCommandNames).GetFields(BindingFlags.Static | BindingFlags.Public).Select(f => f.GetValue(null)!.ToString()!).ToList();

			var guilds = _restClientProvider.Guilds;

			foreach (var guild in guilds)
			{
				// Create message commands
				var messageCommands = (await guild.GetApplicationCommandsAsync()).Where(c => c.Type == ApplicationCommandType.Message).ToList();

				// Remove ones that no longer exist
				foreach (var msgCommand in messageCommands.Where(c => !allMessageNames.Contains(c.Name)))
				{
					await msgCommand.DeleteAsync();
				}

				// Create ones that are new
				foreach (string newMsgCommandName in allMessageNames.Where(n => !messageCommands.Any(c => c.Name == n)))
				{
					await guild.CreateApplicationCommandAsync(new MessageCommandBuilder { Name = newMsgCommandName }.Build());
				}
			}
		}

		public string HandleMessageCommand(RestMessageCommand command)
		{
			switch (command.Data.Name)
			{
				case MessageCommandNames.RemindMe:
					// Pass message ID along via component ID so we have a reference later
					var components = new ComponentBuilder()
						.WithButton("15 Minutes", $"{MessageCommandNames.RemindMe}-{command.Data.Message.Id}-{15}")
						.WithButton("1 Day", $"{MessageCommandNames.RemindMe}-{command.Data.Message.Id}-{60 * 24}")
						.WithButton("1 Week", $"{MessageCommandNames.RemindMe}-{command.Data.Message.Id}-{60 * 24 * 7}")
						.WithButton("Custom Hours", $"{MessageCommandNames.RemindMe}-{command.Data.Message.Id}-CustomHours", ButtonStyle.Secondary)
						.WithButton("Custom Days", $"{MessageCommandNames.RemindMe}-{command.Data.Message.Id}-CustomDays", ButtonStyle.Secondary).Build();
					return command.Respond("Remind you when?", components: components, ephemeral: true);

				default:
					return command.Respond("Unsupported command! Poke Shaosil for details.");
			}
		}

		public async Task<string> HandleMessageComponent(RestMessageComponent messageComponent)
		{
			switch (messageComponent.Data.Type)
			{
				case ComponentType.Button:
					string customButtonId = messageComponent.Data.CustomId;
					if (customButtonId.StartsWith($"{MessageCommandNames.RemindMe}-"))
					{
						// Parse referenced message ID to pass it along via component ids again if necessary
						ulong originalMessageId = ulong.Parse(Regex.Match(customButtonId, ".+-(\\d+)-").Groups.Values.Last().Value);

						if (customButtonId.Contains("-Custom"))
						{
							// Respond with a modal asking for X hours or days
							string desc = customButtonId.EndsWith("Hours") ? "hours" : "days";
							var textInput = new TextInputBuilder("Amount", $"amount-{originalMessageId}-{desc}", value: $"{(desc == "hours" ? 1 : 3)}", minLength: 1, maxLength: 3, required: true);
							return messageComponent.RespondWithModal(new ModalBuilder($"How many {desc} do you want to schedule it?", "custom-reminder-modal").AddTextInput(textInput).Build());
						}
						else
						{
							// Schedule and respond
							int minutes = int.Parse(customButtonId.Substring(customButtonId.LastIndexOf('-') + 1));
							var targetDate = DateTimeOffset.Now.AddMinutes(minutes);
							return await ScheduleReminder(originalMessageId, messageComponent, targetDate);
						}
					}
					else
					{
						return messageComponent.Respond("Unknown button ID! Poke Shaosil for details.", ephemeral: true);
					}

				default:
					return messageComponent.Respond("Unknown component type! Poke Shaosil for details.", ephemeral: true);
			}
		}

		public async Task<string> HandleModel(RestModal modal)
		{
			switch (modal.Data.CustomId)
			{
				case "custom-reminder-modal":
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

				default:
					return modal.Respond("Unknown modal type! Poke Shaosil for details.", ephemeral: true);
			}
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