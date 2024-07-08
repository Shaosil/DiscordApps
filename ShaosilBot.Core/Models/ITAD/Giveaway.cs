using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ITAD
{
	public record GiveawayResponse(
		[property: JsonProperty("_id")] int ID,
		[property: JsonProperty("offset")] int offset,
		[property: JsonProperty("done")] bool Done,
		[property: JsonProperty("data")] IReadOnlyList<GiveawayResponse.GiveawayItem> Items
	)
	{
		public record GiveawayItem(
			[property: JsonProperty("id")] int ID,
			[property: JsonProperty("url")] string URL,
			[property: JsonProperty("expiry")] long? ExpiryUnixSeconds,
			[property: JsonProperty("publishAt")] long PublishAtUnixSeconds,
			[property: JsonProperty("isPending")] bool Pending,
			[property: JsonProperty("title")] string Title,
			[property: JsonProperty("isMature")] bool Mature,
			[property: JsonProperty("shop")] int ShopID,
			[property: JsonProperty("games")] IReadOnlyList<DealResponse.DealOverview> Games
		)
		{
			[JsonIgnore]
			public DateTimeOffset PublishedAt => DateTimeOffset.FromUnixTimeSeconds(PublishAtUnixSeconds);
			[JsonIgnore]
			public DateTimeOffset? Expiry => ExpiryUnixSeconds.HasValue ? DateTimeOffset.FromUnixTimeSeconds(ExpiryUnixSeconds.Value) : null;
		};
	};
}