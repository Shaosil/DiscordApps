using Newtonsoft.Json;

namespace ShaosilBot.Core.Models.InvokeAI
{
	public record Board(
		[property: JsonProperty("board_id")] string BoardId,
		[property: JsonProperty("board_name")] string BoardName,
		[property: JsonProperty("created_at")] DateTime? CreatedAtEST,
		[property: JsonProperty("updated_at")] DateTime? UpdatedAt,
		[property: JsonProperty("deleted_at")] DateTime? DeletedAt,
		[property: JsonProperty("cover_image_name")] string CoverImageName,
		[property: JsonProperty("image_count")] int ImageCount
	);
}