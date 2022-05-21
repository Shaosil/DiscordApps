using Discord.Rest;
using Microsoft.Extensions.Logging;
using ShaosilBot.Providers;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class CatFactsCommand : BaseCommand
    {
        private readonly CatFactsProvider _catFactsProvider;

        public CatFactsCommand(ILogger logger, CatFactsProvider catFactsProvider) : base(logger)
        {
            _catFactsProvider = catFactsProvider;
        }

        public override async Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            // Get current subscribers asynchronously and add this one to the list if they do not exist
            _ = Task.Run(async () =>
            {
                var currentSubscribers = await _catFactsProvider.GetSubscribersAsync(true);
                if (!currentSubscribers.Any(s => s.ID == command.User.Id))
                {
                    currentSubscribers.Add(new CatFactsProvider.Subscriber { ID = command.User.Id, FriendlyName = command.User.Username, DateSubscribed = DateTimeOffset.Now, TimesUnsubscribed = 0 });
                    await _catFactsProvider.UpdateSubscribersAsync(currentSubscribers);
                }
            });

            return command.Respond(await _catFactsProvider.GetRandomCatFact());
        }
    }
}