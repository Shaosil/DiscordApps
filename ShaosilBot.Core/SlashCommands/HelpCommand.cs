using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using System.Text;

namespace ShaosilBot.Core.SlashCommands
{
	public class HelpCommand : BaseCommand
	{
		private readonly ISlashCommandProvider _slashCommandProvider;

		public HelpCommand(ILogger<HelpCommand> logger, ISlashCommandProvider slashCommandProvider) : base(logger)
		{
			_slashCommandProvider = slashCommandProvider;
		}

		public override string CommandName => "help";

		// No need for help on this one
		public override string HelpSummary => throw new NotImplementedException();
		public override string HelpDetails => throw new NotImplementedException();

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = "Provides info about this wonderful Discord bot",
				Options = new[]
				{
					new SlashCommandOptionBuilder
					{
						Name = "command",
						Description = "Ask about a specific command",
						Type = ApplicationCommandOptionType.String,
                        // Specifying choices would reveal all commands to all users regardless of permissions
                        //Choices = _slashCommandProvider.CommandToTypeMappings.Keys.Select(c => new ApplicationCommandOptionChoiceProperties { Name = c, Value = c }).ToList()
                    }
				}.ToList()
			}.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper command)
		{
			var userPermissions = (command.User as IGuildUser).GuildPermissions;
			var allowedCommands = _slashCommandProvider.CommandProperties
				.Where(c => c.Value.Name.Value != CommandName && (!c.Value.DefaultMemberPermissions.IsSpecified || userPermissions.Has(c.Value.DefaultMemberPermissions.Value)))
				.Select(c => c.Key)
				.OrderBy(c => c).ToList();
			var sb = new StringBuilder();

			// If no specific command was specified, display all info summarized
			if (command.Data.Options.Count == 0)
			{
				int maxCmdLength = allowedCommands.Max(k => k.Length);

				sb.AppendLine("Hey there! I'm ShaosilBot, designed by (you guessed it) Shaosil himself. All hail Shaosil. Here's what I can do:");
				foreach (string cmd in allowedCommands)
				{
					var summary = _slashCommandProvider.GetSlashCommandHandler(cmd).HelpSummary;
					sb.AppendLine();
					sb.AppendLine($"`/{cmd}: {summary}`");
				}
				sb.AppendLine();
				sb.AppendLine($"For more details on any command, type /{CommandName} (command).");
				sb.AppendLine();
				sb.AppendLine($"**By the way, you can start your message with `!c` to chat with me!**");
			}
			// Otherwise get that command's specific help details
			else
			{
				string cmd = (command.Data.Options.First().Value as string).ToLower().Trim();
				var matchingCommands = allowedCommands.Where(c => c.Contains(cmd)).ToList();
				if (matchingCommands.Count == 1)
				{
					var details = _slashCommandProvider.GetSlashCommandHandler(matchingCommands[0]).HelpDetails;

					sb.AppendLine($"The '/{matchingCommands[0]}' command is used as follows:");
					sb.AppendLine();
					sb.AppendLine($"```{details}```");
				}
				else if (matchingCommands.Count > 1)
				{
					sb.AppendLine($"Error: Multiple commands match search text. Please specify from the following:");
					foreach (var matchingCommand in matchingCommands)
					{
						sb.Append($"\n* {matchingCommand}");
					}
				}
				else if (cmd == "!c")
				{
					sb.AppendLine("If you want to chat with me, start any message with `!c` and I'll respond. Full disclaimer, Shaosil often tries to give me an unpredictable personality. Any offensive content I generate can be blamed on that.");
				}
				else
				{
					sb.AppendLine($"Error: Either the command '/{cmd}' does not exist, or you do not have permission to access it.");
				}
			}

			return Task.FromResult(command.Respond(sb.ToString(), ephemeral: true));
		}
	}
}