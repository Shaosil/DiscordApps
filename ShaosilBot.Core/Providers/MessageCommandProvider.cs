using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Singletons;
using System.Reflection;

namespace ShaosilBot.Core.Providers
{
	public class MessageCommandProvider : IMessageCommandProvider
	{
		private readonly ILogger<SlashCommandProvider> _logger;
		private readonly IQuartzProvider _quartzProvider;

		public class MessageCommandNames
		{
			public const string RemindMe15Min = "Remind Me! (15 Min)";
			public const string RemindMe1Day = "Remind Me! (1 Day)";
			public const string RemindMe1Week = "Remind Me! (1 Week)";
		}

		public MessageCommandProvider(ILogger<SlashCommandProvider> logger, IQuartzProvider quartzProvider)
		{
			_logger = logger;
			_quartzProvider = quartzProvider;
		}

		public async Task BuildMessageCommands()
		{
			var allMessageNames = typeof(MessageCommandNames).GetFields(BindingFlags.Static | BindingFlags.Public).Select(f => f.GetValue(null)!.ToString()!).ToList();

			var guilds = DiscordSocketClientProvider.Client.Guilds;

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
				case MessageCommandNames.RemindMe15Min:
				case MessageCommandNames.RemindMe1Day:
				case MessageCommandNames.RemindMe1Week:
					int minutes = command.Data.Name == MessageCommandNames.RemindMe15Min ? 15 : command.Data.Name == MessageCommandNames.RemindMe1Day ? (60 * 24) : (60 * 24 * 7);
					string message = $"Reminder: {command.User.Mention} wanted to remember this message.";
					_quartzProvider.ScheduleUserReminder(command.User.Id, command.Id, command.ChannelId!.Value, DateTimeOffset.Now.AddMinutes(minutes), false, message, command.Data.Message);

					return command.Respond("Reminder scheduled successfully.", ephemeral: true);

				default:
					return command.Respond("Unsupported command! Poke Shaosil for details.");
			}
		}
	}
}