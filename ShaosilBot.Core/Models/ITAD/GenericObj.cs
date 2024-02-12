using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ITAD
{
	public record GenericObj(
		[property: JsonProperty("id")] int ID,
		[property: JsonProperty("name")] string Name
	);


}
