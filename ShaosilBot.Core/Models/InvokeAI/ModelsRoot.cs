using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI
{
	public record ModelsRoot([property: JsonProperty("models")] IReadOnlyList<ModelsRoot.Model> ModelList)
	{
		public record Model(
			[property: JsonProperty("key")] string ModelKey,
			[property: JsonProperty("hash")] string ModelHash,
			[property: JsonProperty("name")] string ModelName,
			[property: JsonProperty("base")] string BaseModel,
			[property: JsonProperty("type")] string ModelType,
			[property: JsonProperty("path")] string Path,
			[property: JsonProperty("description")] object Description,
			[property: JsonProperty("format")] string ModelFormat,
			[property: JsonProperty("config_path")] string Config,
			[property: JsonProperty("variant")] string Variant
		);
	};
}