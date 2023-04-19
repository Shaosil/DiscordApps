using Discord;
using Quartz;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Models.SQLite;
using ShaosilBot.Core.SlashCommands;

namespace ShaosilBot.Core.Jobs
{
	public class PollEndJob : IJob
	{
		private readonly IDiscordRestClientProvider _restClientProvider;
		private readonly ISQLiteProvider _sqliteProvider;
		private readonly PollCommand _pollCommand;

		public PollEndJob(IDiscordRestClientProvider restClientProvider, ISQLiteProvider sqliteProvider, PollCommand pollCommand)
		{
			_restClientProvider = restClientProvider;
			_sqliteProvider = sqliteProvider;
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
			_pollCommand.SelectMenuReadySignal.Wait(10000);
			_pollCommand.SelectMenuReadySignal.Reset();

			// Load job data
			ulong channelID = ulong.Parse(context.MergedJobDataMap.GetString(DataMapKeys.ChannelID)!);
			ulong messageID = ulong.Parse(context.MergedJobDataMap.GetString(DataMapKeys.MessageID)!);
			var originalChannel = await _restClientProvider.GetChannelAsync(channelID);
			var originalMessage = originalChannel != null ? await originalChannel.GetMessageAsync(messageID) : null;

			// Load DB data
			var poll = _sqliteProvider.GetDataRecord<PollMessage, ulong>(messageID);
			if (poll != null)
			{
				if (originalChannel != null)
				{
					// Update original message if it still exists
					if (originalMessage != null)
					{
						// Remove the components and update the description
						var originalEmbed = originalMessage.Embeds.First();
						string newDesc = $"{_pollCommand.GetDescriptionFromChoices(poll.PollChoices, true)}\n\nPoll has ended and the results are in!";

						var modifiedEmbed = new EmbedBuilder()
						{
							Title = originalEmbed.Title,
							Color = originalEmbed.Color,
							Description = newDesc
						};

						await originalChannel.ModifyMessageAsync(messageID, (m) => { m.Embed = modifiedEmbed.Build(); m.Components = null; });
					}

					// Delete separate voting message if one exists
					var sepMessage = poll.SelectMenuMessageID.HasValue ? await originalChannel.GetMessageAsync(poll.SelectMenuMessageID.Value) : null;
					if (sepMessage != null)
					{
						await sepMessage.DeleteAsync();
					}
				}

				// Now delete all related records from the poll tables
				if (poll.PollChoices.Sum(c => c.PollUserVotes.Count) > 0) _sqliteProvider.DeleteDataRecords(poll.PollChoices.SelectMany(c => c.PollUserVotes).ToArray());
				if (poll.PollChoices.Count > 0) _sqliteProvider.DeleteDataRecords(poll.PollChoices.ToArray());
				_sqliteProvider.DeleteDataRecords(poll);
			}

			// Ready for the next thread
			_pollCommand.SelectMenuReadySignal.Set();
		}
	}
}