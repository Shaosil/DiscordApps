using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Singletons;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class GitBlameCommand : BaseCommand
    {
        private readonly HttpClient _httpClient;
        private readonly DataBlobProvider _dataBlobProvider;

        public GitBlameCommand(ILogger logger, HttpClient httpClient, DataBlobProvider dataBlobProvider) : base(logger)
        {
            _httpClient = httpClient;
            _dataBlobProvider = dataBlobProvider;
        }

        public override async Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            var subscribers = JsonSerializer.Deserialize<List<Subscriber>>(await _dataBlobProvider.GetBlobTextAsync("GitBlameables.json"));
            var targetUser = command.Data.Options.FirstOrDefault(o => o.Name == "target-user")?.Value as RestGuildUser;
            bool parsedFunctions = int.TryParse(command.Data.Options.FirstOrDefault(o => o.Name == "functions")?.Value.ToString(), out var functions);

            // Functions are handled by themselves
            if (parsedFunctions && command.User is RestGuildUser requestor)
            {
                if (targetUser == null)
                    targetUser = requestor;

                switch (functions)
                {
                    case 0: // Toggle subscription
                        var highestRequestorRole = command.Guild.Roles.Where(r => requestor.RoleIds.Any(ur => ur == r.Id)).OrderByDescending(r => r.Position).FirstOrDefault();
                        var highestTargetRole = command.Guild.Roles.Where(r => targetUser.RoleIds.Any(ur => ur == r.Id)).OrderByDescending(r => r.Position).FirstOrDefault();

                        // Only allow subscription edits to a target user if the requestor is administrator or their highest role is greater than the target's highest role
                        if (targetUser.Id == requestor.Id || !targetUser.GuildPermissions.Administrator && (requestor.GuildPermissions.Administrator || highestRequestorRole.Position > highestTargetRole.Position))
                        {
                            int oldCount = subscribers.Count;

                            // Edit subscribers blob
                            if (subscribers.Any(s => s.ID == targetUser.Id))
                                subscribers.Remove(subscribers.First(s => s.ID == targetUser.Id));
                            else
                                subscribers.Add(new Subscriber { ID = targetUser.Id, FriendlyName = targetUser.Username });

                            await _dataBlobProvider.SaveBlobTextAsync("GitBlameables.json", JsonSerializer.Serialize(subscribers, new JsonSerializerOptions { WriteIndented = true }));
                            return command.Respond($"{targetUser.Username} successfully {(oldCount < subscribers.Count ? "added" : "removed")} as a blameable");
                        }
                        else
                        {
                            return command.Respond($"You do not have sufficient permissions to edit {targetUser.Username}'s subscription. Ask someone more important than you to do it.", ephemeral: true);
                        }

                    case 1: // List blameables
                        return command.Respond($"Current blameables:\n\n{string.Join("\n", subscribers.Select(s => "* " + s.FriendlyName))}");
                }
            }

            // Run blame functionality asynchronously
            _ = Task.Run(async () =>
            {
                // Get a list of all images in my gitblame album and pick a random one
                string selectedImage;
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, Environment.GetEnvironmentVariable("ImgurGitBlameAlbum"));
                    req.Headers.Add("Authorization", $"Client-ID {Environment.GetEnvironmentVariable("ImgurClientID")}");
                    var albumResponse = await (await _httpClient.SendAsync(req)).Content.ReadAsStringAsync();
                    var allImages = JsonSerializer.Deserialize<ImgurRoot>(albumResponse).Images;

                    selectedImage = allImages[Random.Shared.Next(allImages.Count)].link;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error fetching images from Imgur");
                    return await command.FollowupAsync("Error fetching images from Imgur");
                }

                // Get a random response line from the blob
                var responses = (await _dataBlobProvider.GetBlobTextAsync("GitBlameResponses.txt")).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                string response = responses[Random.Shared.Next(responses.Length)];

                var channel = await command.Guild.GetChannelAsync(command.Channel.Id);
                if (targetUser == null)
                {
                    // Blame one of the current subscribers, removing anyone who can not view the current channel
                    subscribers = subscribers.Where(s => command.Guild.GetUserAsync(s.ID).GetAwaiter().GetResult().GetPermissions(channel).ViewChannel).ToList();
                    if (subscribers.Count == 0) return await command.FollowupAsync("There are no blameable users who have access to this channel!");

                    ulong randomId = subscribers[Random.Shared.Next(subscribers.Count)].ID;
                    targetUser = await command.Guild.GetUserAsync(randomId);
                }
                else
                {
                    // Custom responses based on our findings on targetUser
                    if (!targetUser.GetPermissions(channel).ViewChannel)
                        return await command.FollowupAsync($"{command.User.Mention} tried to blame {targetUser.Mention}, but that user was not found in this channel, so {command.User.Mention} is to blame!");
                    if (targetUser.Id == command.User.Id)
                        return await command.FollowupAsync($"{command.User.Mention} has rightfully and humbly blamed themselves for the latest wrongdoing. Good on them.");

                    // Notify everyone they specified a person
                    response += "\n\n* *Targeted*";
                }

                return await command.FollowupAsync($"{response.Replace("{USER}", targetUser.Mention)}\n\n{selectedImage}");
            });

            // Immediately return a defer, respond using the task above
            return command.Defer();
        }

        public class Subscriber
        {
            public ulong ID { get; set; }

            public string FriendlyName { get; set; }
        }

        private class ImgurRoot
        {
            [JsonPropertyName("data")]
            public List<ImgurImage> Images { get; set; }

            public class ImgurImage
            {
                public string id { get; set; }
                public string title { get; set; }
                public string description { get; set; }
                public int datetime { get; set; }
                public string type { get; set; }
                public bool animated { get; set; }
                public int width { get; set; }
                public int height { get; set; }
                public int size { get; set; }
                public int views { get; set; }
                public int bandwidth { get; set; }
                public string vote { get; set; }
                public bool favorite { get; set; }
                public bool? nsfw { get; set; }
                public string section { get; set; }
                public string account_url { get; set; }
                public string account_id { get; set; }
                public bool is_ad { get; set; }
                public bool in_most_viral { get; set; }
                public bool has_sound { get; set; }
                public List<string> tags { get; set; }
                public int ad_type { get; set; }
                public string ad_url { get; set; }
                public string edited { get; set; }
                public bool in_gallery { get; set; }
                public string link { get; set; }
                public string gifv { get; set; }
                public string mp4 { get; set; }
                public int? mp4_size { get; set; }
                public bool? looping { get; set; }
            }
        }
    }
}