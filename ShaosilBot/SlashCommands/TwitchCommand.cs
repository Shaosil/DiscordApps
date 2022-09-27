using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Providers;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
	public class TwitchCommand : BaseCommand
    {
        private readonly TwitchProvider _twitchProvider;

        public TwitchCommand(ILogger<TwitchCommand> logger, TwitchProvider twitchProvider) : base(logger)
        {
            _twitchProvider = twitchProvider;
        }

        public override string CommandName => "twitch";

        public override string HelpSummary => "View or manage Twitch go-live notifications for desired users.";

        public override string HelpDetails => @$"/{CommandName} subs (list | add (string user) | remove (string user))

SUBCOMMANDS:
* subs list
    Returns a list of every Twitch user this server is currently subscribed to.

* subs add (user)
    Subscribes to a Twitch user (exact login name, case insenitive) to get notifications in this server.

* subs remove (user)
    Unsubscribes from an existing Twitch user's (exact login name, case insenitive) notifications in this server.";

        public override SlashCommandProperties BuildCommand()
        {
            return new SlashCommandBuilder
            {
                Description = "Manage all twitch hooks",
                DefaultMemberPermissions = GuildPermission.ManageMessages,
                Options = new[]
                {
                    new SlashCommandOptionBuilder
                    {
                        Name = "subs",
                        Description = "Manage all twitch go-live subscriptions",
                        Type = ApplicationCommandOptionType.SubCommandGroup,
                        Options = new[]
                        {
                            new SlashCommandOptionBuilder { Name = "list", Description = "View all current subscriptions", Type = ApplicationCommandOptionType.SubCommand },
                            new SlashCommandOptionBuilder
                            {
                                Name = "add",
                                Description = "Add new subscription",
                                Type = ApplicationCommandOptionType.SubCommand,
                                Options = new[]
                                {
                                    new SlashCommandOptionBuilder
                                    {
                                        Name = "user",
                                        Description = "Subscribe to a twitch user's stream events",
                                        Type = ApplicationCommandOptionType.String,
                                        IsRequired = true
                                    }
                                }.ToList()
                            },
                            new SlashCommandOptionBuilder
                            {
                                Name = "remove",
                                Description = "Remove existing subsscription",
                                Type = ApplicationCommandOptionType.SubCommand,
                                Options = new[]
                                {
                                    new SlashCommandOptionBuilder
                                    {
                                        Name = "user",
                                        Description = "Unsubscribe to a twitch user's stream events",
                                        Type = ApplicationCommandOptionType.String,
                                        IsRequired = true
                                    }
                                }.ToList()
                            }
                        }.ToList()
                    }
                }.ToList()
            }.Build();
        }

        public override Task<string> HandleCommandAsync(SlashCommandWrapper command)
        {
            Logger.LogInformation($"Twitch command executed at {DateTime.Now}");

            string commandGroup = command.Data.Options.FirstOrDefault()?.Name;
            var subCommand = commandGroup != null ? command.Data.Options.First().Options.FirstOrDefault() : null;

            // Validation
            if (subCommand == null) return Task.FromResult(command.Respond("Missing subcommand! Poke Shaosil and tell him to code better.", ephemeral: true));
            string userArg = subCommand.Options.FirstOrDefault()?.Value as string ?? string.Empty;
            if (new[] { "add, remove" }.Contains(subCommand.Name) && string.IsNullOrWhiteSpace(userArg))
                return Task.FromResult(command.Respond("Missing user argument. Please try again and actually enter something this time smh.", ephemeral: true));

			return command.DeferWithCodeTask(async () =>
            {
                switch (commandGroup ?? string.Empty)
                {
                    case "subs":
                        // Always get sub IDs and their matching usernames
                        var subs = await _twitchProvider.GetSubscriptionsAsync();
                        var uniqueIds = subs.data.Select(d => d.condition.broadcaster_user_id).Distinct().ToArray();
                        var users = subs.data.Any() ? await _twitchProvider.GetUsers(true, uniqueIds) : null;

                        switch (subCommand.Name)
                        {
                            case "list":
								if (subs.data.Count < 1)
								{
									await command.FollowupAsync($"There are no current twitch subscriptions. Use `/{CommandName} subs add [username]` to create one.");
									return;
								}

                                // Display usernames of all subscribed twitch users
                                var sb = new StringBuilder();
                                sb.AppendLine("This server is subscribed to the following twitch users:");
                                sb.AppendLine();
                                foreach (var id in uniqueIds)
                                {
                                    string matchingUser = users.data.First(u => u.id == id).display_name;
                                    sb.AppendLine($"* {matchingUser}");
                                }

								await command.FollowupAsync(sb.ToString());
								return;

                            case "add":
								// Make sure the requested user doesn't already exist
								if (users?.data.Any(u => u.login.ToLower() == userArg.Trim().ToLower()) ?? false)
								{
									await command.FollowupAsync($"User {userArg} is already in the subscriptions list. Use `/{CommandName} subs list` to view all subscriptions.");
									return;
								}

                                // Try to find the user by login
                                var newUser = await _twitchProvider.GetUsers(false, userArg);
								if (!newUser.data.Any())
								{
									await command.FollowupAsync($"No user found with the login '{userArg}'. Check the spelling and try again.");
									return;
								}

								// Attempt to subscribe
								if (!await _twitchProvider.PostSubscription(newUser.data.FirstOrDefault().id))
								{
									await command.FollowupAsync($"Received unauthorized status code while attempting to subscribe to user '{userArg}'. Poke Shaosil and tell him to code better.");
									return;
								}

								await command.FollowupAsync($"Successfully subscribed to Twitch user '{userArg}'.");
								return;

                            case "remove":
                                // Make sure the requested user exists
                                var userToUnsubscribe = users?.data.FirstOrDefault(u => u.login.ToLower() == userArg.Trim().ToLower());
								if (userToUnsubscribe == null)
								{
									await command.FollowupAsync($"User {userArg} is not currently in the subscriptions list. User `/{CommandName} subs list` to view all subscriptions.");
									return;
								}

                                // Attempt to unsubscribe
                                var deleteUsers = subs.data.Where(d => d.condition.broadcaster_user_id == userToUnsubscribe.id).ToList();
								if (!await _twitchProvider.DeleteSubscriptions(deleteUsers))
								{
									await command.FollowupAsync($"Received unauthorized status code while attempting to unsubscribe from user '{userArg}'. Poke Shaosil and tell him to code better.");
									return;
								}

                                await command.FollowupAsync($"Successfully unsubscribed from Twitch user '{userArg}'.");
								return;

                            default:
                                await command.FollowupAsync($"Unknown subcommmand type: '{subCommand.Name}'. Poke Shaosil and tell him to code better.");
								return;
                        }

                    default:
                        await command.FollowupAsync($"Unknown commmand group: '{commandGroup}'. Poke Shaosil and tell him to code better.");
						return;
                }
            });
        }
    }
}