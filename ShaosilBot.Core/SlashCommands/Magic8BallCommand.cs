﻿using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Providers;
using ShaosilBot.Core.SlashCommands;
using System.Text;

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

		public Magic8BallCommand(ILogger<BaseCommand> logger) : base(logger) { }

		public override string CommandName => "magic8ball";

		public override string HelpSummary => "Ask a question and the 8 ball will randomly choose a traditional 8-ball answer.";

		public override string HelpDetails => @$"/{CommandName} (string question)

REQUIRED ARGUMENTS:
* question:
    The question to ask the 8-ball. Try to provide simple yes or no questions for the most clarity.";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new[]
				{
					new SlashCommandOptionBuilder { Name = "question", Type = ApplicationCommandOptionType.String, Description = "Ask me a question.", IsRequired = true }
				}.ToList()
			}.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper cmdWrapper)
		{
			if (cmdWrapper.Command.Data.Options.Count != 1 || string.IsNullOrWhiteSpace(cmdWrapper.Command.Data.Options.First().Value as string))
				return Task.FromResult(cmdWrapper.Respond("Invalid question specified. Try again, but better.", ephemeral: true));

			var sb = new StringBuilder();
			sb.AppendLine($"{cmdWrapper.Command.User.Mention} shakes a magic 8 ball and asks the question: '{cmdWrapper.Command.Data.Options.First().Value}'.");
			sb.AppendLine();
			sb.AppendLine("The 8 ball's response:");
			sb.Append($"*{_choices[Random.Shared.Next(_choices.Length)]}*");
			return Task.FromResult(cmdWrapper.Respond(sb.ToString()));
		}
	}
}