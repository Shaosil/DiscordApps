using Discord.Rest;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShaosilBot.SlashCommands
{
    public class RandomCommand : BaseCommand
    {
        public RandomCommand(ILogger logger) : base(logger) { }

        public override Task<string> HandleCommandAsync(RestSlashCommand command)
        {
            string questionVal = command.Data.Options.FirstOrDefault(o => o.Name == "question")?.Value as string;
            var choicesGiven = command.Data.Options.Where(o => o.Name.StartsWith("choice")).ToList();

            // If the number of options provided is exactly one, return an ephemeral wrist slap
            if (choicesGiven.Count == 1)
                return Task.FromResult(command.Respond("You can't just give me a single option. Either give me none for a coin flip, or multiple to let me pick from a list.", ephemeral: true));

            // Coin flip
            if (choicesGiven.Count == 0)
            {
                bool isHeads = Random.Shared.Next(0, 2) == 0;
                if (string.IsNullOrWhiteSpace(questionVal))
                    return Task.FromResult(command.Respond($"I've flipped a coin and it came up **{(isHeads ? "heads" : "tails")}**!"));
                else
                    return Task.FromResult(command.Respond($"{questionVal}\n\nCoin flip result: **{(isHeads ? "heads" : "tails")}**!"));
            }

            // List picker
            int selectionIndex = Random.Shared.Next(0, command.Data.Options.Count);
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(questionVal))
                sb.AppendLine(questionVal);
            else
                sb.AppendLine($"I have chosen #{selectionIndex + 1} from the following options:");
            sb.AppendLine();
            for (int i = 0; i < command.Data.Options.Count; i++)
            {
                if (i == selectionIndex) sb.Append("**");
                sb.Append($"{(i + 1).ToString().PadLeft(2)}) {command.Data.Options.ElementAt(i).Value}");
                if (i == selectionIndex) sb.Append("**");
                if (i < command.Data.Options.Count - 1) sb.AppendLine();
            }
            return Task.FromResult(command.Respond(sb.ToString()));
        }
    }
}