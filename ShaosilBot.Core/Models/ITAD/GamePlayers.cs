using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ITAD
{
	public record GamePlayers(
		[property: JsonProperty("recent")] int Recent,
		[property: JsonProperty("day")] int Day,
		[property: JsonProperty("week")] int Week,
		[property: JsonProperty("peak")] int Peak
	);
}