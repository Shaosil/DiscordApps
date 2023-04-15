using Discord;
using Quartz;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.SlashCommands;

namespace ShaosilBot.Core.Jobs
{
	public class PollEndJob : IJob
	{
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly PollCommand _pollCommand;

		public PollEndJob(IDiscordRestClientProvider restClientProvider, PollCommand pollCommand)
		{
			_restClientProvider = restClientProvider;
			_pollCommand = pollCommand;
		}

		public class DataMapKeys
		{
			public const string ChannelID = "ChannelID";
			public const string MessageID = "MessageID";
		}

		public async Task Execute(IJobExecutionContext context)
		{
			// Wait here and lock the poll command's ready signal
			_pollCommand.SelectMenuReadySignal.WaitOne(10000);
			_pollCommand.SelectMenuReadySignal.Reset();

			// Load data
			ulong channelID = ulong.Parse(context.MergedJobDataMap.GetString(DataMapKeys.ChannelID)!);
			ulong messageID = ulong.Parse(context.MergedJobDataMap.GetString(DataMapKeys.MessageID)!);
			var originalChannel = await _restClientProvider.GetChannelAsync(channelID);
			var originalMessage = originalChannel != null ? await originalChannel.GetMessageAsync(messageID) : null;

			// Do nothing if the original poll no longer exists
			if (originalMessage != null)
			{
				// Remove the components and update the description
				var originalEmbed = originalMessage.Embeds.First();
				var parsedVotes = _pollCommand.ParseVotes(originalEmbed.Description, true);
				string newDesc = $"{_pollCommand.GetDescriptionFromParsedVotes(parsedVotes)}\n\nPoll has ended and the results are in!";

				var modifiedEmbed = new EmbedBuilder()
				{
					Title = originalEmbed.Title,
					Color = originalEmbed.Color,
					Description = newDesc
				};

				await originalChannel!.ModifyMessageAsync(messageID, (m) => { m.Embed = modifiedEmbed.Build(); m.Components = null; });
			}

			// Ready for the next thread
			_pollCommand.SelectMenuReadySignal.Set();
		}
	}
}