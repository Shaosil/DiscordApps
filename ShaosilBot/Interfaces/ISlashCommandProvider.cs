using Discord;
using Discord.WebSocket;
using ShaosilBot.SlashCommands;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ShaosilBot.Interfaces
{
    public interface ISlashCommandProvider
    {
        IReadOnlyDictionary<string, SlashCommandProperties> CommandProperties { get; }

        Task BuildGuildCommands(DiscordSocketClient client);
        BaseCommand GetSlashCommandHandler(string name);
    }
}