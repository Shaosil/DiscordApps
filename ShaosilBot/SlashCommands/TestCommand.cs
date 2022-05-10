using Discord.Rest;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class TestCommand : BaseCommand
    {
        public TestCommand(ILogger logger) : base(logger) { }

        public override Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            Logger.LogInformation($"Test Command executed at {DateTime.Now}");
            return Task.FromResult(command.Respond("Test command successful", ephemeral: true));
        }
    }
}