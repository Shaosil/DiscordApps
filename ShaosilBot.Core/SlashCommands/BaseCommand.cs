using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Providers;

namespace ShaosilBot.Core.SlashCommands
{
	public abstract class BaseCommand
	{
		protected ILogger Logger { get; }

		public BaseCommand(ILogger logger)
		{
			Logger = logger;
		}

		public abstract string CommandName { get; }

		public abstract string HelpSummary { get; }

		public abstract string HelpDetails { get; }

		public abstract SlashCommandProperties BuildCommand();

		public abstract Task<string> HandleCommand(SlashCommandWrapper cmdWrapper);
	}
}