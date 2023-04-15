using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;

namespace ShaosilBot.Core.SlashCommands
{
	public class GitBlameCommand : BaseCommand
	{
		public const string BlameablesFilename = "GitBlameables.json";
		public const string ResponsesFilename = "GitBlameResponses.txt";

		private readonly IGuildHelper _guildHelper;
		private readonly IHttpUtilities _httpUtilities;
		private readonly IFileAccessHelper _fileAccessHelper;
		private readonly IDiscordRestClientProvider _restClientProvider;

		public GitBlameCommand(ILogger<BaseCommand> logger,
			IGuildHelper guildHelper,
			IHttpUtilities httpUtilities,
			IFileAccessHelper fileAccessHelper,
			IDiscordRestClientProvider restClientProvider) : base(logger)
		{
			_guildHelper = guildHelper;
			_httpUtilities = httpUtilities;
			_fileAccessHelper = fileAccessHelper;
			_restClientProvider = restClientProvider;
		}

		public override string CommandName => "git-blame";

		public override string HelpSummary => "Randomly chooses from a list of blameable users and says everything is their fault.";

		public override string HelpDetails => @$"/{CommandName} [user target-user] | [int functions]

Passing no arguments will randomly select a user defined in the blameable list.

OPTIONAL ARGS:
* target-user:
    Blames a specific user (others will see they were targeted). This can be used with any user except ones not in the current channel.

* functions:
    - Toggle Subscription
        Adds or removes the calling user to the list of blameable users. If combined with the target-user arg, it will add or remove that user, provided the requsting user has a higher role.
    
    - List Blameables
        Displays a list of all users who are currently able to be randomly selected for blame.";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new[]
				{
					new SlashCommandOptionBuilder { Name = "target-user", Type = ApplicationCommandOptionType.User, Description = "Blame someone specific" },
					new SlashCommandOptionBuilder
					{
						Name = "functions",
						Type = ApplicationCommandOptionType.Integer,
						Description = "Extra utility functions",
						Choices = new[]
						{
							new ApplicationCommandOptionChoiceProperties { Name = "Toggle Subscription", Value = 0 },
							new ApplicationCommandOptionChoiceProperties { Name = "List Blameables", Value = 1 }
						}.ToList()
					}
				}.ToList()
			}.Build();
		}

		public override async Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			var targetUser = (cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "target-user")?.Value as IGuildUser)!;
			bool parsedFunctions = int.TryParse(cmdWrapper.Command.Data.Options.FirstOrDefault(o => o.Name == "functions")?.Value.ToString(), out var functions);
			bool keepLock = parsedFunctions && functions == 0;
			var subscribers = _guildHelper.LoadUserIDs(BlameablesFilename);
			var curGuild = _restClientProvider.Guilds.First(g => g.Id == cmdWrapper.Command.GuildId);

			// Functions are handled by themselves
			if (parsedFunctions)
			{
				var requestor = cmdWrapper.Command.User as IGuildUser;
				if (targetUser == null)
					targetUser = requestor;

				switch (functions)
				{
					case 0: // Toggle subscription
							// Only allow subscription edits to a target user if the requestor is administrator or their highest role is greater than the target's highest role
						if (_guildHelper.UserCanEditTargetUser(curGuild, requestor, targetUser))
						{
							int oldCount = subscribers.Count;

							// Edit subscribers blob
							if (subscribers.Any(s => s == targetUser.Id))
								subscribers.Remove(subscribers.First(s => s == targetUser.Id));
							else
								subscribers.Add(targetUser.Id);

							_fileAccessHelper.SaveFileJSON(BlameablesFilename, subscribers);
							return cmdWrapper.Respond($"{targetUser.Username} successfully {(oldCount < subscribers.Count ? "added" : "removed")} as a blameable");
						}
						else
						{
							return cmdWrapper.Respond($"You do not have sufficient permissions to edit {targetUser.Username}'s subscription. Ask someone more important than you to do it.", ephemeral: true);
						}

					case 1: // List blameables
					default:
						return cmdWrapper.Respond($"Current blameables:\n\n{string.Join("\n", subscribers.Select(s => $"* <@{s}>"))}");
				}
			}

			// Run blame functionality asynchronously
			return await cmdWrapper.DeferWithCode(async () =>
			{
				// Get a list of all images in my gitblame album and pick a random one
				string selectedImage;
				try
				{
					selectedImage = await _httpUtilities.GetRandomGitBlameImage();
				}
				catch (Exception ex)
				{
					Logger.LogError(ex, "Error fetching images from Imgur");
					await cmdWrapper.Command.FollowupAsync("Error fetching images from Imgur");
					return;
				}

				// Get a random response line from the blob
				var responses = _fileAccessHelper.LoadFileText(ResponsesFilename).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
				string response = responses[Random.Shared.Next(responses.Length)];

				var channel = (await _restClientProvider.GetChannelAsync(cmdWrapper.Command.ChannelId!.Value)) as IGuildChannel;
				if (targetUser == null)
				{
					var existingUsers = new List<ulong>(subscribers);
					while (targetUser == null && subscribers.Count > 0)
					{
						// Pick a random subscriber
						ulong randomId = subscribers[Random.Shared.Next(subscribers.Count)];
						targetUser = await curGuild.GetUserAsync(randomId);

						// Check the user exists in the guild and remove them from both lists if not
						if (targetUser == null)
						{
							existingUsers.Remove(randomId);
							subscribers.Remove(randomId);
						}
						// If they simply don't have permission, update the subscribers list and null out targetUser so we can try again
						else if (!targetUser.GetPermissions(channel).ViewChannel)
						{
							subscribers.Remove(randomId);
							targetUser = null;
						}
					}

					// Update the blameables file in case we removed any users
					_fileAccessHelper.SaveFileJSON(BlameablesFilename, existingUsers);

					// Notify if there are no subscribers
					if (targetUser == null)
					{
						await cmdWrapper.Command.FollowupAsync("There are no blameable users who have access to this channel!");
						return;
					}
				}
				else
				{
					// Custom responses based on our findings on targetUser
					if (!targetUser.GetPermissions(channel).ViewChannel)
					{
						await cmdWrapper.Command.FollowupAsync($"{cmdWrapper.Command.User.Mention} tried to blame {targetUser.Mention}, but that user was not found in this channel, so {cmdWrapper.Command.User.Mention} is to blame!");
						return;
					}
					else if (targetUser.Id == cmdWrapper.Command.User.Id)
					{
						await cmdWrapper.Command.FollowupAsync($"{cmdWrapper.Command.User.Mention} has rightfully and humbly blamed themselves for the latest wrongdoing. Good on them.");
						return;
					}

					// Notify everyone they specified a person
					response += "\n\n* *Targeted*";
				}

				await cmdWrapper.Command.FollowupAsync($"{response.Replace("{USER}", targetUser.Mention)}\n\n{selectedImage}");
			});
		}
	}
}