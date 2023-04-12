using Discord;
using Discord.Rest;
using Quartz;
using ShaosilBot.Core.Interfaces;

namespace ShaosilBot.Core.Jobs
{
	public class ReminderJob : IJob
	{
		private readonly IDiscordRestClientProvider _discordRestClientProvider;

		public class DataMapKeys
		{
			public const string UserID = "UserID";
			public const string ChannelID = "ChannelID";
			public const string Message = "Message";
			public const string ReferenceMessageID = "ReferenceMessageID";
			public const string ReferenceMessageContent = "ReferenceMessageContent";
		}

		public ReminderJob(IDiscordRestClientProvider discordRestClientProvider)
		{
			_discordRestClientProvider = discordRestClientProvider;
		}

		public async Task Execute(IJobExecutionContext context)
		{
			ulong userID = ulong.Parse(context.MergedJobDataMap.GetString(DataMapKeys.UserID)!);
			string? channelStr = context.MergedJobDataMap.GetString(DataMapKeys.ChannelID);
			ulong? channelID = string.IsNullOrEmpty(channelStr) ? null : ulong.Parse(channelStr);
			string? msg = context.MergedJobDataMap.GetString(DataMapKeys.Message);

			// If it was private, send a DM to the user. Otherwise, try to send it to the channel
			var user = await _discordRestClientProvider.GetUserAsync(userID);
			var channel = channelID.HasValue ? await _discordRestClientProvider.GetChannelAsync(channelID.Value) as RestTextChannel : null;
			if (channel != null)
			{
				// If we have a reference message, this was from a message command. Otherwise, it was from a slash
				string? refMsgIDStr = context.MergedJobDataMap.GetString(DataMapKeys.ReferenceMessageID);
				ulong? refMsgID = string.IsNullOrEmpty(refMsgIDStr) ? null : ulong.Parse(refMsgIDStr);
				if (refMsgID.HasValue)
				{
					// If we don't find the original message, include the content instead
					var refMessage = await channel.GetMessageAsync(refMsgID.Value);
					if (refMessage == null)
					{
						string refMsgContent = context.MergedJobDataMap.GetString(DataMapKeys.ReferenceMessageContent)!;
						await channel.SendMessageAsync($"{msg}\n\nOriginal message not found. Backup contents:\n\n{refMsgContent}");
					}
					else
					{
						await channel.SendMessageAsync(msg, messageReference: new MessageReference(refMsgID));
					}
				}
				else
				{
					await channel.SendMessageAsync($"Reminder from <@{userID}>:\n\n{msg}");
				}
			}
			else if (user != null)
			{
				var dmChannel = await user.CreateDMChannelAsync();
				await dmChannel.SendMessageAsync($"I'm reminding you:\n\n{msg}");
			}
		}
	}
}