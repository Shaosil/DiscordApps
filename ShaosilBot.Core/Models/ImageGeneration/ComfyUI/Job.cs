using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ImageGeneration.ComfyUI
{
	public class Job
	{
		[JsonProperty("id")]
		public string ID { get; set; }

		[JsonProperty("status")]
		public string Status { get; set; }

		[JsonProperty("priority")]
		public string Priority { get; set; }

		[JsonProperty("create_time")]
		public long CreateTimeTicks { get; set; }
		public DateTimeOffset CreateTime => DateTimeOffset.FromUnixTimeMilliseconds(CreateTimeTicks);

		[JsonProperty("execution_start_time")]
		public long ExecutionStartTicks { get; set; }
		public DateTimeOffset ExecutionStart => DateTimeOffset.FromUnixTimeMilliseconds(ExecutionStartTicks);

		[JsonProperty("execution_end_time")]
		public long ExecutionEndTicks { get; set; }
		public DateTimeOffset ExecutionEnd => DateTimeOffset.FromUnixTimeMilliseconds(ExecutionEndTicks);

		[JsonProperty("preview_output")]
		public PreviewOutput Output { get; set; }

		[JsonProperty("workflow")]
		public JobWorkflow Workflow { get; set; }

		public class PreviewOutput
		{
			[JsonProperty("filename")]
			public string FileName { get; set; }

			[JsonProperty("subfolder")]
			public string Subfolder { get; set; }

			[JsonProperty("type")]
			public string FileType { get; set; }
		}

		public class JobWorkflow
		{
			[JsonProperty("prompt")]
			public WorkflowNodes Nodes { get; set;}
		}
	}
}