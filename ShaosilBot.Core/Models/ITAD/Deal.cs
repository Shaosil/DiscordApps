using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ITAD
{
	public record DealResponse(
		[property: JsonProperty("nextOffset")] int NextOffset,
		[property: JsonProperty("hasMore")] bool HasMore,
		[property: JsonProperty("list")] IReadOnlyList<DealResponse.DealOverview> Deals
	)
	{
		public record DealOverview(
			[property: JsonProperty("id")] Guid ID,
			[property: JsonProperty("slug")] string Slug,
			[property: JsonProperty("title")] string Title,
			[property: JsonProperty("type")] string Type,
			[property: JsonProperty("deal")] DealOverview.DealDetails Deal
		)
		{
			public record DealDetails(
				[property: JsonProperty("shop")] GenericObj Shop,
				[property: JsonProperty("price")] Price Price,
				[property: JsonProperty("regular")] Price Regular,
				[property: JsonProperty("cut")] int Cut,
				[property: JsonProperty("voucher")] string Voucher,
				[property: JsonProperty("storeLow")] Price StoreLow,
				[property: JsonProperty("historyLow")] Price HistoryLow,
				[property: JsonProperty("flag")] string Flag,
				[property: JsonProperty("drm")] IReadOnlyList<GenericObj> DRMs,
				[property: JsonProperty("platforms")] IReadOnlyList<GenericObj> Platforms,
				[property: JsonProperty("timestamp")] DateTime Timestamp,
				[property: JsonProperty("expiry")] DateTime? Expiry,
				[property: JsonProperty("url")] string URL
			);
		};
	};
}