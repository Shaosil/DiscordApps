namespace ShaosilBot.Core.Models.SQLite
{
	public class GameSale : ITable
	{
		[PrimaryKey(false)]
		public Guid ID { get; set; }

		[Required]
		public string Slug { get; set; }

		[Required]
		public string Title { get; set; }

		public string IsThereAnyDealLink { get; set; }

		public double BestPrice { get; set; }

		public int BestPercentOff { get; set; }

		public string? BestPercentStore { get; set; }

		public double? BestPercentStoreRegularPrice { get; set; }

		public string? BestPercentStoreLink { get; set; }

		public ulong? DiscordChannelID { get; set; }

		public ulong? DiscordMessageID { get; set; }

		public DateTime AddedOn { get; set; }
	}
}