using Discord.Rest;
using Quartz;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Core.Jobs
{
	public class SelfDestructMessageJob : IJob
	{
		private readonly IDiscordRestClientProvider _discordRestClientProvider;

		public class DataMapKeys
		{
			public const string ChannelID = "ChannelID";
			public const string MessageID = "MessageID";
		}

		public SelfDestructMessageJob(IDiscordRestClientProvider discordRestClientProvider)
		{
			_discordRestClientProvider = discordRestClientProvider;
		}

		public async Task Execute(IJobExecutionContext context)
		{
			ulong channelId = ulong.Parse(context.MergedJobDataMap.GetString(DataMapKeys.ChannelID)!);
			ulong messageId = ulong.Parse(context.MergedJobDataMap.GetString(DataMapKeys.MessageID)!);

			var channel = await _discordRestClientProvider.Client.GetChannelAsync(channelId) as IRestMessageChannel;
			if (channel != null)
			{
				var message = await channel.GetMessageAsync(messageId);

				if (message != null) await channel.DeleteMessageAsync(message);
			}
		}
	}
}