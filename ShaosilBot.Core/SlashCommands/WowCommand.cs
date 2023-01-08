using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Providers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaosilBot.Core.SlashCommands
{
	public class WowCommand : BaseCommand
    {
        private readonly HttpClient _client;
		private readonly IConfiguration _configuration;

		public WowCommand(ILogger<WowCommand> logger, IHttpClientFactory httpClientFactory, IConfiguration configuration) : base(logger)
        {
            _client = httpClientFactory.CreateClient();
			_configuration = configuration;
		}

        public override string CommandName => "wow";

        public override string HelpSummary => "Wow (featuring Owen Wilson).";

        public override string HelpDetails => "Picks a random scene from a giant list of every time Owen Wilson has ever said 'wow' in a movie and clips it.";

        public override SlashCommandProperties BuildCommand()
        {
            return new SlashCommandBuilder { Description = "Wow." }.Build();
        }

        public override Task<string> HandleCommand(SlashCommandWrapper command)
		{
			// Immediately acknowledge, and let the async task above decide how to followup
			return command.DeferWithCodeTask(async () =>
            {
                string url = _configuration["RandomWowURL"];
                var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();

                    try
                    {
                        var responseObj = JsonSerializer.Deserialize<WowResponse[]>(content).FirstOrDefault();
                        string description = $"*{responseObj.movie} **wow** {responseObj.current_wow_in_movie} of {responseObj.total_wows_in_movie}*";
						await command.FollowupWithFileAsync(await _client.GetStreamAsync(responseObj.video._360p), Path.GetFileName(responseObj.video._360p), description);
                    }
                    catch
                    {
						await command.FollowupAsync("Error parsing JSON from wow API (not ShaosilBot's fault).");
                    }
                }
                else
					await command.FollowupAsync("Error retrieving response from wow API (not ShaosilBot's fault).");
            });
        }

        private class WowResponse
        {
            public string movie { get; set; }
            public int year { get; set; }
            public DateTime release_date { get; set; }
            public string director { get; set; }
            public string character { get; set; }
            public string movie_duration { get; set; }
            public string timestamp { get; set; }
            public string full_line { get; set; }
            public int current_wow_in_movie { get; set; }
            public int total_wows_in_movie { get; set; }
            public string poster { get; set; }
            public Video video { get; set; }
            public string audio { get; set; }

            public class Video
            {
                [JsonPropertyName("1080p")]
                public string _1080p { get; set; }
                [JsonPropertyName("720p")]
                public string _720p { get; set; }
                [JsonPropertyName("480p")]
                public string _480p { get; set; }
                [JsonPropertyName("360p")]
                public string _360p { get; set; }
            }
        }
    }
}