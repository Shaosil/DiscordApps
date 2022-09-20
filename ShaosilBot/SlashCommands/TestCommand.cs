using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class TestCommand : BaseCommand
    {
        public TestCommand(ILogger<TestCommand> logger) : base(logger) { }

        public override string CommandName => "test-command";

        public override string HelpSummary => "Needs no introduction.";

        public override string HelpDetails => "Needs no explanation.";

        public override SlashCommandProperties BuildCommand()
        {
            return new SlashCommandBuilder
            {
                Description = "Getting closer to world domination",
                DefaultMemberPermissions = GuildPermission.Administrator
            }.Build();
        }

        public override Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            Logger.LogInformation($"Test Command executed at {DateTime.Now}");
            return Task.FromResult(command.Respond("Test command successful", ephemeral: true));
        }
    }
}