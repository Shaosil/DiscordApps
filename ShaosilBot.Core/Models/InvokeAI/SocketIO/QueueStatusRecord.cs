using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI.SocketIO
{
	public record QueueStatus(
		[property: JsonProperty("queue_id")] string QueueId,
		[property: JsonProperty("status")] string Status,
		[property: JsonProperty("item_id")] int ItemId,
		[property: JsonProperty("batch_id")] Guid BatchId,
		[property: JsonProperty("timestamp")] int Timestamp
	);
}