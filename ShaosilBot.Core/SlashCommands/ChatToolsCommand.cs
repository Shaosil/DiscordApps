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
		private readonly IDiscordRestClientProvider _restClientProvider;

		public ChatToolsCommand(ILogger<BaseCommand> logger, IFileAccessHelper fileAccessHelper, IDiscordRestClientProvider restClientProvider) : base(logger)
		{
			_fileAccessHelper = fileAccessHelper;
			_restClientProvider = restClientProvider;
		}

		public override string CommandName => "chat-tools";

		public override string HelpSummary => "Manage the way you chat with the bot.";

		public override string HelpDetails => @$"/{CommandName} stats (self | all) | custom-prompt (show | set [string prompt]) | clear-history

SUBCOMMANDS:
* stats
	- self
		Displays your detailed usage for the current month, including tokens.

	- all
		Displays the top 10 users' usages for the current month.

* custom-prompt
	- show
		Displays your custom instructions to the bot that will be included in each message

	- set (prompt)
		Sets or clears your custom instructions

* clear-history (ADMIN ONLY)
	Erases all chat history from bot's memory for current channel. Only message administrators may use this command.";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new List<SlashCommandOptionBuilder>
				{
					new SlashCommandOptionBuilder
					{
						Name = "stats",
						Description = "Show your current chat usage statistics",
						Type = ApplicationCommandOptionType.SubCommandGroup,
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder { Name = "self", Description = "Show your own detailed stats.", Type =  ApplicationCommandOptionType.SubCommand },
							new SlashCommandOptionBuilder { Name = "all", Description = "Show others' current stats.", Type =  ApplicationCommandOptionType.SubCommand }
						}
					},
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
					},
					new SlashCommandOptionBuilder { Name = "clear-history", Description = "Clears the current channel's history", Type = ApplicationCommandOptionType.SubCommand }
				}
			}.Build();
		}

		public override async Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			string returnMessage;
			bool ephermal = true;

			// Load this user's info - keep lease in case we need to update it
			var allUsers = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, ChatGPTUser>>(ChatGPTProvider.ChatGPTUsersFile, true);
			var ourInfo = allUsers[cmdWrapper.Command.User.Id];

			var subCmd = cmdWrapper.Command.Data.Options.First();
			if (subCmd.Name == "stats")
			{
				// Subcommand self or all
				subCmd = subCmd.Options.First();

				var statsBuilder = new StringBuilder();

				if (subCmd.Name == "self")
				{
					// Show stats
					statsBuilder.AppendLine($"Chat usage since {DateTime.Now:M/1/yyyy}:");
					statsBuilder.AppendLine();
					statsBuilder.AppendLine($"```* Chats sent:       {ourInfo.TokensUsed.Count():N0}");
					statsBuilder.AppendLine($"* Used tokens:      {ourInfo.TokensUsed.Sum(t => t.Value):N0}");
					statsBuilder.AppendLine($"* Remaining tokens: {ourInfo.AvailableTokens:N0}");
					statsBuilder.AppendLine($"* Borrowed tokens:  {allUsers.SelectMany(u => u.Value.LentTokens.Where(t => t.Key == cmdWrapper.Command.User.Id)).Sum(t => t.Value):N0}");
					statsBuilder.AppendLine($"* Lent tokens:      {ourInfo.LentTokens.Sum(t => t.Value):N0}");
					statsBuilder.AppendLine();
					statsBuilder.AppendLine($"* Custom prompt:    {ourInfo.CustomSystemPrompt ?? "(none)"}```");
				}
				else
				{
					statsBuilder.AppendLine($"Top user token usage since {DateTime.Now:M/1/yyyy}:");
					statsBuilder.AppendLine();

					var topUsers = allUsers.Where(u => u.Value.TokensUsed.Any()).OrderBy(u => u.Value.AvailableTokens).Take(10).ToList();
					if (!topUsers.Any())
					{
						statsBuilder.AppendLine("No current usage for this month. Let's get chatting folks!");
					}
					else
					{
						// TODO: List current starting tokens per user?

						for (int i = 0; i < topUsers.Count; i++)
						{
							statsBuilder.AppendLine($"{i + 1,2}) <@{topUsers[i].Key}>: {topUsers[i].Value.TokensUsed.Sum(t => t.Value):N0} ({topUsers[i].Value.TokensUsed.Count:N0} chats)");
						}

						// TODO: Progress bar towards max limit?
						statsBuilder.AppendLine();
						statsBuilder.AppendLine($"TOTAL: {topUsers.Sum(u => u.Value.TokensUsed.Sum(t => t.Value)):N0} tokens and {topUsers.Sum(u => u.Value.TokensUsed.Count):N0} chats.");


					}
					ephermal = false;
				}

				returnMessage = statsBuilder.ToString();
			}
			else if (subCmd.Name == "custom-prompt")
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
			else
			{
				// Verify user has manage message permissions
				var channel = (await _restClientProvider.GetChannelAsync(cmdWrapper.Command.ChannelId!.Value)) as IGuildChannel;
				if ((cmdWrapper.Command.User as IGuildUser)!.GetPermissions(channel).ManageMessages)
				{
					var allHistory = _fileAccessHelper.LoadFileJSON<Dictionary<ulong, List<ChatGPTChannelMessage>>>(ChatGPTProvider.ChatLogFile, true);
					var ourHistory = allHistory.GetValueOrDefault(channel.Id);

					if ((ourHistory?.Count ?? 0) == 0)
					{
						returnMessage = "No current history stored in this channel.";
					}
					else
					{
						ourHistory!.Clear();
						_fileAccessHelper.SaveFileJSON(ChatGPTProvider.ChatLogFile, allHistory);
						returnMessage = "Channel history successfully wiped from bot's memory.";
						ephermal = false;
					}
					_fileAccessHelper.ReleaseFileLease(ChatGPTProvider.ChatLogFile);
				}
				else
				{
					returnMessage = "Sorry, only admins may clear a bot's history knowledge.";
				}
			}
			_fileAccessHelper.ReleaseFileLease(ChatGPTProvider.ChatGPTUsersFile);

			return cmdWrapper.Respond(returnMessage, ephemeral: ephermal);
		}
	}
}