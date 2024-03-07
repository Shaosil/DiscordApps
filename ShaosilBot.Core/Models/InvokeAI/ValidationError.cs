using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI
{
	public record ValidationError(
		[property: JsonProperty("detail")] IReadOnlyList<ValidationError.Detail> Details
	)
	{
		public record Detail(
			[property: JsonProperty("loc")] IReadOnlyList<object> Loc,
			[property: JsonProperty("msg")] string Msg,
			[property: JsonProperty("type")] string Type
		);
	};
}