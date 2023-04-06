using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models;
using ShaosilBot.Core.Providers;
using ShaosilBot.Core.Singletons;
using System.Text;

namespace ShaosilBot.Core.SlashCommands
{
	public class ChatToolsCommand : BaseCommand
	{
		private readonly IFileAccessHelper _fileAccessHelper;

		public ChatToolsCommand(ILogger<BaseCommand> logger, IFileAccessHelper fileAccessHelper) : base(logger)
		{
			_fileAccessHelper = fileAccessHelper;
		}

		public override string CommandName => "chat-tools";

		public override string HelpSummary => "Manage the way you chat with the bot.";

		public override string HelpDetails => @$"/{CommandName} stats | custom-prompt (show | set [string prompt])

SUBCOMMANDS:
* stats
    Displays your usage for the current month, including tokens.

* custom-prompt
	- show
		Displays your custom instructions to the bot that will be included in each message

	- set (prompt)
		Sets or clears your custom instructions";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new List<SlashCommandOptionBuilder>
				{
					new SlashCommandOptionBuilder { Name = "stats", Description = "Show your current chat usage statistics", Type = ApplicationCommandOptionType.SubCommand },
					new SlashCommandOptionBuilder
					{
						Name = "custom-prompt",
						Description = "Manage your custom instructions for the bot",
						Type = ApplicationCommandOptionType.SubCommandGroup,
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder { Name = "show", Description = "Show your current custom prompt", Type = ApplicationCommandOptionType.SubCommand },
							new SlashCommandOptionBuilder
							{
								Name = "set",
								Description = "Sets (or clears) your custom prompt",
								Type = ApplicationCommandOptionType.SubCommand,
								Options = new List<SlashCommandOptionBuilder>
								{
									new SlashCommandOptionBuilder { Name = "prompt", Description = "The custom prompt", Type = ApplicationCommandOptionType.String, MaxLength = 100 }
								}
							}
						}
					}
				}
			}.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper command)
		{
			string returnMessage = "Work in progress. Poke Shaosil for faster results.";

			// Load this user's info - keep lease in case we need to update it
			var allUsers = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTProvider.ChatGPTUsersFile, true);
			var ourInfo = allUsers[command.User.Id];

			var subCmd = command.Data.Options.First();
			if (subCmd.Name == "stats")
			{
				// Show stats
				var statsBuilder = new StringBuilder();
				statsBuilder.AppendLine($"Chat usage since {DateTime.Now:M/1/yyyy}:");
				statsBuilder.AppendLine();
				statsBuilder.AppendLine($"```* Chats sent:       {ourInfo.TokensUsed.Count():N0}");
				statsBuilder.AppendLine($"* Used tokens:      {ourInfo.TokensUsed.Sum(t => t.Value):N0}");
				statsBuilder.AppendLine($"* Remaining tokens: {ourInfo.AvailableTokens:N0}");
				statsBuilder.AppendLine($"* Borrowed tokens:  {allUsers.SelectMany(u => u.Value.LentTokens.Where(t => t.Key == command.User.Id)).Sum(t => t.Value):N0}");
				statsBuilder.AppendLine($"* Lent tokens:      {ourInfo.LentTokens.Sum(t => t.Value):N0}");
				statsBuilder.AppendLine();
				statsBuilder.AppendLine($"* Custom prompt:    {ourInfo.CustomSystemPrompt ?? "(none)"}```");
				returnMessage = statsBuilder.ToString();
			}
			else
			{
				// Subcommand show or set
				subCmd = subCmd.Options.First();

				if (subCmd.Name == "show")
				{
					if (!string.IsNullOrWhiteSpace(ourInfo.CustomSystemPrompt)) returnMessage = $"Your current custom prompt is:\n\n`{ourInfo.CustomSystemPrompt}`";
					else returnMessage = "You currently have no customized prompt.";
				}
				else
				{
					var prompt = subCmd.Options.FirstOrDefault()?.Value.ToString();
					ourInfo.CustomSystemPrompt = prompt;
					_fileAccessHelper.SaveFileJSON(ChatGPTProvider.ChatGPTUsersFile, allUsers, false); // Manually release below
					returnMessage = $"Custom prompt successfully {(string.IsNullOrWhiteSpace(prompt) ? "cleared" : "updated")}.";
				}
			}
			_fileAccessHelper.ReleaseFileLease(ChatGPTProvider.ChatGPTUsersFile);

			return Task.FromResult(command.Respond(returnMessage, ephemeral: true));
		}
	}
}