using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ITAD
{
	public record GameStats(
		[property: JsonProperty("rank")] int Rank,
		[property: JsonProperty("waitlisted")] int Waitlisted,
		[property: JsonProperty("collected")] int Collected
	);
}