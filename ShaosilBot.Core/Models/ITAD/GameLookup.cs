using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ITAD
{
	public record GameLookupResponse(
		[property: JsonProperty("found")] bool Found,
		[property: JsonProperty("game")] GameInfoResponse? Game
	);
}