using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;

namespace ShaosilBot.Core.SlashCommands
{
	public class CatFactsCommand : BaseCommand
	{
		public const string CatFactsFileName = "CatFacts.txt";

		private readonly IFileAccessHelper _fileAccessHelper;

		public CatFactsCommand(ILogger<CatFactsCommand> logger, IFileAccessHelper fileAccessHelper) : base(logger)
		{
			_fileAccessHelper = fileAccessHelper;
		}

		public override string CommandName => "cat-fact";

		public override string HelpSummary => "Pulls a random cat fact out of the database of many cat facts.";

		public override string HelpDetails => $"/{CommandName}\n\nSimply use the command to hear a cat fact! There's nothin' to it, as they say.";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder { Description = "Meows a random cat fact to everyone." }.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper command)
		{
			var _catFacts = (_fileAccessHelper.LoadFileText(CatFactsFileName)).Split(Environment.NewLine);
			return Task.FromResult(command.Respond(_catFacts[Random.Shared.Next(_catFacts.Length)]));
		}
	}
}