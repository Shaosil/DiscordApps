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

		public override string HelpDetails => @$"/{CommandName} stats (self | all) | custom-prompt (show | set [string prompt, string user-or-bot]) | clear-history

SUBCOMMANDS:
* stats
	- self
		Displays your detailed usage for the current month, including tokens.

	- all
		Displays the top 10 users' usages for the current month.

* custom-prompt
	- show
		Displays your custom instructions to the bot that will be included in each message

	- set (prompt, user-or-bot)
		Sets or clears your custom prompts for yourself and the bot

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
								Description = "Sets (or clears) a custom prompt for yourself or the bot's first response.",
								Type = ApplicationCommandOptionType.SubCommand,
								Options = new List<SlashCommandOptionBuilder>
								{
									new SlashCommandOptionBuilder { Name = "prompt", Description = "The custom prompt", Type = ApplicationCommandOptionType.String, MaxLength = 100 },
									new SlashCommandOptionBuilder
									{
										Name = "user-or-bot",
										Description = "Who the custom prompt is for",
										Type = ApplicationCommandOptionType.String,
										IsRequired = true,
										Choices = new[]
										{
											new ApplicationCommandOptionChoiceProperties { Name = "User", Value = "User" },
											new ApplicationCommandOptionChoiceProperties { Name = "Bot", Value = "Bot" }
										}.ToList()
									}
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
					statsBuilder.AppendLine($"Chat usage since {DateTimeOffset.Now:M/1/yyyy}:");
					statsBuilder.AppendLine();
					statsBuilder.AppendLine($"```* Chats sent:              {ourInfo.InputTokensUsed.Count():N0}");
					statsBuilder.AppendLine($"* Used input tokens:       {ourInfo.InputTokensUsed.Sum(t => t.Value):N0}");
					statsBuilder.AppendLine($"* Used output tokens:      {ourInfo.OutputTokensUsed.Sum(t => t.Value):N0}");
					statsBuilder.AppendLine($"* Remaining input tokens:  {ourInfo.AvailableInputTokens:N0}");
					statsBuilder.AppendLine($"* Remaining output tokens: {ourInfo.AvailableOutputTokens:N0}");
					statsBuilder.AppendLine($"* Borrowed input tokens:   {allUsers.SelectMany(u => u.Value.LentInputTokens.Where(t => t.Key == cmdWrapper.Command.User.Id)).Sum(t => t.Value):N0}");
					statsBuilder.AppendLine($"* Borrowed output tokens:  {allUsers.SelectMany(u => u.Value.LentOutputTokens.Where(t => t.Key == cmdWrapper.Command.User.Id)).Sum(t => t.Value):N0}");
					statsBuilder.AppendLine($"* Lent input tokens:       {ourInfo.LentInputTokens.Sum(t => t.Value):N0}");
					statsBuilder.AppendLine($"* Lent output tokens:      {ourInfo.LentOutputTokens.Sum(t => t.Value):N0}");
					statsBuilder.AppendLine();
					statsBuilder.AppendLine($"* Custom user prompt:      {ourInfo.CustomUserPrompt ?? "(none)"}");
					statsBuilder.AppendLine($"* Custom asst prompt:      {ourInfo.CustomAssistantPrompt ?? "(none)"}```");
				}
				else
				{
					statsBuilder.AppendLine($"Top user token usage since {DateTimeOffset.Now:M/1/yyyy}:");
					statsBuilder.AppendLine();

					var topUsers = allUsers.Where(u => u.Value.TotalTokensUsed > 0).OrderBy(u => u.Value.TotalAvailableTokens).Take(10).ToList();
					if (!topUsers.Any())
					{
						statsBuilder.AppendLine("No current usage for this month. Let's get chatting folks!");
					}
					else
					{
						// TODO: List current starting tokens per user?
						for (int i = 0; i < topUsers.Count; i++)
						{
							statsBuilder.AppendLine($"{i + 1,2}) <@{topUsers[i].Key}>: {topUsers[i].Value.TotalTokensUsed:N0} ({topUsers[i].Value.InputTokensUsed.Count:N0} chats)");
						}

						// TODO: Progress bar towards max limit?
						statsBuilder.AppendLine();
						statsBuilder.AppendLine($"TOTAL: {topUsers.Sum(u => u.Value.TotalTokensUsed):N0} tokens and {topUsers.Sum(u => u.Value.InputTokensUsed.Count):N0} chats.");


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
					returnMessage = $"Custom user prompt: {ourInfo.CustomUserPrompt ?? "(None)"}\nCustom bot prompt: {ourInfo.CustomAssistantPrompt ?? "(None)"}";
				}
				else
				{
					string? prompt = subCmd.Options.FirstOrDefault(o => o.Name == "prompt")?.Value.ToString();
					bool forUser = (string)subCmd.Options.First(o => o.Name == "user-or-bot").Value == "User";
					if (forUser)
					{
						ourInfo.CustomUserPrompt = prompt;
					}
					else
					{
						ourInfo.CustomAssistantPrompt = prompt;
					}
					_fileAccessHelper.SaveFileJSON(ChatGPTProvider.ChatGPTUsersFile, allUsers, false); // Manually release below
					returnMessage = $"Custom prompt successfully {(string.IsNullOrWhiteSpace(prompt) ? "cleared" : "updated")} for {(forUser ? "user" : "bot")}.";
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