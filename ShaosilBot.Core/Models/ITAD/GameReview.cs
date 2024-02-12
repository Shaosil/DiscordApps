using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ITAD
{
	public record GameReview(
		[property: JsonProperty("score")] int? Score,
		[property: JsonProperty("source")] string Source,
		[property: JsonProperty("count")] int Count,
		[property: JsonProperty("url")] string Url
	);
}