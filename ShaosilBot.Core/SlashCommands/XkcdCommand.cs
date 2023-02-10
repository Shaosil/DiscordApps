using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Providers;
using System.Text.Json;

namespace ShaosilBot.Core.SlashCommands
{
	public class XkcdCommand : BaseCommand
    {
        private readonly HttpClient _client;

        public XkcdCommand(ILogger<XkcdCommand> logger, IHttpClientFactory httpClientFactory) : base(logger)
        {
            _client = httpClientFactory.CreateClient();
        }

        public override string CommandName => "xkcd";

        public override string HelpSummary => "Displays a random (or specified) comic from XKCD.";

        public override string HelpDetails => @$"/{CommandName} [latest] | [random] | [int comic]

SUBCOMMANDS
* latest
    Pulls the most recent comic.

* random
    Pulls a completely random comic from the first to last ever written.

* comic (int num)
    Pass a specific 1 based index of the comic you want to see. 1 = first, and so on.";

        public override SlashCommandProperties BuildCommand()
		{
            return new SlashCommandBuilder
            {
                Description = "Get the latest XKCD comic, or optionally a specific or random one!",
                Options = new[]
				{
					new SlashCommandOptionBuilder { Name = "latest", Type = ApplicationCommandOptionType.SubCommand, Description = $"Pulls the latest and greatest comic." },
					new SlashCommandOptionBuilder { Name = "random", Type = ApplicationCommandOptionType.SubCommand, Description = $"Pulls a random comic. May the odds be ever in your favor." },
					new SlashCommandOptionBuilder
                    {
                        Name = "comic", Type = ApplicationCommandOptionType.SubCommand, Description = "Get a specific comic",
                        Options = new[]
                        {
                            new SlashCommandOptionBuilder
                            {
                                Name = "num",
                                Type = ApplicationCommandOptionType.Integer,
                                MinValue = 0,
                                Description = "The 1 based index number of the comic to pull"
                            }
                        }.ToList()
                    },
				}.ToList()
            }.Build();
        }

        public override Task<string> HandleCommand(SlashCommandWrapper command)
        {
            // Get current comic asynchronously and defer the response for later
            return command.DeferWithCodeTask(async () =>
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
					var latest = command.Data.Options.FirstOrDefault(o => o.Name == "latest");
					var random = command.Data.Options.FirstOrDefault(o => o.Name == "random");
					var comicNum = command.Data.Options.FirstOrDefault(o => o.Name == "comic")?.Options.FirstOrDefault(o => o.Name == "num");
					int requestedNum = latest != null ? 0 : random != null ? Random.Shared.Next(1, data.num + 1) : int.Parse(comicNum.Value.ToString()); // Null = get random
					string description;

					if (requestedNum > 0)
					{
						// Update data with specified comic if one was requested
						response = await _client.GetAsync($"https://xkcd.com/{requestedNum}/info.0.json");
						if (!response.IsSuccessStatusCode) throw new Exception();
						data = JsonSerializer.Deserialize<Xkcd>(await response.Content.ReadAsStringAsync());
					}

					description = $"{command.User.Mention} used {(latest != null ? "latest" : random != null ? "random" : $"#{requestedNum}")}! It's super effective!";

					// Now call the image link and return the stream as a followup file
					response = await _client.GetAsync(data.img);
					if (!response.IsSuccessStatusCode) throw new Exception();
					await command.FollowupAsync(embed: new EmbedBuilder { ImageUrl = data.img, Title = $"{data.title} (#{data.num})", Description = description }.Build());
				}
				catch
				{
					await command.FollowupAsync("Unable to download specified xkcd data. Please verify your request.");
				}
			});
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