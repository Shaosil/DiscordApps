using Discord;
using Microsoft.Extensions.Logging;
using Quartz;
using ServerManager.Core;
using ServerManager.Core.Interfaces;
using ServerManager.Core.Models;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using System.Runtime.InteropServices;
using System.ServiceProcess;
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
						Name = "invokeai",
						Description = "Manage the InvokeAI web server",
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.InvokeAI.Status,
								Description = "Gives the current status of the InvokeAI server.",
								Type = ApplicationCommandOptionType.SubCommand
							},
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.InvokeAI.Startup,
								Description = "Starts the InvokeAI server.",
								Type = ApplicationCommandOptionType.SubCommand
							},
							new SlashCommandOptionBuilder
							{
								Name = SupportedCommands.InvokeAI.Shutdown,
								Description = "Shuts down the InvokeAI server.",
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
										Name = "job-name",
										Description = "The name of the job to execute",
										Type = ApplicationCommandOptionType.String,
										Choices = new List<ApplicationCommandOptionChoiceProperties>
										{
											new ApplicationCommandOptionChoiceProperties { Name = "GameDealSearch", Value = "GameDealSearch" }
										},
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
			// Verify user has manage message permissions
			var channel = (await _restClientProvider.GetChannelAsync(cmdWrapper.Command.ChannelId!.Value)) as IGuildChannel;
			if (!(cmdWrapper.Command.User as IGuildUser)!.GetPermissions(channel).ManageMessages)
			{
				return cmdWrapper.Respond($"Sorry, the `/{CommandName}` command is only available for admin users in this server.", ephemeral: true);
			}

			var group = cmdWrapper.Command.Data.Options.First();
			var subCmd = group.Options.First();

			// Verify the ServerManager service is running if needed
			if (group.Name == "bds" || group.Name == "invokeai")
			{
				bool serviceRunning = false;
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					var sc = new ServiceController("ServerManager");
					try
					{
						serviceRunning = sc.Status == ServiceControllerStatus.Running;
					}
					catch (InvalidOperationException) { } // This means the service doesn't exist
				}
				if (!serviceRunning)
				{
					return cmdWrapper.Respond($"ERROR: The ServerManager service does not appear to be running on the server.", ephemeral: true);
				}
			}

			// Set the server command type based on the subcommand group
			object[]? args = Array.Empty<object>();
			if (group.Name == "bds")
			{
				if (subCmd.Name == SupportedCommands.BDS.Shutdown)
				{
					args = [(object)(subCmd.Options.FirstOrDefault()?.Value is bool force && force)]; // Whether to force kill it
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
						await cmdWrapper.Command.FollowupAsync("Timeout while waiting for response - ask Shaosil to verify the ServerManager service is running.");
					}
					else
					{
						var result = ((Task<QueueMessageResponse>)completedTask).Result;
						await cmdWrapper.Command.FollowupAsync($"Response from server:\n\n{result.Response}");
					}

				}, true);
			}
			else if (group.Name == "invokeai")
			{
				// Defer while we wait for a response
				return await cmdWrapper.DeferWithCode(async () =>
				{
					// Wait no longer than 60 seconds
					Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
					var completedTask = await Task.WhenAny(_rabbitMQProvider.SendCommand(eCommandType.InvokeAI, subCmd.Name, args), timeoutTask);

					if (completedTask == timeoutTask)
					{
						await cmdWrapper.Command.FollowupAsync("Timeout while waiting for response - ask Shaosil to verify the ServerManager service is running.");
					}
					else
					{
						var result = ((Task<QueueMessageResponse>)completedTask).Result;
						await cmdWrapper.Command.FollowupAsync($"Response from server:\n\n{result.Response}");
					}

				}, true);
			}
			else if (group.Name == "scheduled-jobs")
			{
				if (subCmd.Name == "execute")
				{
					string jobKey = null;

					if (subCmd.Options.First()!.Value.ToString() == "GameDealSearch")
					{
						jobKey = QuartzProvider.SearchForGameDealsJobIdentity;
					}

					if (string.IsNullOrWhiteSpace(jobKey))
					{
						return cmdWrapper.Respond("*Error - No job key found!*", ephemeral: true);
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
	}
}