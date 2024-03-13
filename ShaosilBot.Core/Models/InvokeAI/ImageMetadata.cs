using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI
{
	public record ImageMetadata(
		[property: JsonProperty("generation_mode")] string GenerationMode,
		[property: JsonProperty("positive_prompt")] string PositivePrompt,
		[property: JsonProperty("negative_prompt")] string NegativePrompt,
		[property: JsonProperty("width")] int Width,
		[property: JsonProperty("height")] int Height,
		[property: JsonProperty("seed")] uint Seed,
		[property: JsonProperty("rand_device")] string RandDevice,
		[property: JsonProperty("cfg_scale")] string CfgScale,
		[property: JsonProperty("cfg_rescale_multiplier")] string CfgRescaleMultiplier,
		[property: JsonProperty("steps")] int Steps,
		[property: JsonProperty("scheduler")] string Scheduler,
		[property: JsonProperty("model")] Model.Content Model,
		[property: JsonProperty("vae")] Model.Content Vae,
		[property: JsonProperty("positive_style_prompt")] string PositiveStylePrompt,
		[property: JsonProperty("negative_style_prompt")] string NegativeStylePrompt
	);
}