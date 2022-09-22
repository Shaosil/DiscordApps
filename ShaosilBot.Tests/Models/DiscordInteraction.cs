using Discord;
using ShaosilBot.SlashCommands;

namespace ShaosilBot.Tests.Models
{
    public class DiscordInteraction : SerializableModel
    {
        public InteractionType type { get; set; }
        public string channel_id { get; set; }
        public User user { get; set; } = new();
        public Data data { get; set; } = new();

        public static DiscordInteraction CreatePing() => new DiscordInteraction { type = InteractionType.Ping };

        public static DiscordInteraction CreateSlash(BaseCommand command)
        {
            return new DiscordInteraction
            {
                type = InteractionType.ApplicationCommand,
                data = new Data
                {
                    type = ApplicationCommandType.Slash,
                    name = command.CommandName
                }
            };
        }

        public class Data
        {
            public string id { get; set; }
            public string guild_id { get; set; }
            public string name { get; set; }
            public ApplicationCommandType type { get; set; }
        }
        
        public class User
        {
            public string id { get; set; }
            public string username { get; set; }
        }
    }
}
