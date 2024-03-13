using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI
{
	public record QueueItemCollection(
		[property: JsonProperty("limit")] int Limit,
		[property: JsonProperty("has_more")] bool HasMore,
		[property: JsonProperty("items")] List<QueueItemCollection.QueueItem> Items
	)
	{
		public record QueueItem(
			[property: JsonProperty("item_id")] int ItemID,
			[property: JsonProperty("status")] string Status,
			[property: JsonProperty("priority")] int Priority,
			[property: JsonProperty("batch_id")] string BatchID,
			[property: JsonProperty("session_id")] string SessionID
		);
	};
}