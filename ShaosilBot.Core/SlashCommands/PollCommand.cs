using Discord;
using Microsoft.Extensions.Logging;
using ShaosilBot.Core.Providers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ShaosilBot.Core.SlashCommands
{
	public class PollCommand : BaseCommand
	{
		public PollCommand(ILogger<BaseCommand> logger) : base(logger) { }

		public override string CommandName => "poll";

		public override string HelpSummary => "Lets people vote on a question. Can be a simple yes/no or up to 20 choices with custom emojis!";

		public override string HelpDetails => @$"/{CommandName} (string question) [string choice1 - choice20]

Passing only a question will provide ':thumbsup:' and ':thumbsdown:' reactions by default. Otherwise, provide 2+ choices for a custom list.

REQUIRED ARGS:
* question:
    The text on which to vote.

OPTIONAL ARGS:
* choice1 - choice20:
    If one is provided, at least two must be. Place any basic emoji at the front in order to replace its matching 'a, b, c' reaction.";

		public override SlashCommandProperties BuildCommand()
		{
			var choices = new List<SlashCommandOptionBuilder>();
			for (int i = 1; i <= 20; i++)
				choices.Add(new SlashCommandOptionBuilder
				{
					Name = $"choice{i}",
					Description = HelpSummary,
					Type = ApplicationCommandOptionType.String
				});

			return new SlashCommandBuilder
			{
				Description = HelpSummary,
				Options = new[]
				{
					new SlashCommandOptionBuilder
					{
						Name = "question",
						Type = ApplicationCommandOptionType.String,
						Description = "What do you want people to vote on?",
						IsRequired = true
					}
				}.Concat(choices).ToList()
			}.Build();
		}

		public override Task<string> HandleCommand(SlashCommandWrapper command)
		{
			// Validation
			string? questionText = command.Data.Options.FirstOrDefault(o => o.Name == "question")?.Value as string;
			if (string.IsNullOrWhiteSpace(questionText))
				return Task.FromResult(command.Respond("You must provide a question!", ephemeral: true));

			var choiceArgs = command.Data.Options.Where(o => o.Name.StartsWith("choice")).Select(c => c.Value?.ToString()?.Trim()).ToList();
			if (choiceArgs.Count == 1)
				return Task.FromResult(command.Respond("You may either provide no choices for a default yes/no poll, or 2+ choices for a custom list.", ephemeral: true));

			// Build choice texts and reactions
			char[] defaultEmoji = "🇦".ToArray();
			var reactionEmojis = new List<string>();
			var choiceTexts = new List<string>();
			for (int i = 0; i < choiceArgs.Count; i++)
			{
				// Use supplied emoji for this choice if one exists
				var captures = Regex.Match(choiceArgs[i]!, "^(\\p{Cs}*)(\\P{Cs}*.*)");
				string emoji = captures.Groups[1].Value;
				if (string.IsNullOrWhiteSpace(emoji))
					emoji = new string(defaultEmoji);

				// Make sure we only take a single emoji in case multiple in a row were defined
				var emojiInfo = new StringInfo(emoji);
				string emojiRemainder = string.Empty;
				if (emojiInfo.LengthInTextElements > 1)
				{
					emojiRemainder = emojiInfo.SubstringByTextElements(1);
					emoji = emojiInfo.SubstringByTextElements(0, 1);
				}
				reactionEmojis.Add(emoji);

				// Use only the choice text, if any (prefixing with any leftover emojis - very rarely occurs)
				string? choiceText = captures.Groups[2].Value?.Trim();
				if (!string.IsNullOrWhiteSpace(choiceText))
					choiceTexts.Add($"{emoji}: {emojiRemainder}{choiceText}");

				// Incrementing the second unicode position of the regional 'A' will correctly return the following letters
				defaultEmoji[1] = (char)(defaultEmoji[1] + 1);
			}

			// Thumbs up/thumbs down for no supplied choices
			if (choiceArgs.Count == 0)
				reactionEmojis.AddRange(new[] { "👍", "👎" });

			// Make sure the reactions are distinct
			reactionEmojis = reactionEmojis.Distinct().ToList();

			// Validate choice count again in case they only supplied emojis in SOME of the choices
			if (choiceTexts.Count > 0 && choiceTexts.Count < choiceArgs.Count)
				return Task.FromResult(command.Respond("Your choices contain a mix of emojis and text. Please stick with one or the other", ephemeral: true));

			// Validate the number of unique reactions == the number of choices
			if (reactionEmojis.Count < choiceTexts.Count || reactionEmojis.Count < 2)
				return Task.FromResult(command.Respond("Duplicate custom emojis detected - please try again with unique options.", ephemeral: true));

			// Spin up a new thread to wait for the below response so we can react to the new message
			_ = Task.Run(async () =>
			{
				var message = await command.GetOriginalResponseAsync();
				if (message != null)
					await message.AddReactionsAsync(reactionEmojis.Select(e => Emoji.Parse(e) as IEmote));
			});

			var embed = new EmbedBuilder() { Title = $"📊 **{questionText.Trim()}**", Description = string.Join('\n', choiceTexts), Color = new Color(0x7c0089) };
			return Task.FromResult(command.Respond(embed: embed.Build()));
		}
	}
}