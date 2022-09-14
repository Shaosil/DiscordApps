using Discord.Rest;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class Magic8BallCommand : BaseCommand
    {
        private readonly string[] _choices = new[]
        {
            "It is certain.",
            "It is decidedly so.",
            "Without a doubt.",
            "Yes definitely.",
            "You may rely on it.",
            "As I see it, yes.",
            "Most likely.",
            "Outlook good.",
            "Yes.",
            "Signs point to yes.",
            "Reply hazy, try again.",
            "Ask again later.",
            "Better not tell you now.",
            "Cannot predict now.",
            "Concentrate and ask again.",
            "Don't count on it.",
            "My reply is no.",
            "My sources say no.",
            "Outlook not so good.",
            "Very doubtful."
        };

        public Magic8BallCommand(ILogger logger) : base(logger) { }

        public override Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            if (command.Data.Options.Count != 1 || string.IsNullOrWhiteSpace(command.Data.Options.First().Value as string))
                return Task.FromResult(command.Respond("Invalid question specified. Try again, but better.", ephemeral: true));

            var sb = new StringBuilder();
            sb.AppendLine($"{command.User.Mention} shakes a magic 8 ball and asks the question: '{command.Data.Options.First().Value}'.");
            sb.AppendLine();
            sb.AppendLine("The 8 ball's response:");
            sb.Append($"*{_choices[Random.Shared.Next(_choices.Length)]}*");
            return Task.FromResult(command.Respond(sb.ToString()));
        }
    }
}