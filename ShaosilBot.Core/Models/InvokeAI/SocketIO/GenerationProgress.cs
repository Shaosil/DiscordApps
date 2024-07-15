using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI.SocketIO
{
	public record GenerationProgress(
		[property: JsonProperty("queue_id")] string QueueID,
		[property: JsonProperty("item_id")] int QueueItemID,
		[property: JsonProperty("batch_id")] Guid QueueBatchID,
		[property: JsonProperty("progress_image")] GenerationProgress.ProgressImageRecord? ProgressImage,
		[property: JsonProperty("step")] int Step,
		[property: JsonProperty("order")] int Order,
		[property: JsonProperty("total_steps")] int TotalSteps
	)
	{
		public record ProgressImageRecord(
			[property: JsonProperty("width")] int Width,
			[property: JsonProperty("height")] int Height,
			[property: JsonProperty("dataURL")] string DataURL
		);
	};
}