using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI.SocketIO
{
	public record InvocationComplete(
		[property: JsonProperty("queue_id")] string QueueID,
		[property: JsonProperty("item_id")] int QueueItemID,
		[property: JsonProperty("batch_id")] Guid QueueBatchID,
		[property: JsonProperty("invocation_source_id")] string SourceNodeID,
		[property: JsonProperty("result")] InvocationComplete.ResultRecord Result
	)
	{
		public record ResultRecord(
			[property: JsonProperty("image")] ResultRecord.ImageRecord Image,
			[property: JsonProperty("width")] int Width,
			[property: JsonProperty("height")] int Height,
			[property: JsonProperty("type")] string Type
		)
		{
			public record ImageRecord(
				[property: JsonProperty("image_name")] string ImageName
			);
		};
	};
}
