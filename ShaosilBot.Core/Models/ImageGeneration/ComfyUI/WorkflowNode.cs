using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ImageGeneration.ComfyUI
{
	public class WorkflowNodes : Dictionary<string, WorkflowNodes.WorkflowNode>
	{
		public WorkflowNode? Checkpoint => Values.FirstOrDefault(n => n.ClassType == "CheckpointLoaderSimple");
		public WorkflowNode? Image => Values.FirstOrDefault(n => n.ClassType == "EmptyLatentImage");
		public WorkflowNode? PositiveText => Values.FirstOrDefault(n => n.ClassType == "CLIPTextEncode" && n.Metadata.Title == "Positive Prompt Text");
		public WorkflowNode? NegativeText => Values.FirstOrDefault(n => n.ClassType == "CLIPTextEncode" && n.Metadata.Title == "Negative Prompt Text");
		public WorkflowNode? Sampler => Values.FirstOrDefault(n => n.ClassType == "KSamplerAdvanced");
		public WorkflowNode? SaveImage => Values.FirstOrDefault(n => n.ClassType == "SaveImage");

		public class WorkflowNode
		{

			[JsonProperty("inputs")]
			public Dictionary<string, object> Inputs { get; set; }

			[JsonProperty("class_type")]
			public string ClassType { get; set; }

			[JsonProperty("_meta")]
			public Meta Metadata { get; set; }

			public class Meta
			{
				[JsonProperty("title")]
				public string Title { get; set; }
			}
		};
	}
}