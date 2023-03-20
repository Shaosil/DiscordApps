using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Providers;
using System.Text;

namespace ShaosilBot.Core.SlashCommands
{
	public class RandomCommand : BaseCommand
	{
		public RandomCommand(ILogger<BaseCommand> logger) : base(logger) { }

		public override string CommandName => "random";

		public override string HelpSummary => "Flips a coin or randomly choose an item from a list, depending on what arguments you provide.";

		public override string HelpDetails => @$"/{CommandName} [string question] [string choice1... choice20]

Passing no arguments will simply flip a coin for a quick heads or tails decision.

OPTIONAL ARGUMENTS:
* question:
    Adds some flavor text to the choices. Eg: ""What color shirt should I wear today?""

* choice1 - choice20:
    Specifying two or more choices will make this command pick from a list of the choices you supplied instead of flipping a coin.";

		public override SlashCommandProperties BuildCommand()
		{
			var randomChoices = new List<SlashCommandOptionBuilder>();
			for (int i = 1; i <= 20; i++)
				randomChoices.Add(new SlashCommandOptionBuilder { Name = $"choice{i}", Description = "A chooseable option", Type = ApplicationCommandOptionType.String });

			return new SlashCommandBuilder
			{
				Description = $"Flips a coin, or picks a random item from a list of up to {randomChoices.Count} provided choices.",
				Options = new[]
				{
					new SlashCommandOptionBuilder { Name = "question", Description = "An optional statement describing your specified choices", Type = ApplicationCommandOptionType.String }
				}.Concat(randomChoices).ToList()
			}.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper command)
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
			int selectionIndex = Random.Shared.Next(0, choicesGiven.Count);
			var sb = new StringBuilder();
			if (!string.IsNullOrWhiteSpace(questionVal))
				sb.AppendLine(questionVal);
			else
				sb.AppendLine($"I have chosen #{selectionIndex + 1} from the following options:");
			sb.AppendLine();
			for (int i = 0; i < choicesGiven.Count; i++)
			{
				if (i == selectionIndex) sb.Append("**");
				sb.Append($"{(i + 1).ToString().PadLeft(2)}) {choicesGiven[i].Value}");
				if (i == selectionIndex) sb.Append("**");
				if (i < choicesGiven.Count - 1) sb.AppendLine();
			}
			return Task.FromResult(command.Respond(sb.ToString()));
		}
	}
}