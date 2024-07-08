using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ITAD
{
	public record Shop(
		[property: JsonProperty("id")] int ID,
		[property: JsonProperty("title")] string Name
	);
}
