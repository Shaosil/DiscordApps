using Discord;
using ShaosilBot.SlashCommands;

namespace ShaosilBot.Tests.Models
{
	public class DiscordInteraction : SerializableModel
    {
        public InteractionType type { get; private set; }
		public User user { get; private set; } = new();
        public Data data { get; private set; }

        public static DiscordInteraction CreatePing() => new DiscordInteraction { type = InteractionType.Ping };

        public static DiscordInteraction CreateSlash(BaseCommand command)
        {
			return new DiscordInteraction
			{
				type = InteractionType.ApplicationCommand,
				data = new Data(command.CommandName, ApplicationCommandType.Slash)
            };
        }

        public class Data
        {
			public string id { get; } = Random.Shared.NextULong().ToString();
            public string guild_id { get; private set; }
            public string name { get; private set; }
            public ApplicationCommandType type { get; private set; }

			public Data (string nameArg, ApplicationCommandType typeArg)
			{
				name = nameArg;
				type = typeArg;
			}
        }
        
        public class User
        {
            public string? id { get; private set; }
            public string? username { get; private set; }

			public User (string? idArg = null, string? usernameArg = null)
			{
				id = idArg;
				username = usernameArg;
			}
        }
    }
}