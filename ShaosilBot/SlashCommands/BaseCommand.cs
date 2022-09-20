using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public abstract class BaseCommand
    {
        protected ILogger Logger { get; }

        public BaseCommand(ILogger logger)
        {
            Logger = logger;
        }

        public abstract string CommandName { get; }

        public abstract string HelpSummary { get; }

        public abstract string HelpDetails { get; }

        public abstract SlashCommandProperties BuildCommand();

        public abstract Task<string> HandleCommandAsync(RestSlashCommand command);
    }
}