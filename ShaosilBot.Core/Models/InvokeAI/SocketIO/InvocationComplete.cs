using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI.SocketIO
{
	public record InvocationComplete(
		[property: JsonProperty("queue_id")] string QueueID,
		[property: JsonProperty("queue_item_id")] int QueueItemID,
		[property: JsonProperty("queue_batch_id")] string QueueBatchID,
		[property: JsonProperty("graph_execution_state_id")] string GraphExecutionStateID,
		[property: JsonProperty("source_node_id")] string SourceNodeID,
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
