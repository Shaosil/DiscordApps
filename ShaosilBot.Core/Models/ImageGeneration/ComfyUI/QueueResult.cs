using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ShaosilBot.Core.Models.ImageGeneration.ComfyUI
{
	public record QueueResult()
	{
		[JsonProperty("queue_running")]
		private List<List<object>> _internalRunning;

		[JsonProperty("queue_pending")]
		private List<List<object>> _internalPending;

		public IReadOnlyList<KeyValuePair<Guid, WorkflowNodes>>? Running => ParseObjList(_internalRunning);
		public IReadOnlyList<KeyValuePair<Guid, WorkflowNodes>>? Pending => ParseObjList(_internalPending);
		public IReadOnlyList<KeyValuePair<Guid, WorkflowNodes>>? AllItems => Running?.Concat(Pending ?? [])?.ToList();

		private IReadOnlyList<KeyValuePair<Guid, WorkflowNodes>>? ParseObjList(List<List<object>> list)
		{
			return list?.Select(r => new KeyValuePair<Guid, WorkflowNodes>(Guid.Parse(r[1].ToString()!), ((JObject)r[2]).ToObject<WorkflowNodes>()!))?.ToList();
		}
	}
}