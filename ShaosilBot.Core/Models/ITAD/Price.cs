using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.ITAD
{
	public record Price(
		[property: JsonProperty("amount")] double Amount,
		[property: JsonProperty("amountInt")] int AmountInt,
		[property: JsonProperty("currency")] string Currency
	);
}
