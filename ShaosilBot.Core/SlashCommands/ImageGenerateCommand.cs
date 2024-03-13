using Discord;
using Discord.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.InvokeAI;
using ShaosilBot.Core.Providers;
using System.Text;
using System.Text.RegularExpressions;
using static ShaosilBot.Core.Providers.MessageCommandProvider.MessageComponentNames;

namespace ShaosilBot.Core.SlashCommands
{
	public class ImageGenerateCommand : BaseCommand
	{
		private readonly ILogger<ImageGenerateCommand> _logger;
		private readonly IConfiguration _configuration;
		private readonly IImageGenerationProvider _imageGenerationProvider;

		public ImageGenerateCommand(ILogger<ImageGenerateCommand> logger,
			IConfiguration configuration,
			IImageGenerationProvider imageGenerationProvider) : base(logger)
		{
			_logger = logger;
			_configuration = configuration;
			_imageGenerationProvider = imageGenerationProvider;
		}

		public override string CommandName => "image-generate";

		// No need for help on this one
		public override string HelpSummary => "Create a brand new image using only your words.";

		public override string HelpDetails => @$"/{CommandName} (enqueue)

SUBCOMMANDS:
* enqueue (string prompt, [string neg-prompt, string model, uint seed, string scheduler, int steps, int cfg, int width, int height, bool private])
    Sends a new item to the image processing queue for generation, with optional configuration parameters.";

		public override SlashCommandProperties BuildCommand()
		{
			int[] validDimensions = [640, 768, 832, 1024, 1280, 1344];
			var validModels = _imageGenerationProvider.GetConfigValidModels();

			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new[]
				{
					new SlashCommandOptionBuilder
					{
						Name = "enqueue",
						Description = "Enqueues an image generation job for processing",
						Type = ApplicationCommandOptionType.SubCommand,
						Options = new List<SlashCommandOptionBuilder>
						{
							new SlashCommandOptionBuilder
							{
								Name = "prompt",
								Description = "Describe the image you want to generate. Be specific and detailed.",
								Type = ApplicationCommandOptionType.String,
								IsRequired = true,
								MaxLength = 1000
							},
							new SlashCommandOptionBuilder
							{
								Name = "neg-prompt",
								Description = "An option anti-description of the image you want.",
								Type = ApplicationCommandOptionType.String,
								MaxLength = 1000
							},
							new SlashCommandOptionBuilder
							{
								Name = "model",
								Description = "Which image generation model to use.",
								Type = ApplicationCommandOptionType.String,
								Choices = validModels.Select(m => new ApplicationCommandOptionChoiceProperties { Name = m.Value, Value = m.Key }).ToList()
							},
							new SlashCommandOptionBuilder
							{
								Name = "seed",
								Description = "A specific integer seed to use. Defaults to random.",
								Type = ApplicationCommandOptionType.Integer,
								MinValue = 0,
								MaxValue = uint.MaxValue
							},
							new SlashCommandOptionBuilder
							{
								Name = "scheduler",
								Description = "Which image generation scheduler to use. Defaults to dpmpp_2m_sde_k.",
								Type = ApplicationCommandOptionType.String,
								Choices = _imageGenerationProvider.ValidSchedulers.Select(s => new ApplicationCommandOptionChoiceProperties { Name = s, Value = s }).ToList()
							},
							new SlashCommandOptionBuilder
							{
								Name = "steps",
								Description = "How many iterations to process the image. Defaults to 35.",
								Type = ApplicationCommandOptionType.Integer,
								MinValue = 5,
								MaxValue = 50
							},
							new SlashCommandOptionBuilder
							{
								Name = "cfg",
								Description = "The CFG scale of processing. Defaults to 7.",
								Type = ApplicationCommandOptionType.Integer,
								MinValue = 1,
								MaxValue = 15
							},
							new SlashCommandOptionBuilder
							{
								Name = "width",
								Description = "How many pixels wide the image should be. Defaults to 1024.",
								Type = ApplicationCommandOptionType.Integer,
								Choices = validDimensions.Select(d => new ApplicationCommandOptionChoiceProperties { Name = $"{d}", Value = d }).ToList()
							},
							new SlashCommandOptionBuilder
							{
								Name = "height",
								Description = "How many pixels tall the image should be. Defaults to 1024.",
								Type = ApplicationCommandOptionType.Integer,
								Choices = validDimensions.Select(d => new ApplicationCommandOptionChoiceProperties { Name = $"{d}", Value = d }).ToList()
							},
							new SlashCommandOptionBuilder
							{
								Name = "private",
								Description = "Whether or not the image generation is for your eyes only. Defaults to false.",
								Type = ApplicationCommandOptionType.Boolean
							}
						}
					}
				}.ToList()
			}.Build();
		}

