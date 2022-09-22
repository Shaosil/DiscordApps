using Discord;

namespace ShaosilBot.Tests.Models
{
    public class DiscordInteractionResponse : SerializableModel
    {
        public InteractionResponseType type { get; set; }

        public Data data { get; set; }

        public class Data
        {
            public string content { get; set; }
        }
    }
}