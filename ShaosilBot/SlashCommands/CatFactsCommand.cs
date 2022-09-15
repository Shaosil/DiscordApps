using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Singletons;
using System;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class CatFactsCommand : BaseCommand
    {
        private readonly DataBlobProvider _blobProvider;

        public CatFactsCommand(ILogger logger, DataBlobProvider blobProvider) : base(logger)
        {
            _blobProvider = blobProvider;
        }

        public override async Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            var _catFacts = (await _blobProvider.GetBlobTextAsync("CatFacts.txt")).Split(Environment.NewLine);
            return _catFacts[Random.Shared.Next(_catFacts.Length)];
        }
    }
}