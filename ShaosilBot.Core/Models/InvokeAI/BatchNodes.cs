using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI
{
	public record BatchNodes(
		[property: JsonProperty("sdxl_model_loader")] BatchNodes.SdxlModelLoaderNode SdxlModelLoader,
		[property: JsonProperty("positive_conditioning")] BatchNodes.PositiveConditioningNode PositiveConditioning,
		[property: JsonProperty("negative_conditioning")] BatchNodes.NegativeConditioningNode NegativeConditioning,
		[property: JsonProperty("noise")] BatchNodes.NoiseNode Noise,
		[property: JsonProperty("sdxl_denoise_latents")] BatchNodes.SdxlDenoiseLatentsNode SdxlDenoiseLatents,
		[property: JsonProperty("latents_to_image")] BatchNodes.LatentsToImageNode LatentsToImage,
		[property: JsonProperty("core_metadata")] BatchNodes.CoreMetadataNode CoreMetadata,
		[property: JsonProperty("vae_loader")] BatchNodes.VaeLoaderNode VaeLoader
	)
	{
		public record SdxlModelLoaderNode(
			[property: JsonProperty("type")] string Type,
			[property: JsonProperty("id")] string Id,
			[property: JsonProperty("model")] Model Model,
			[property: JsonProperty("is_intermediate")] bool IsIntermediate
		);

		public record PositiveConditioningNode(
			[property: JsonProperty("type")] string Type,
			[property: JsonProperty("id")] string Id,
			[property: JsonProperty("prompt")] string Prompt,
			[property: JsonProperty("style")] string Style,
			[property: JsonProperty("is_intermediate")] bool IsIntermediate
		);

		public record NegativeConditioningNode(
			[property: JsonProperty("type")] string Type,
			[property: JsonProperty("id")] string Id,
			[property: JsonProperty("prompt")] string Prompt,
			[property: JsonProperty("style")] string Style,
			[property: JsonProperty("is_intermediate")] bool IsIntermediate
		);

		public record NoiseNode(
			[property: JsonProperty("type")] string Type,
			[property: JsonProperty("id")] string Id,
			[property: JsonProperty("seed")] long Seed,
			[property: JsonProperty("width")] int Width,
			[property: JsonProperty("height")] int Height,
			[property: JsonProperty("use_cpu")] bool UseCpu,
			[property: JsonProperty("is_intermediate")] bool IsIntermediate
		);

		public record SdxlDenoiseLatentsNode(
			[property: JsonProperty("type")] string Type,
			[property: JsonProperty("id")] string Id,
			[property: JsonProperty("cfg_scale")] string CfgScale,
			[property: JsonProperty("cfg_rescale_multiplier")] string CfgRescaleMultiplier,
			[property: JsonProperty("scheduler")] string Scheduler,
			[property: JsonProperty("steps")] int Steps,
			[property: JsonProperty("denoising_start")] string DenoisingStart,
			[property: JsonProperty("denoising_end")] string DenoisingEnd,
			[property: JsonProperty("is_intermediate")] bool IsIntermediate
		);

		public enum eTest { test1, test2 }

		public record LatentsToImageNode(
			[property: JsonProperty("type")] string Type,
			[property: JsonProperty("id")] string Id,
			[property: JsonProperty("fp32")] bool Fp32,
			[property: JsonProperty("is_intermediate")] bool IsIntermediate,
			[property: JsonProperty("board")] Board Board,
			[property: JsonProperty("use_cache")] bool UseCache
		);

		public record CoreMetadataNode(
			[property: JsonProperty("id")] string Id,
			[property: JsonProperty("type")] string Type,
			[property: JsonProperty("generation_mode")] string GenerationMode,
			[property: JsonProperty("cfg_scale")] string CfgScale,
			[property: JsonProperty("cfg_rescale_multiplier")] string CfgRescaleMultiplier,
			[property: JsonProperty("height")] int Height,
			[property: JsonProperty("width")] int Width,
			[property: JsonProperty("negative_prompt")] string NegativePrompt,
			[property: JsonProperty("model")] Model Model,
			[property: JsonProperty("steps")] int Steps,
			[property: JsonProperty("rand_device")] string RandDevice,
			[property: JsonProperty("scheduler")] string Scheduler,
			[property: JsonProperty("positive_style_prompt")] string PositiveStylePrompt,
			[property: JsonProperty("negative_style_prompt")] string NegativeStylePrompt,
			[property: JsonProperty("vae")] Model Vae
		);

		public record Model(
			[property: JsonProperty("key")] string ModelKey,
			[property: JsonProperty("hash")] string ModelHash,
			[property: JsonProperty("name")] string ModelName,
			[property: JsonProperty("base")] string BaseModel,
			[property: JsonProperty("type")] string ModelType
		);

		public record Board(
			[property: JsonProperty("board_id")] string BoardId
		);

		public record VaeLoaderNode(
			[property: JsonProperty("type")] string Type,
			[property: JsonProperty("id")] string Id,
			[property: JsonProperty("is_intermediate")] bool IsIntermediate,
			[property: JsonProperty("vae_model")] Model VaeModel
		);
	};
}