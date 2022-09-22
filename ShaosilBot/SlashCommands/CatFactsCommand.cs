using Discord;
using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Interfaces;
using ShaosilBot.Singletons;
using System;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class CatFactsCommand : BaseCommand
    {
        private readonly IDataBlobProvider _blobProvider;

        public CatFactsCommand(ILogger<CatFactsCommand> logger, IDataBlobProvider blobProvider) : base(logger)
        {
            _blobProvider = blobProvider;
        }

        public override string CommandName => "cat-fact";

        public override string HelpSummary => "Pulls a random cat fact out of the database of many cat facts.";

        public override string HelpDetails => $"/{CommandName}\n\nSimply use the command to hear a cat fact! There's nothin' to it, as they say.";

        public override SlashCommandProperties BuildCommand()
        {
            return new SlashCommandBuilder { Description = "Meows a random cat fact to everyone." }.Build();
        }

        public override async Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            var _catFacts = (await _blobProvider.GetBlobTextAsync("CatFacts.txt")).Split(Environment.NewLine);
            return _catFacts[Random.Shared.Next(_catFacts.Length)];
        }
    }
}