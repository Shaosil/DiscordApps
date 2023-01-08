using Discord;
using ShaosilBot.Core.SlashCommands;

namespace ShaosilBot.Core.Interfaces
{
	public interface ISlashCommandProvider
    {
        IReadOnlyDictionary<string, SlashCommandProperties> CommandProperties { get; }

        Task BuildGuildCommands();
        BaseCommand GetSlashCommandHandler(string name);
    }
}