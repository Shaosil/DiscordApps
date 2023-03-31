using Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Singletons;
using ShaosilBot.Core.SlashCommands;
using System.Reflection;

namespace ShaosilBot.Core.Providers
{
	public class SlashCommandProvider : ISlashCommandProvider
	{
		private readonly ILogger<SlashCommandProvider> _logger;
		private readonly IServiceProvider _serviceProvider;
		private Dictionary<string, SlashCommandProperties> _commandProperties = new Dictionary<string, SlashCommandProperties>();
		private readonly Dictionary<string, Type> _commandToTypeMappings = new Dictionary<string, Type>();

		private bool _allCommandsBuilt = false;

		public IReadOnlyDictionary<string, SlashCommandProperties> CommandProperties => _commandProperties;

		public SlashCommandProvider(ILogger<SlashCommandProvider> logger, IServiceProvider serviceProvider)
		{
			_logger = logger;
			_serviceProvider = serviceProvider;
		}

		public async Task BuildGuildCommands()
		{
			var guilds = DiscordSocketClientProvider.Client.Guilds;

			// Store each type's command properties by calling BuildCommand on each type. Build HelpCommand last so it has access to everything else
			var derivedTypes = Assembly.GetExecutingAssembly().DefinedTypes.Where(t => t.BaseType == typeof(BaseCommand)).ToList();
			var scope = _serviceProvider.CreateScope();
			foreach (var derivedType in derivedTypes.Where(t => t != typeof(HelpCommand)).Concat(derivedTypes.Where(t => t == typeof(HelpCommand))))
			{
				// Store the types to command names for the service locator
				var instance = scope.ServiceProvider.GetService(derivedType) as BaseCommand;
				_commandToTypeMappings[instance.CommandName] = derivedType;

				// Store the properties to command names
				var builtCommand = instance.BuildCommand();
				builtCommand.Name = instance.CommandName;
				_commandProperties[instance.CommandName] = builtCommand;
			}

			foreach (var guild in guilds)
			{
				var existingCommands = (await guild.GetApplicationCommandsAsync()).Where(c => c.Type == ApplicationCommandType.Slash).ToList();

				// Delete any commands that are no longer defined
				foreach (var existingCommand in existingCommands.Where(c => !_commandProperties.Values.Any(b => b.Name.GetValueOrDefault() == c.Name)))
				{
					await existingCommand.DeleteAsync();
				}

				// Create/update new and existing commands
				foreach (var newOrExistingCommand in _commandProperties.Values)
				{
					var existingMatch = existingCommands.FirstOrDefault(c => c.Name == newOrExistingCommand.Name.GetValueOrDefault());

					if (existingMatch != null && !CommandsEqual(existingMatch, newOrExistingCommand))
					{
						// Easier to delete and re-add instead of calling modify and manually setting properties
						await existingMatch.DeleteAsync();
						existingMatch = null;
					}

					if (existingMatch == null)
					{
						// Create
						await guild.CreateApplicationCommandAsync(newOrExistingCommand);
					}
				}
			}

			_allCommandsBuilt = true;
		}

		private bool CommandsEqual(IApplicationCommand existingCommand, SlashCommandProperties newCommand)
		{
			// Compare base options
			if (existingCommand.DefaultMemberPermissions.RawValue != (ulong)newCommand.DefaultMemberPermissions.GetValueOrDefault()
				|| existingCommand.Description != newCommand.Description.GetValueOrDefault())
			{
				return false;
			}

			if (!CommandOptionListsEqual(existingCommand.Options, newCommand.Options.GetValueOrDefault()))
			{
				return false;
			}

			return true;
		}

		private bool CommandOptionListsEqual(IReadOnlyCollection<IApplicationCommandOption> existingOptions, List<ApplicationCommandOptionProperties> newOptions)
		{
			// Check length of options and compare
			int existingOptionsCount = existingOptions?.Count ?? 0;
			int newOptionsCount = newOptions?.Count ?? 0;
			if (existingOptionsCount != newOptionsCount)
			{
				return false;
			}
			else if (existingOptionsCount > 0)
			{
				var orderedExistingOptions = existingOptions.OrderBy(c => c.Name).ToList();
				var orderedNewOptions = newOptions.OrderBy(c => c.Name).ToList();

				for (int i = 0; i < orderedExistingOptions.Count; i++)
				{
					if (!CommandOptionsEqual(orderedExistingOptions[i], orderedNewOptions[i]))
					{
						return false;
					}
				}
			}

			return true;
		}

		private bool CommandOptionsEqual(IApplicationCommandOption existingOption, ApplicationCommandOptionProperties newOption)
		{
			// Compare base options
			if (existingOption.Name != newOption.Name
				|| existingOption.Type != newOption.Type
				|| existingOption.Description != newOption.Description
				|| existingOption.IsRequired.GetValueOrDefault() != newOption.IsRequired.GetValueOrDefault()
				|| existingOption.IsAutocomplete.GetValueOrDefault() != newOption.IsAutocomplete
				|| existingOption.MinLength.GetValueOrDefault() != newOption.MinLength.GetValueOrDefault()
				|| existingOption.MinValue.GetValueOrDefault() != newOption.MinValue.GetValueOrDefault()
				|| existingOption.MaxLength.GetValueOrDefault() != newOption.MaxLength.GetValueOrDefault()
				|| existingOption.MaxValue.GetValueOrDefault() != newOption.MaxValue.GetValueOrDefault())
			{
				return false;
			}

			// Compare choices
			int existingChoicesCount = existingOption.Choices?.Count ?? 0;
			int newChoicesCount = newOption.Choices?.Count ?? 0;
			if (existingChoicesCount != newChoicesCount)
			{
				return false;
			}
			if (existingChoicesCount > 0)
			{
				var orderedExistingChoices = existingOption.Choices.OrderBy(x => x.Name).ToList();
				var orderedNewChoices = newOption.Choices.OrderBy(x => x.Name).ToList();

				for (int i = 0; i < existingChoicesCount; i++)
				{
					if (orderedExistingChoices[i].Name != orderedNewChoices[i].Name
						|| (orderedExistingChoices[i].Value is string existingString && existingString != orderedNewChoices[i].Value as string)
						|| long.Parse(orderedExistingChoices[i].Value.ToString()) != long.Parse(orderedNewChoices[i].Value.ToString()))
					{
						return false;
					}
				}
			}

			// Recursively compare suboptions
			if (!CommandOptionListsEqual(existingOption.Options, newOption.Options))
			{
				return false;
			}

			return true;
		}

		public BaseCommand GetSlashCommandHandler(string name)
		{
			// If not yet initialized, wait until we are
			while (!_allCommandsBuilt) Thread.Sleep(250);

			_logger.LogDebug($"Resolving service of type {name}...");
			var service = _commandToTypeMappings.ContainsKey(name)
				? _serviceProvider.CreateScope().ServiceProvider.GetService(_commandToTypeMappings[name]) as BaseCommand
				: null;

			if (service == null) _logger.LogError("Service not found!");
			return service;
		}
	}
}