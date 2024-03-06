using Newtonsoft.Json;
using static ShaosilBot.Core.Models.InvokeAI.Model;

namespace ShaosilBot.Core.Models.InvokeAI
{

	public record Model([property: JsonProperty("models")] IReadOnlyList<Content> Models)
	{
		public record Content(
			[property: JsonProperty("model_name")] string ModelName,
			[property: JsonProperty("base_model")] string BaseModel,
			[property: JsonProperty("model_type")] string ModelType,
			[property: JsonProperty("path")] string Path,
			[property: JsonProperty("description")] object Description,
			[property: JsonProperty("model_format")] string ModelFormat,
			[property: JsonProperty("error")] object Error,
			[property: JsonProperty("vae")] object Vae,
			[property: JsonProperty("config")] string Config,
			[property: JsonProperty("variant")] string Variant
		);
	};
}