using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class XkcdCommand : BaseCommand
    {
        private readonly HttpClient _client;

        public XkcdCommand(ILogger logger, HttpClient client) : base(logger)
        {
            _client = client;
        }

        public override async Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            // Get current comic asynchronously and defer the response for later
            _ = Task.Run(async () =>
            {
                HttpResponseMessage response;
                Xkcd data;

                // Initial ping
                try
                {
                    response = await _client.GetAsync("https://xkcd.com/info.0.json");
                    if (response.IsSuccessStatusCode)
                        data = JsonSerializer.Deserialize<Xkcd>(await response.Content.ReadAsStringAsync());
                    else
                        throw new Exception();
                }
                catch
                {
                    await command.FollowupAsync("Unable to download initial xkcd data. Please try again later.");
                    return;
                }

                try
                {
                    var option = command.Data.Options.FirstOrDefault(o => o.Name == "comic-num");
                    int requestedNum = option == null ? Random.Shared.Next(1, data.num + 1) : int.Parse(option.Value.ToString()); // Null = get random
                    string description;

                    if (requestedNum > 0)
                    {
                        // Update data with specified comic if one was requested
                        response = await _client.GetAsync($"https://xkcd.com/{requestedNum}/info.0.json");
                        if (!response.IsSuccessStatusCode) throw new Exception();
                        data = JsonSerializer.Deserialize<Xkcd>(await response.Content.ReadAsStringAsync());

                        description = $"{command.User.Mention} used {(option == null ? "random" : $"#{requestedNum}")}! It's super effective!";
                    }
                    else
                        description = $"{command.User.Mention} used current! It's super effective!";

                    // Now call the image link and return the stream as a followup file
                    response = await _client.GetAsync(data.img);
                    if (!response.IsSuccessStatusCode) throw new Exception();
                    await command.FollowupAsync(embed: new EmbedBuilder { ImageUrl = data.img, Title = $"{data.title} (#{data.num})", Description = description }.Build());
                }
                catch
                {
                    await command.FollowupAsync("Unable to download specified xkcd data. Please verify your request.");
                    return;
                }
            });

            return await Task.FromResult(command.Defer());
        }

        public class Xkcd
        {
            public string month { get; set; }
            public int num { get; set; }
            public string link { get; set; }
            public string year { get; set; }
            public string news { get; set; }
            public string safe_title { get; set; }
            public string transcript { get; set; }
            public string alt { get; set; }
            public string img { get; set; }
            public string title { get; set; }
            public string day { get; set; }
        }
    }
}