using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI.SocketIO
{
	public record QueueStatus(
		[property: JsonProperty("queue_id")] string QueueId,
		[property: JsonProperty("queue_item")] QueueStatus.QueueItemRecord QueueItem,
		[property: JsonProperty("timestamp")] int Timestamp
	)
	{
		public record QueueItemRecord(
			[property: JsonProperty("item_id")] int ItemId,
			[property: JsonProperty("status")] string Status,
			[property: JsonProperty("batch_id")] string BatchId,
			[property: JsonProperty("session_id")] string SessionId,
			[property: JsonProperty("error")] object Error,
			[property: JsonProperty("created_at")] string CreatedAt,
			[property: JsonProperty("updated_at")] string UpdatedAt,
			[property: JsonProperty("started_at")] string StartedAt,
			[property: JsonProperty("completed_at")] object CompletedAt
		);
	};
}