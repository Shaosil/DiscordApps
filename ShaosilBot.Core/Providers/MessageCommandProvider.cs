using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.SlashCommands;

namespace ShaosilBot.Core.Providers
{
	public class MessageCommandProvider : IMessageCommandProvider
	{
		private readonly ILogger<SlashCommandProvider> _logger;
		private readonly RemindMeCommand _remindMeCommand;
		private readonly PollCommand _pollCommand;

		public class CommandNames
		{
			public const string RemindMe = "Remind Me!";

			public class Modals
			{
				public const string CustomReminder = "custom-reminder-modal";
			}
		}

		public MessageCommandProvider(ILogger<SlashCommandProvider> logger,
			RemindMeCommand remindMeCommand,
			PollCommand pollCommand)
		{
			_logger = logger;
			_remindMeCommand = remindMeCommand;
			_pollCommand = pollCommand;
		}

		public string HandleMessageCommand(RestMessageCommand command)
		{
			switch (command.Data.Name)
			{
				case CommandNames.RemindMe:
					return _remindMeCommand.HandleRemindMeMessageCommand(command);

				default:
					return command.Respond("Unsupported command! Poke Shaosil for details.");
			}
		}

		public async Task<string> HandleMessageComponent(RestMessageComponent messageComponent)
		{
			string customButtonId = messageComponent.Data.CustomId;

			switch (messageComponent.Data.Type)
			{
				case ComponentType.Button:
					if (customButtonId.StartsWith($"{CommandNames.RemindMe}-"))
					{
						return await _remindMeCommand.HandleReminderTimeButton(messageComponent);
					}
					else
					{
						return messageComponent.Respond("Unknown button ID! Poke Shaosil for details.", ephemeral: true);
					}

				case ComponentType.SelectMenu:
					if (customButtonId == PollCommand.SelectMenuID)
					{
						return _pollCommand.HandleVote(messageComponent);
					}
					else
					{
						return messageComponent.Respond("Unknown select menu ID! Poke Shaosil for details.", ephemeral: true);
					}

				default:
					return messageComponent.Respond("Unknown component type! Poke Shaosil for details.", ephemeral: true);
			}
		}

		public async Task<string> HandleModel(RestModal modal)
		{
			switch (modal.Data.CustomId)
			{
				case CommandNames.Modals.CustomReminder:
					return await _remindMeCommand.HandleReminderTimeModal(modal);

				default:
					return modal.Respond("Unknown modal type! Poke Shaosil for details.", ephemeral: true);
			}
		}
	}
}