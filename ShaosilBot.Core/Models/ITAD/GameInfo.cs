using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ITAD
{
	public record GameInfoResponse(
		[property: JsonProperty("id")] string Id,
		[property: JsonProperty("slug")] string Slug,
		[property: JsonProperty("title")] string Title,
		[property: JsonProperty("type")] string Type,
		[property: JsonProperty("mature")] bool Mature,
		[property: JsonProperty("earlyAccess")] bool EarlyAccess,
		[property: JsonProperty("achievements")] bool Achievements,
		[property: JsonProperty("tradingCards")] bool TradingCards,
		[property: JsonProperty("appid")] int Appid,
		[property: JsonProperty("tags")] IReadOnlyList<string> Tags,
		[property: JsonProperty("releaseDate")] string ReleaseDate,
		[property: JsonProperty("developers")] IReadOnlyList<GenericObj> Developers,
		[property: JsonProperty("publishers")] IReadOnlyList<GenericObj> Publishers,
		[property: JsonProperty("reviews")] IReadOnlyList<GameReview> Reviews,
		[property: JsonProperty("stats")] GameStats Stats,
		[property: JsonProperty("players")] GamePlayers Players
	);
}