		public override async Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			var subCmd = cmdWrapper.Command.Data.Options.First();

			// Extra params
			string posPrompt = subCmd.Options.FirstOrDefault(o => o.Name == "prompt")?.Value.ToString() ?? string.Empty;
			string negPrompt = subCmd.Options.FirstOrDefault(o => o.Name == "neg-prompt")?.Value.ToString() ?? string.Empty;
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "width")?.Value.ToString() ?? "1024", out var width);
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "height")?.Value.ToString() ?? "1024", out var height);
			string? seedStr = subCmd.Options.FirstOrDefault(o => o.Name == "seed")?.Value.ToString();
			string? model = subCmd.Options.FirstOrDefault(o => o.Name == "model")?.Value.ToString();
			string scheduler = subCmd.Options.FirstOrDefault(o => o.Name == "scheduler")?.Value.ToString() ?? "dpmpp_2m_sde_k";
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "steps")?.Value.ToString() ?? "35", out var steps);
			string cfg = subCmd.Options.FirstOrDefault(o => o.Name == "cfg")?.Value.ToString() ?? "7";
			bool.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "private")?.Value.ToString() ?? false.ToString(), out var isPrivate);

			// Since everything relies on web requests, defer with code
			return await cmdWrapper.DeferWithCode(async () =>
			{
				// Pokemon style exception handling
				try
				{
					if (!await _imageGenerationProvider.IsOnline())
					{
						await cmdWrapper.Command.FollowupAsync("The image generation service is not running. Ask an admin to start it for you.");
					}
					else
					{
						// Load the original message so we can pass it to the image provider for later modifications
						var originalMessage = await cmdWrapper.GetOriginalMessage();
						if (originalMessage == null)
						{
							await cmdWrapper.Command.FollowupAsync("Error: Could not load deferred message! Nothing sent to queue.");
							return;
						}

						// Make sure there are not more than N queue messages already
						var numPending = (await _imageGenerationProvider.GetPendingQueueItems()).Items.Count;
						if (numPending >= _configuration.GetValue<int>("InvokeAIQueueLimit"))
						{
							await cmdWrapper.Command.FollowupAsync("Sorry, too many items in the image queue! Wait a little then try again.");
						}
						else
						{
							try
							{
								// Followup with in-progress message
								var queueData = await _imageGenerationProvider.EnqueueBatchItem(originalMessage, cmdWrapper.Command.User, posPrompt, negPrompt, width, height, seedStr, model, scheduler, steps, cfg);
								var messageData = BuildMessageDetailsFromQueueItem(queueData);

								await cmdWrapper.Command.FollowupAsync($"{cmdWrapper.Command.User.Mention} is generating an image!", embed: messageData.Key.Build(), components: messageData.Value.Build());
							}
							catch (Exception ex)
							{
								await cmdWrapper.Command.FollowupAsync($"Error while enqueuing item: {ex.Message}");
							}
						}
					}
				}
				catch (Exception ex)
				{
					await cmdWrapper.Command.FollowupAsync($"ERROR: {ex.Message}");
				}
			}, ephermal: isPrivate);
		}

		public async Task<string> HandleGenerationButton(RestMessageComponent messageComponent)
		{
			// Make sure it's even online first
			if (!await _imageGenerationProvider.IsOnline())
			{
				return messageComponent.Respond($"The image generation service is not running. Ask an admin to start it for you.", ephemeral: true);
			}

			// Custom ID is always in format (main ID)-(command)-(GUID)
			string[] msgIDs = Regex.Match(messageComponent.Data.CustomId, ".+?-(.+?)-(.+)").Groups.Cast<Group>().Skip(1).Select(g => g.Value).ToArray();
			string command = msgIDs[0];
			string ID = msgIDs[1];

			switch (command)
			{
				case ImageGeneration.CmdCancel:
					// If successful, remove the original message
					if (_imageGenerationProvider.TryCancelQueueItem(messageComponent.User, ID, out var cancelResponse))
					{
						await messageComponent.Message.DeleteAsync();
					}
					return messageComponent.Respond(cancelResponse, ephemeral: true);

				case ImageGeneration.CmdRequeue:

					IUserMessage? requeueMessage = null;
					try
					{
						// Send a new message
						requeueMessage = await messageComponent.Channel.SendMessageAsync($"{messageComponent.User.Mention} is generating an image!");

						// Requeue and modify message
						var requeueData = await _imageGenerationProvider.RequeueImage(ID, requeueMessage, messageComponent.User);
						var requeueMessageData = BuildMessageDetailsFromQueueItem(requeueData);
						await requeueMessage.ModifyAsync(p =>
						{
							p.Embed = requeueMessageData.Key.Build();
							p.Components = requeueMessageData.Value.Build();
						});

					}
					catch (Exception ex)
					{
						if (requeueMessage != null)
						{
							await requeueMessage.ModifyAsync(p => { p.Content = $"Error requeueing image: {ex.Message}"; });
						}
					}

					// Always silently defer the button interaction since we create a new message
					return messageComponent.Defer();

				case ImageGeneration.CmdDelete:
					// If successful, remove the original message
					if (_imageGenerationProvider.TryDeleteImage(messageComponent.User, ID, out var deleteResponse))
					{
						await messageComponent.Message.DeleteAsync();
					}
					return messageComponent.Respond(deleteResponse, ephemeral: true);

				default:
					return messageComponent.Respond("Unknown button command! Poke Shaosil for more details.", ephemeral: true);
			}
		}

		private KeyValuePair<EmbedBuilder, ComponentBuilder> BuildMessageDetailsFromQueueItem(FriendlyEnqueueResult queueItem)
		{
			var descSB = new StringBuilder();
			descSB.AppendLine($"Prompt: {queueItem.PositivePrompt}");
			if (!string.IsNullOrWhiteSpace(queueItem.NegativePrompt))
			{
				descSB.AppendLine($"Negative Prompt: {queueItem.NegativePrompt}");
			}
			descSB.AppendLine($"Seed: {queueItem.Seed}");
			descSB.AppendLine($"Model: {queueItem.Model}");
			descSB.AppendLine($"Steps: {queueItem.Steps}");
			descSB.AppendLine($"CFG: {queueItem.CFG}");
			var embedBuilder = new EmbedBuilder()
			{
				Color = new Color(0x7c0089),
				Title = $"Status: Queued ({(queueItem.LinePos > 1 ? $"#{queueItem.LinePos}" : "Next")} in line)",
				Description = descSB.ToString()
			};
			var componentBuilder = new ComponentBuilder().AddRow(new ActionRowBuilder().WithButton("Cancel", $"{ImageGeneration.ImageGenerate}-{ImageGeneration.CmdCancel}-{queueItem.BatchID}", ButtonStyle.Danger));

			return new KeyValuePair<EmbedBuilder, ComponentBuilder>(embedBuilder, componentBuilder);
		}
	}
}