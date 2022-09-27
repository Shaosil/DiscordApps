using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Interfaces;
using ShaosilBot.Models;
using ShaosilBot.Providers;
using ShaosilBot.Utilities;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
	public class GitBlameCommand : BaseCommand
    {
        public const string BlameablesFilename = "GitBlameables.json";
		public const string ResponsesFilename = "GitBlameResponses.txt";

		private readonly IHttpUtilities _httpUtilities;
		private readonly IDataBlobProvider _dataBlobProvider;

        public GitBlameCommand(ILogger<GitBlameCommand> logger, IHttpUtilities httpUtilities, IDataBlobProvider dataBlobProvider) : base(logger)
        {
            _httpUtilities = httpUtilities;
            _dataBlobProvider = dataBlobProvider;
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
                Description = "Blame a random or specific user.",
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

        public override async Task<string> HandleCommandAsync(SlashCommandWrapper command)
        {
            var targetUser = command.Data.Options.FirstOrDefault(o => o.Name == "target-user")?.Value as IGuildUser;
            bool parsedFunctions = int.TryParse(command.Data.Options.FirstOrDefault(o => o.Name == "functions")?.Value.ToString(), out var functions);
            bool releaseLease = !parsedFunctions || functions != 0;
            var subscribers = await SimpleDiscordUserHelper.GetAndUpdateUsers(_dataBlobProvider, command.Guild, BlameablesFilename, releaseLease);

            // Functions are handled by themselves
            if (parsedFunctions)
            {
				var requester = command.User as IGuildUser;
                if (targetUser == null)
                    targetUser = requester;

                switch (functions)
                {
                    case 0: // Toggle subscription
                        // Only allow subscription edits to a target user if the requestor is administrator or their highest role is greater than the target's highest role
                        if (GuildHelpers.UserCanEditTargetUser(command.Guild, requester, targetUser))
                        {
                            int oldCount = subscribers.Count;

                            // Edit subscribers blob
                            if (subscribers.Any(s => s.ID == targetUser.Id))
                                subscribers.Remove(subscribers.First(s => s.ID == targetUser.Id));
                            else
                                subscribers.Add(new SimpleDiscordUser { ID = targetUser.Id, FriendlyName = targetUser.Username });

                            await _dataBlobProvider.SaveBlobTextAsync(BlameablesFilename, JsonSerializer.Serialize(subscribers, new JsonSerializerOptions { WriteIndented = true }));
                            return command.Respond($"{targetUser.Username} successfully {(oldCount < subscribers.Count ? "added" : "removed")} as a blameable");
                        }
                        else
                        {
                            return command.Respond($"You do not have sufficient permissions to edit {targetUser.Username}'s subscription. Ask someone more important than you to do it.", ephemeral: true);
                        }

                    case 1: // List blameables
                    default:
                        return command.Respond($"Current blameables:\n\n{string.Join("\n", subscribers.Select(s => "* " + s.FriendlyName))}");
                }
            }

            // Run blame functionality asynchronously
            return command.DeferWithCode(async () =>
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
                    await command.FollowupAsync("Error fetching images from Imgur");
					return;
                }

                // Get a random response line from the blob
                var responses = (await _dataBlobProvider.GetBlobTextAsync(ResponsesFilename)).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                string response = responses[Random.Shared.Next(responses.Length)];

                var channel = await command.Guild.GetChannelAsync(command.Channel.Id);
				if (targetUser == null)
				{
					// Blame one of the current subscribers, removing anyone who can not view the current channel
					subscribers = subscribers.Where(s => command.Guild.GetUserAsync(s.ID).GetAwaiter().GetResult().GetPermissions(channel).ViewChannel).ToList();
					if (subscribers.Count == 0)
					{
						await command.FollowupAsync("There are no blameable users who have access to this channel!");
						return;
					}

                    ulong randomId = subscribers[Random.Shared.Next(subscribers.Count)].ID;
                    targetUser = await command.Guild.GetUserAsync(randomId);
                }
                else
                {
					// Custom responses based on our findings on targetUser
					if (!targetUser.GetPermissions(channel).ViewChannel)
					{
						await command.FollowupAsync($"{command.User.Mention} tried to blame {targetUser.Mention}, but that user was not found in this channel, so {command.User.Mention} is to blame!");
						return;
					}
                    else if (targetUser.Id == command.User.Id)
					{
						await command.FollowupAsync($"{command.User.Mention} has rightfully and humbly blamed themselves for the latest wrongdoing. Good on them.");
						return;
					}

					// Notify everyone they specified a person
					response += "\n\n* *Targeted*";
                }

                await command.FollowupAsync($"{response.Replace("{USER}", targetUser.Mention)}\n\n{selectedImage}");
            });
        }
    }
}