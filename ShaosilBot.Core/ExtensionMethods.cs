using Discord;

namespace ShaosilBot.Core
{
	public static class EmbedExtensions
	{
		public static Embed Copy(this IEmbed originalEmbed, string? newTitle = null, string? newDesc = null, string? newImgURL = null, string? newThumbnailURL = null, string? newURL = null)
		{
			return new EmbedBuilder()
			{
				Color = originalEmbed.Color,
				Title = newTitle ?? originalEmbed.Title,
				Description = newDesc ?? originalEmbed.Description,
				ImageUrl = newImgURL ?? originalEmbed.Image?.Url,
				ThumbnailUrl = newThumbnailURL ?? originalEmbed.Thumbnail?.Url,
				Url = newURL ?? originalEmbed.Url
			}.Build();
		}
	}
}