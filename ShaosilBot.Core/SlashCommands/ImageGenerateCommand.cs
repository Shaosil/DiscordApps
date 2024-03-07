using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;

namespace ShaosilBot.Core.SlashCommands
{
	public class ImageGenerateCommand : BaseCommand
	{
		private readonly IImageGenerationProvider _imageGenerationProvider;

		public ImageGenerateCommand(ILogger<ImageGenerateCommand> logger, IImageGenerationProvider imageGenerationProvider) : base(logger)
		{
			_imageGenerationProvider = imageGenerationProvider;
		}

		public override string CommandName => "image-generate";

		// No need for help on this one
		public override string HelpSummary => "Create a brand new image using only your words.";

		public override string HelpDetails => @$"/{CommandName} (enqueue)

SUBCOMMANDS:
* enqueue (string prompt, [int width, int height, uint seed, string model, string scheduler, int steps, int cfg, bool private])
    Sends a new item to the image processing queue for generation, with optional configuration parameters.

* list-models
    Displays all currently available image generation models, and notes the default.";

		public override SlashCommandProperties BuildCommand()
		{
			int[] validDimensions = [640, 768, 1024];

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
								IsRequired = true
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
								Name = "seed",
								Description = "A specific integer seed to use. Defaults to random.",
								Type = ApplicationCommandOptionType.Integer,
								MinValue = 0,
								MaxValue = uint.MaxValue
							},
							new SlashCommandOptionBuilder
							{
								Name = "model",
								Description = "Which image generation model to use. Use list-models for details.",
								Type = ApplicationCommandOptionType.String
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
								Name = "private",
								Description = "Whether or not the image generation is for your eyes only. Defaults to false.",
								Type = ApplicationCommandOptionType.Boolean
							}
						}
					},

					new SlashCommandOptionBuilder
					{
						Name = "list-models",
						Description = "Displays all currently available image generation models.",
						Type = ApplicationCommandOptionType.SubCommand
					}
				}.ToList()
			}.Build();
		}

		public override async Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			var subCmd = cmdWrapper.Command.Data.Options.First();

			// Extra params
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "width")?.Value.ToString() ?? "1024", out var width);
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "height")?.Value.ToString() ?? "1024", out var height);
			string? seedStr = subCmd.Options.FirstOrDefault(o => o.Name == "seed")?.Value.ToString();
			uint seed;
			if (seedStr == null)
			{
				// TODO - MOVE TO PROVIDER
				var uintBytes = new byte[4];
				new Random().NextBytes(uintBytes);
				seed = BitConverter.ToUInt32(uintBytes);
			}
			else
			{
				seed = uint.Parse(seedStr);
			}
			string? model = subCmd.Options.FirstOrDefault(o => o.Name == "model")?.Value.ToString();
			string scheduler = subCmd.Options.FirstOrDefault(o => o.Name == "scheduler")?.Value.ToString() ?? "dpmpp_2m_sde_k";
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "steps")?.Value.ToString() ?? "35", out var steps);
			int.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "cfg")?.Value.ToString() ?? "7", out var cfg);
			bool.TryParse(subCmd.Options.FirstOrDefault(o => o.Name == "private")?.Value.ToString() ?? false.ToString(), out var isPrivate);

			// Since everything relies on web requests, defer with code
			return await cmdWrapper.DeferWithCode(async () =>
			{
				// Pokemon style exception handling
				try
				{
					// List models
					if (subCmd.Name == "list-models")
					{
						if (!await _imageGenerationProvider.IsOnline())
						{
							await cmdWrapper.Command.FollowupAsync("The image generation service is not running. Ask an admin to start it for you.");
						}
						else
						{
							var models = (await _imageGenerationProvider.GetModels()).Select(m => $"* {m}");
							await cmdWrapper.Command.FollowupAsync($"Available models:\n\n{string.Join('\n', models)}");
						}
					}

					// The big one
					else if (subCmd.Name == "enqueue")
					{
						await cmdWrapper.Command.FollowupAsync("Work in progress! Poke Shaosil for more details. Or don't. That's probably better.");
					}
				}
				catch (Exception ex)
				{
					await cmdWrapper.Command.FollowupAsync($"ERROR: {ex.Message}");
				}
			}, ephermal: true /* subCmd.Name == "list-models" || isPrivate*/);
		}
	}
}