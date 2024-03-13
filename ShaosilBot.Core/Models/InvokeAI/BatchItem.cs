using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI
{
	public record BatchRoot(
		[property: JsonProperty("prepend")] bool Prepend,
		[property: JsonProperty("batch")] BatchRoot.BatchItem Batch
	)
	{
		public record BatchItem(
			[property: JsonProperty("batch_id", NullValueHandling = NullValueHandling.Ignore)] string? BatchID,
			[property: JsonProperty("graph")] Graph Graph,
			[property: JsonProperty("runs")] int Runs,
			[property: JsonProperty("data")] IReadOnlyList<List<Data>> Data
		);

		public record Graph(
			[property: JsonProperty("id")] string Id,
			[property: JsonProperty("nodes")] BatchNodes Nodes,
			[property: JsonProperty("edges")] IReadOnlyList<Edge> Edges
		);

		public record Edge(
			[property: JsonProperty("source")] EdgeItem Source,
			[property: JsonProperty("destination")] EdgeItem Destination
		);

		public record EdgeItem(
			[property: JsonProperty("node_id")] string NodeId,
			[property: JsonProperty("field")] string Field
		);

		public record Data(
			[property: JsonProperty("node_path")] string NodePath,
			[property: JsonProperty("field_name")] string FieldName,
			[property: JsonProperty("items")] List<object> items
		);
	};
}