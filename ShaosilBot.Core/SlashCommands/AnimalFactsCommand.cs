using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using System.Reflection;

namespace ShaosilBot.Core.SlashCommands
{
	public class AnimalFactsCommand : BaseCommand
	{
		public const string CatFactsFileName = "CatFacts.txt";
		public const string DogFactsFileName = "DogFacts.txt";

		private readonly IFileAccessHelper _fileAccessHelper;

		public AnimalFactsCommand(ILogger<BaseCommand> logger, IFileAccessHelper fileAccessHelper) : base(logger)
		{
			_fileAccessHelper = fileAccessHelper;
		}

		public override string CommandName => "animal-fact";

		public override string HelpSummary => "Pulls a random fact out of the database of many cat and dog facts.";

		public override string HelpDetails => $"/{CommandName} (animal type)\n\nSimply use the command to hear a fact about the animal of your choosing! There's nothin' to it, as they say.";

		public override SlashCommandProperties BuildCommand()
		{
			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new List<SlashCommandOptionBuilder>
				{
					new SlashCommandOptionBuilder
					{
						Name = "type",
						Description = "The type of animal fact to see",
						Type = ApplicationCommandOptionType.String,
						Choices = new List<ApplicationCommandOptionChoiceProperties>
						{
							new ApplicationCommandOptionChoiceProperties { Name = "Cat", Value = CatFactsFileName },
							new ApplicationCommandOptionChoiceProperties { Name = "Dog", Value = DogFactsFileName }
						}
					}
				}
			}.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper command)
		{
			// If no type was specified, choose randomly between all filenames
			string? fileName = command.Data.Options.FirstOrDefault()?.Value.ToString();
			if (string.IsNullOrWhiteSpace(fileName))
			{
				var allProps = GetType().GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(f => f.Name.EndsWith("FileName")).ToList();
				int randIndex = Random.Shared.Next(allProps.Count);
				fileName = allProps[randIndex].GetValue(null)!.ToString();
			}

			var facts = (_fileAccessHelper.LoadFileText(fileName)).Split(Environment.NewLine);
			string emoji = fileName == DogFactsFileName ? ":dog:" : ":cat:";
			return Task.FromResult(command.Respond($"{emoji} {facts[Random.Shared.Next(facts.Length)]}"));
		}
	}
}