using System.Diagnostics;
using System.Reflection;
using Discord;
using Microsoft.Extensions.Logging;
using Quartz;
using ServerManager.Core;
using ServerManager.Core.Interfaces;
using ServerManager.Core.Models;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using static ServerManager.Core.Models.QueueMessage;

namespace ShaosilBot.Core.SlashCommands
{
	public class ServerCommand : BaseCommand
	{
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly IRabbitMQProvider _rabbitMQProvider;
		private readonly ISchedulerFactory _schedulerFactory;

		public ServerCommand(ILogger<BaseCommand> logger,
			IDiscordRestClientProvider restClientProvider,
			IRabbitMQProvider rabbitMQProvider,
			ISchedulerFactory schedulerFactory) : base(logger)
		{
			_restClientProvider = restClientProvider;
			_rabbitMQProvider = rabbitMQProvider;
			_schedulerFactory = schedulerFactory;
		}

		public override string CommandName => "manage-server";

		public override string HelpSummary => "(ADMIN ONLY) Tools to manage tools on the bot server PC.";

		public override string HelpDetails => "Needs no explanation.";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = "Admin tools for the bot server PC",
				Options = new List<SlashCommandOptionBuilder>
				{
					new SlashCommandOptionBuilder
					{
						Type = ApplicationCommandOptionType.SubCommandGroup,
						Name = "bds",
						Description = "Manage the Minecraft Bedrock Dedicated Server",
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.BDS.Status,
								Description = "Gives the current status of the dedicated server.",
								Type = ApplicationCommandOptionType.SubCommand
							},
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.BDS.Startup,
								Description = "Starts the bedrock dedicated server.",
								Type = ApplicationCommandOptionType.SubCommand
							},
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.BDS.ListPlayers,
								Description = "Lists all currently connected players.",
								Type = ApplicationCommandOptionType.SubCommand
							},
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.BDS.Shutdown,
								Description = "Shuts down the server gracefully if no one is on.",
								Type = ApplicationCommandOptionType.SubCommand,
								Options = new List<SlashCommandOptionBuilder>
								{
									new SlashCommandOptionBuilder
									{
										Name = "force",
										Description = "Kill the process (caution)",
										Type = ApplicationCommandOptionType.Boolean
									}
								}
							},
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.BDS.Logs,
								Description = "Displays the last X logs.",
								Type = ApplicationCommandOptionType.SubCommand,
								Options = new List<SlashCommandOptionBuilder>
								{
									new SlashCommandOptionBuilder
									{
										Name = "amount",
										Description = "How many logs to display",
										Type = ApplicationCommandOptionType.Integer,
										MinValue = 1,
										MaxValue = 20
									}
								}
							}
						}
					},

					new SlashCommandOptionBuilder
					{
						Type = ApplicationCommandOptionType.SubCommandGroup,
						Name = "comfyui",
						Description = "Manage the ComfyUI web server",
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.ComfyUI.Status,
								Description = "Gives the current status of the ComfyUI server.",
								Type = ApplicationCommandOptionType.SubCommand
							},
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.ComfyUI.Startup,
								Description = "Starts the ComfyUI server.",
								Type = ApplicationCommandOptionType.SubCommand
							},
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.ComfyUI.Shutdown,
								Description = "Shuts down the ComfyUI server.",
								Type = ApplicationCommandOptionType.SubCommand
							}
						}
					},

					new SlashCommandOptionBuilder
					{
						Type = ApplicationCommandOptionType.SubCommandGroup,
						Name = "scheduled-jobs",
						Description = "Manually execute scheduled bot tasks",
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder
							{
								Name = "execute",
								Description = "Execute a scheduled job",
								Type = ApplicationCommandOptionType.SubCommand,
								Options = new List<SlashCommandOptionBuilder>
								{
									new SlashCommandOptionBuilder
									{
										Name = "job-key",
										Description = "The quartz job key identifier",
										Type = ApplicationCommandOptionType.String,
										IsRequired = true
									}
								}
							}
						}
					}
				}
			}.Build();
		}

		public override async Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			if (!await VerifyPermissions(cmdWrapper.Command.ChannelId!.Value, (cmdWrapper.Command.User as IGuildUser)!))
			{
				return cmdWrapper.Respond($"Sorry, the `/{CommandName}` command is only available for admin users in this server.");
			}

			var group = cmdWrapper.Command.Data.Options.First();
			var subCmd = group.Options.First();

			// Set the server command type based on the subcommand group
			object[]? args = Array.Empty<object>();
			if (group.Name == "bds")
			{
				if (!VerifyServerManagerRunning(out var serverMsg))
				{
					return cmdWrapper.Respond(serverMsg, ephemeral: true);
				}

				if (subCmd.Name == SupportedCommands.BDS.Shutdown)
				{
					args = [(subCmd.Options.FirstOrDefault()?.Value is bool force && force)]; // Whether to force kill it
				}
				else if (subCmd.Name == SupportedCommands.BDS.Logs)
				{
					args = [subCmd.Options.FirstOrDefault()?.Value ?? 10]; // Amount of logs. Default to 10
				}

				// Defer while we wait for a response
				return await cmdWrapper.DeferWithCode(async () =>
				{
					// Wait no longer than 30 seconds
					Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
					var completedTask = await Task.WhenAny(_rabbitMQProvider.SendCommand(eCommandType.BDS, subCmd.Name, args), timeoutTask);

					if (completedTask == timeoutTask)
					{
						await cmdWrapper.Command.FollowupAsync("Timeout while waiting for response - ask Shaosil to verify the server manager is running.");
					}
					else
					{
						var result = ((Task<QueueMessageResponse>)completedTask).Result;
						await cmdWrapper.Command.FollowupAsync($"Response from server:\n\n{result.Response}");
					}

				}, true);
			}
			else if (group.Name == "comfyui")
			{
				// Defer while we wait for a response
				return await cmdWrapper.DeferWithCode(async () =>
				{
					string result = await AttemptToStartImageGenService(cmdWrapper.Command.ChannelId!.Value, (cmdWrapper.Command.User as IGuildUser)!, subCmd.Name);
					await cmdWrapper.Command.FollowupAsync(result);
				}, true);
			}
			else if (group.Name == "scheduled-jobs")
			{
				if (subCmd.Name == "execute")
				{
					// Validate job key is supported via value reflection
					string jobKey = subCmd.Options.First().Value.ToString()!;
					string[] validChoices = [..typeof(QuartzProvider).GetFields(BindingFlags.Public | BindingFlags.Static)
						.Where(f => f.Name.EndsWith("Identity") && f.FieldType == typeof(string)).Select(f => f.GetValue(null)!.ToString()!)];
					if (string.IsNullOrWhiteSpace(jobKey) || !validChoices.Contains(jobKey))
					{
						return cmdWrapper.Respond("*Error - Invalid job key provided!*", ephemeral: true);
					}

					try
					{
						Logger.LogInformation($"Executing job '{jobKey}'...");
						var scheduler = await _schedulerFactory.GetScheduler();
						await scheduler.TriggerJob(new JobKey(jobKey));

						return cmdWrapper.Respond("*Job successfully executed!*", ephemeral: true);
					}
					catch (Exception ex)
					{
						Logger.LogError(ex, "Error executing job!");
						return cmdWrapper.Respond("*Error - Could not execute job. Check logs for details.*", ephemeral: true);
					}
				}
				else
				{
					return cmdWrapper.Respond("*Error - unsupported subcommand!*", ephemeral: true);
				}
			}
			else
			{
				return cmdWrapper.Respond("*Error - unsupported command group!*", ephemeral: true);
			}
		}

		private async Task<bool> VerifyPermissions(ulong channelID, IGuildUser user)
		{
			// Verify user has manage message permissions
			var channel = (await _restClientProvider.GetChannelAsync(channelID)) as IGuildChannel;
			return user.GetPermissions(channel).ManageMessages;
		}

		private bool VerifyServerManagerRunning(out string message)
		{
			message = string.Empty;

			bool running = Process.GetProcessesByName("ServerManager").Length > 0;

			if (!running)
			{
				message = $"ERROR: The server manager service does not appear to be running.";
			}

			return running;
		}

		public async Task<string> AttemptToStartImageGenService(ulong channelID, IGuildUser user, string cmdName)
		{
			if (!await VerifyPermissions(channelID, user))
			{
				return "Sorry, only admin users in this server may manage remote services.";
			}

			if (!VerifyServerManagerRunning(out var serverMsg))
			{
				return serverMsg;
			}

			// Wait no longer than 60 seconds
			Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
			var completedTask = await Task.WhenAny(_rabbitMQProvider.SendCommand(eCommandType.ComfyUI, cmdName, []), timeoutTask);

			if (completedTask == timeoutTask)
			{
				return "Timeout while waiting for response - ask Shaosil to verify the ServerManager service is running.";
			}
			else
			{
				var result = ((Task<QueueMessageResponse>)completedTask).Result;
				return $"Response from server:\n\n{result.Response}";
			}

		}
	}
}