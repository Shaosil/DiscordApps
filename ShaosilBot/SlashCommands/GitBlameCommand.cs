using Discord.Rest;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class GitBlameCommand : BaseCommand
    {
        public GitBlameCommand(ILogger logger) : base(logger) { }

        public override Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            // Temp - Store the possible responses here until the command is more customizable
            var tempUsers = new Dictionary<string, string[]> 
            {
                { "<@!421853674709712896>", new[] { "https://i.imgur.com/bMReYaR.jpg", "https://i.imgur.com/WxR0p0x.jpg", "https://i.imgur.com/J7VVgSi.jpg", "https://i.imgur.com/3gF0vc4.jpg", "https://i.imgur.com/cUqgBzL.jpg" } },
                { "<@!369997507373301760>", new[] { "https://i.imgur.com/jCIgXOU.jpg", "https://i.imgur.com/ftmlpAJ.jpg", "https://i.imgur.com/A73raTH.jpg", "https://i.imgur.com/NaP4yVc.jpg", "https://i.imgur.com/0ba8AKt.jpg" } },
                { "<@!392127164570664962>", new[] { "https://i.imgur.com/nfVC3k6.jpg", "https://i.imgur.com/bkMFgyU.jpg", "https://i.imgur.com/bcJaksW.jpg", "https://i.imgur.com/bl3TmLE.jpg", "https://i.imgur.com/ucfYKFW.jpg" } },
                { "<@!291343459565043712>", new[] { "https://i.imgur.com/wejgFBH.jpg", "https://i.imgur.com/v3IVTPq.jpg", "https://i.imgur.com/aVEIuPi.jpg", "https://i.imgur.com/g4w3SvF.jpg", "https://i.imgur.com/Ba4l5xw.jpg" } }
            };
            var tempResponses = new[]
            {
                "Come on {USER}, what were you thinking dude?",
                "{USER}, I literally can't even...",
                "Hey guys, {USER} did a bad thing.",
                "Seriously {USER}? You just had to go there.",
                "Hey {USER}! The odds have ruled against you. Better luck next time.",
                "Why, {USER}, just why?",
                "This is obviously {USER}'s fault."
            };

            string winner = tempUsers.Keys.ElementAt(Random.Shared.Next(tempUsers.Count));
            string response = tempResponses[Random.Shared.Next(tempResponses.Length)].Replace("{USER}", winner);
            string image = tempUsers[winner][Random.Shared.Next(tempUsers[winner].Length)];

            return Task.FromResult(command.Respond($"{response}\n\n{image}"));
        }
    }
}