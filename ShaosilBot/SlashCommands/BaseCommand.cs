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

        public abstract Task<string> HandleCommandAsync(RestSlashCommand command);
    }
}
