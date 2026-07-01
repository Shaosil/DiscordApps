using System.Text;
using System.Text.RegularExpressions;
using Discord;
using Discord.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.ImageGeneration;
using ShaosilBot.Core.Providers;
using static ShaosilBot.Core.Providers.MessageCommandProvider;
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
* enqueue (string prompt, [string neg-prompt, string model, string seed, string sampler, int steps, int cfg, int width, int height])
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
								Description = "A specific unsigned 64 bit seed to use. Will calculate string hash if needed. Defaults to random.",
								Type = ApplicationCommandOptionType.String
							},
							new SlashCommandOptionBuilder
							{
								Name = "sampler",
								Description = "Which image generation sampler to use. Defaults to DPM++ 3M SDE.",
								Type = ApplicationCommandOptionType.String,
								Choices = _imageGenerationProvider.ValidSamplers.Select(s => new ApplicationCommandOptionChoiceProperties { Name = s, Value = s }).ToList()
							},
							new SlashCommandOptionBuilder
							{
								Name = "steps",
								Description = "How many iterations to process the image. Defaults to 30.",
								Type = ApplicationCommandOptionType.Integer,
								MinValue = 5,
								MaxValue = 50
							},
							new SlashCommandOptionBuilder
							{
								Name = "cfg",
								Description = "The CFG scale of processing. Defaults to 6.",
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
							}
						}
					}
				}.ToList()
			}.Build();
		}

		public override async Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			// Check if allowed
			if (IsDisabled(out var disabledMessage))
			{
				return cmdWrapper.Respond(disabledMessage, ephemeral: true);
			}

			var subCmd = cmdWrapper.Command.Data.Options.First();

			// Extra params
			string posPrompt = subCmd.Options.FirstOrDefault(o => o.Name == "prompt")?.Value.ToString() ?? string.Empty;
			string? negPrompt = subCmd.Options.FirstOrDefault(o => o.Name == "neg-prompt")?.Value.ToString();
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "width")?.Value.ToString() ?? "1024", out var width);
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "height")?.Value.ToString() ?? "1024", out var height);
			string? seedStr = subCmd.Options.FirstOrDefault(o => o.Name == "seed")?.Value.ToString();
			string? model = subCmd.Options.FirstOrDefault(o => o.Name == "model")?.Value.ToString();
			string sampler = subCmd.Options.FirstOrDefault(o => o.Name == "sampler")?.Value.ToString() ?? "dpmpp_3m_sde";
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "steps")?.Value.ToString() ?? "30", out var steps);
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "cfg")?.Value.ToString() ?? "6", out var cfg);

			// Since everything relies on web requests, defer with code
			return await cmdWrapper.DeferWithCode(async () =>
			{
				// Pokemon style exception handling
				try
				{
					var offlineErrors = await DoOnlineCheck(posPrompt, negPrompt, seedStr, model, steps, cfg);
					if (!string.IsNullOrWhiteSpace(offlineErrors.Key))
					{
						await cmdWrapper.Command.FollowupAsync(offlineErrors.Key, components: offlineErrors.Value);
					}
					else
					{
						// Load the original message so we can pass it to the image provider for later modifications
						var originalMessage = await cmdWrapper.GetOriginalMessage();
						if (originalMessage == null)
						{
							await cmdWrapper.Command.FollowupAsync("Error: Could not load deferred message! Nothing sent to queue.", ephemeral: true);
							return;
						}

						// Followup with in-progress or error message
						var queueData = await _imageGenerationProvider.EnqueuePrompt(originalMessage, cmdWrapper.Command.User, posPrompt, negPrompt, width, height, seedStr, model, sampler, steps, cfg, null);
						if (queueData.Success)
						{
							var messageData = BuildMessageDetailsFromQueueItem(queueData);
							await cmdWrapper.Command.FollowupAsync($"{cmdWrapper.Command.User.Mention} is generating an image!", embed: messageData.Key.Build(), components: messageData.Value.Build());
						}
						else
						{
							await cmdWrapper.Command.FollowupAsync(queueData.ErrorMessage, ephemeral: true);
						}
					}
				}
				catch (Exception ex)
				{
					await cmdWrapper.Command.FollowupAsync($"ERROR: {ex.Message}", ephemeral: true);
				}
			});
		}

		/// <summary>
		/// Checks if service is running, and if not, returns a message and component to use
		/// </summary>
		/// <returns>A KVP of the error message string and button rows component</returns>
		private async Task<KeyValuePair<string, MessageComponent>> DoOnlineCheck(string? posPrompt, string? negPrompt, string? seedStr, string? model, int? steps, int? cfg)
		{
			if (!await _imageGenerationProvider.IsOnline())
			{
				var button = new ComponentBuilder().WithButton("Start Service (Admin)", customId: $"{ImageGeneration.StartService}", style: ButtonStyle.Primary);
				string extraDesc = string.Empty;

				if (posPrompt != null)
				{
					button = button.WithButton("Retry Prompt", customId: $"{ImageGeneration.ImageGenerate}-{ImageGeneration.CmdRequeueFailed}", style: ButtonStyle.Primary);
					extraDesc = $"\n\n```{new PromptValues(posPrompt, negPrompt, seedStr, model, steps, cfg, false)}```";
				}

				string msg = $"The image generation service is not running.{extraDesc}";
				return new KeyValuePair<string, MessageComponent>(msg, button.Build());
			}

			return default;
		}

		private bool IsDisabled(out string message)
		{
			message = string.Empty;

			// Verify we are allowed to enqueue things
			if (!_configuration.GetValue<bool>("ImageAIEnabled"))
			{
				message = "Image generation is not currently allowed.";
				return true;
			}

			return false;
		}

		internal async Task<string> HandleEditImageCommand(RestMessageCommand command)
		{
			if (string.IsNullOrWhiteSpace(FindImageUrlInMessage(command.Data.Message)))
			{
				return command.Respond("No image found in referenced message.", ephemeral: true);
			}

			var offlineErrors = await DoOnlineCheck(null, null, null, null, null, null);
			if (!string.IsNullOrWhiteSpace(offlineErrors.Key))
			{
				return command.Respond(offlineErrors.Key);
			}

			// Store original message ID so we can load the image URL later
			var modal = new ModalBuilder("Edit Image with AI", $"{MessageCommandNames.Modals.EditImage}|{command.Data.Message.Id}");
			modal.AddTextInput("Prompt", "pos-prompt", TextInputStyle.Paragraph, maxLength: 1000, required: true);

			return command.RespondWithModal(modal.Build());
		}

		public async Task<string> HandleGenerationButton(RestMessageComponent messageComponent)
		{
			// Build prompt values from the previous message text
			PromptValues promptVals = BuildPromptValuesFromMessage(messageComponent.Message.Embeds.FirstOrDefault()?.Description ?? messageComponent.Message.Content);
			var offlineErrors = await DoOnlineCheck(promptVals.PosPrompt, promptVals.NegPrompt, promptVals.Seed, promptVals.Model, promptVals.Steps, promptVals.CFG);
			if (!string.IsNullOrWhiteSpace(offlineErrors.Key))
			{
				return messageComponent.Respond(offlineErrors.Key, components: offlineErrors.Value);
			}

			// Custom ID is always in format (main ID)-(command)-(item ID if it exists)
			string[] msgIDs = Regex.Match(messageComponent.Data.CustomId, "-([^-]+)(?:-(.+))?").Groups.Cast<Group>().Skip(1).Select(g => g.Value).ToArray();
			string command = msgIDs[0];
			Guid.TryParse(msgIDs.ElementAtOrDefault(1) ?? string.Empty, out var ID);

			switch (command)
			{
				case ImageGeneration.CmdCancel:
					// If successful, remove the original message and silently defer
					var cancelResult = await _imageGenerationProvider.TryCancelQueueItem(messageComponent.User, ID);
					if (cancelResult.Key)
					{
						await messageComponent.Message.DeleteAsync();
						return messageComponent.Defer();
					}

					// Otherwise, respond with the error message
					return messageComponent.Respond(cancelResult.Value, ephemeral: true);

				case ImageGeneration.CmdRequeue:
				case ImageGeneration.CmdRequeueFailed:
					// Check if allowed
					if (IsDisabled(out var disabledMessage))
					{
						return messageComponent.Respond(disabledMessage, ephemeral: true);
					}

					// Immediately defer to avoid a timeout, then handle the requeue
					_ = Task.Run(async () =>
					{
						// Followup with in-progress or error message
						var requeueMessage = await messageComponent.Channel.SendMessageAsync($"{messageComponent.User.Mention} is generating an image!");
						var queueData = await _imageGenerationProvider.EnqueuePrompt(requeueMessage, messageComponent.User, promptVals.PosPrompt, promptVals.NegPrompt, null, null, null, promptVals.Model, null, promptVals.Steps, promptVals.CFG, null);

						if (queueData.Success)
						{
							var messageData = BuildMessageDetailsFromQueueItem(queueData);
							await requeueMessage.ModifyAsync(p =>
							{
								p.Embed = messageData.Key.Build();
								p.Components = messageData.Value.Build();
							});

							// Delete the original message if this is a requeue failed
							if (command == ImageGeneration.CmdRequeueFailed)
							{
								await messageComponent.Message.DeleteAsync();
							}
						}
						else
						{
							await requeueMessage.ModifyAsync(p => p.Content = queueData.ErrorMessage);
						}
					});


					// Silently defer since we have a new message either way
					return messageComponent.Defer();

				case ImageGeneration.CmdRemix:
					// Check if allowed
					if (IsDisabled(out disabledMessage))
					{
						return messageComponent.Respond(disabledMessage, ephemeral: true);
					}

					// Just send a modal at this point (embed image ID in the custom ID). The actual requeue will come after the modal is submitted
					var modal = new ModalBuilder("Remix Parameters", $"{MessageCommandNames.Modals.RemixImage}|{ID}|{promptVals.Seed}");
					modal.AddTextInput("Prompt", "pos-prompt", TextInputStyle.Paragraph, maxLength: 1000, required: true, value: promptVals.PosPrompt);
					modal.AddTextInput("Negative Prompt", "neg-prompt", TextInputStyle.Paragraph, placeholder: "Optional", maxLength: 1000, required: false, value: promptVals.NegPrompt);
					modal.AddTextInput("Seed", "seed", required: false, placeholder: "Leave blank for random, or -1 for the original seed.", maxLength: ulong.MaxValue.ToString().Length);
					modal.AddTextInput("Model", "model", required: true, value: promptVals.Model, placeholder: "Partial names work.");
					modal.AddTextInput("Steps", "steps", minLength: 1, maxLength: 2, required: true, value: $"{promptVals.Steps ?? 30}");
					return messageComponent.RespondWithModal(modal.Build());

				case ImageGeneration.CmdDelete:
					// Always remove the original message if authorized (if we were mentioned)
					if (messageComponent.Message.Content.Contains(messageComponent.User.Mention))
					{
						await messageComponent.Message.DeleteAsync();
					}

					// Then remove the original message. If it succeeds, silently defer. Otherwise, respond with message
					var deleteResponse = await _imageGenerationProvider.TryDeleteImage(messageComponent.User, ID);
					if (deleteResponse.Key)
					{
						return messageComponent.Defer();
					}
					else
					{
						return messageComponent.Respond(deleteResponse.Value, ephemeral: true);
					}

				default:
					return messageComponent.Respond("Unknown button command! Poke Shaosil for more details.", ephemeral: true);
			}
		}

		internal async Task<string> HandleRemixModal(RestModal modal)
		{
			string[] titlePieces = modal.Data.CustomId.Split('|');
			Guid ID = Guid.Parse(titlePieces[1]);
			string origSeed = titlePieces[2];

			// Validate all of the fields
			string posPrompt = modal.Data.Components.First(c => c.CustomId == "pos-prompt").Value.Trim();
			string? negPrompt = modal.Data.Components.First(c => c.CustomId == "neg-prompt").Value?.Trim();
			string? seedStr = modal.Data.Components.First(c => c.CustomId == "seed").Value?.Trim();
			string? model = modal.Data.Components.First(c => c.CustomId == "model").Value?.Trim();
			string? stepsStr = modal.Data.Components.First(c => c.CustomId == "steps").Value?.Trim();
			var validationErrors = new List<string>();
			if (string.IsNullOrWhiteSpace(posPrompt))
			{
				validationErrors.Add("* Prompt is required.");
			}
			if (!string.IsNullOrWhiteSpace(seedStr) && seedStr != "-1" && !Regex.IsMatch(seedStr, "^\\d+$"))
			{
				validationErrors.Add("* Invalid seed. You may leave it blank to use a random seed, or use -1 for the original seed.");
			}
			if (string.IsNullOrWhiteSpace(model))
			{
				validationErrors.Add("* Model is required.");
			}
			else
			{
				// Load models and validate the passed model exists using a partial match, case insensitive search
				var configModels = _imageGenerationProvider.GetConfigValidModels();
				var allModels = await _imageGenerationProvider.GetModelsOfType("checkpoints", true);
				int modelLen = model.Length;
				model = configModels.FirstOrDefault(m => m.Value.ToLower().Contains(model.ToLower())).Key
					?? (modelLen >= 4 ? allModels.FirstOrDefault(m => m.ToLower().Contains(model.ToLower())) : null);
				if (string.IsNullOrWhiteSpace(model))
				{
					validationErrors.Add($"* Invalid model specified. Options:\n{string.Join("\n", configModels.Values.Select(m => $"  - {m}"))})");
				}
			}
			if (!int.TryParse(stepsStr, out int steps) || steps < 1 || steps > 50)
			{
				validationErrors.Add("* Steps must be between 1 and 50.");
			}
			if (validationErrors.Any())
			{
				return modal.Respond($"Invalid parameters!\n\n{string.Join("\n", validationErrors)}", ephemeral: true);
			}

			IUserMessage? requeueMessage = null;
			try
			{
				// Send a new message
				requeueMessage = await modal.Channel.SendMessageAsync($"{modal.User.Mention} is generating an image!");

				// Requeue and modify message
				var requeueData = await _imageGenerationProvider.EnqueuePrompt(requeueMessage, modal.User, posPrompt, negPrompt, null, null, seedStr == "-1" ? origSeed : seedStr, model, null, steps, null, null);
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

				return modal.Respond("Error while requeing image!", ephemeral: true);
			}

			// Defer silently if we reach this point since we will have created a new message above
			return modal.Defer();
		}

		internal async Task<string> HandleEditModal(RestModal modal)
		{
			_ = Task.Run(async() =>
			{
				// Load and validate referenced image
				ulong msgId = ulong.Parse(modal.Data.CustomId.Split('|')[1]);
				var referencedMessage = await modal.Channel.GetMessageAsync(msgId);
				string? imageUrl = FindImageUrlInMessage(referencedMessage);

				if (string.IsNullOrWhiteSpace(imageUrl))
				{
					await modal.FollowupAsync("Error: Could not find referenced image.", ephemeral: true);
					return;
				}

				// Download image to input directory
				string imageDir = _configuration.GetValue<string>("ImageAIInputFolder")!;
				string filename = $"{Guid.NewGuid()}.jpg";
				var bytes = await new HttpClient().GetByteArrayAsync(imageUrl);
				await File.WriteAllBytesAsync($"{imageDir}/{filename}", bytes);

				// Send a new message
				var newMessage = await modal.Channel.SendMessageAsync($"{modal.User.Mention} is editing an image!", messageReference: new MessageReference(msgId));

				// Queue and modify message
				string posPrompt = modal.Data.Components.First(c => c.CustomId == "pos-prompt").Value.Trim();
				var queueData = await _imageGenerationProvider.EnqueuePrompt(newMessage, modal.User, posPrompt, null, null, null, null, null, null, null, null, filename);
				var queueMessageData = BuildMessageDetailsFromQueueItem(queueData);
				await newMessage.ModifyAsync(p =>
				{
					p.Embed = queueMessageData.Key.Build();
					p.Components = queueMessageData.Value.Build();
				});

				// Message commands seem to always show a response message after the modal is submitted, so just delete it here
				await modal.DeleteOriginalResponseAsync();
			});

			return modal.Defer(ephemeral: true);
		}

		private KeyValuePair<EmbedBuilder, ComponentBuilder> BuildMessageDetailsFromQueueItem(SharedEnqueueResult queueItem)
		{
			var embedBuilder = new EmbedBuilder()
			{
				Color = new Color(0x7c0089),
				Title = $"Status: Queued ({(queueItem.LinePos > 1 ? $"#{queueItem.LinePos}" : "Next")} in line)",
				Description = new PromptValues(queueItem.PositivePrompt, queueItem.NegativePrompt, $"{queueItem.Seed}",
					queueItem.Model, queueItem.Steps, queueItem.CFG, queueItem.IsImageEdit).ToString()
			};
			var componentBuilder = new ComponentBuilder().AddRow(new ActionRowBuilder().WithButton("Cancel", $"{ImageGeneration.ImageGenerate}-{ImageGeneration.CmdCancel}-{queueItem.BatchID}", ButtonStyle.Danger));

			return new KeyValuePair<EmbedBuilder, ComponentBuilder>(embedBuilder, componentBuilder);
		}

		private PromptValues BuildPromptValuesFromMessage(string messageContent)
		{
			string posPrompt = Regex.Match(messageContent, @"Prompt: (.+?)\n(Negative|Seed)", RegexOptions.Singleline).Groups[1].Value;
			string? negPrompt = Regex.Match(messageContent, @"Negative Prompt: (.+)\nSeed", RegexOptions.Singleline).Groups.Cast<Group>().ElementAtOrDefault(1)?.Value;
			string? seed = Regex.Match(messageContent, @"Seed: (\d+)").Groups.Cast<Group>().ElementAtOrDefault(1)?.Value;
			string? model = Regex.Match(messageContent, @"Model: (.+)\n").Groups.Cast<Group>().ElementAtOrDefault(1)?.Value;
			int? steps = int.TryParse(Regex.Match(messageContent, @"Steps: (\d+)").Groups.Cast<Group>().ElementAtOrDefault(1)?.Value ?? string.Empty, out var iSteps) ? iSteps : null;
			int? cfg = int.TryParse(Regex.Match(messageContent, @"CFG: (\d+)").Groups.Cast<Group>().ElementAtOrDefault(1)?.Value ?? string.Empty, out var icfg) ? icfg : null;

			return new PromptValues(posPrompt, negPrompt, seed, model, steps, cfg, false);
		}

		private string? FindImageUrlInMessage(RestMessage message)
		{
			// Return first found image URL, whether it is an attachment or in an embed
			return message?.Attachments?.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a.Url))?.Url
				?? message?.Embeds?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Image?.Url))?.Image?.Url;
		}

		/// <summary>
		/// Used to easily store prompt data in messages
		/// </summary>
		private record PromptValues(string PosPrompt, string? NegPrompt, string? Seed, string? Model, int? Steps, int? CFG, bool isImageEdit)
		{
			public override string ToString()
			{
				// Manually use \n instead of letting StringBuilder use Environment.Newline, since parsing \r\n with Regex complicates things
				var descSB = new StringBuilder();
				descSB.Append($"Prompt: {PosPrompt}\n");
				if (!string.IsNullOrWhiteSpace(NegPrompt))
				{
					descSB.Append($"Negative Prompt: {NegPrompt}\n");
				}
				if (!isImageEdit)
				{
					descSB.Append($"Seed: {Seed}\n");
					descSB.Append($"Model: {Model}\n");
					descSB.Append($"Steps: {Steps}\n");
					descSB.Append($"CFG: {CFG}");
				}

				return descSB.ToString();
			}
		}
	}
}