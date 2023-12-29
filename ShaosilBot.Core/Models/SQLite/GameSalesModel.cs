namespace ShaosilBot.Core.Models.SQLite
{
	public class GameSale : ITable
	{
		[PrimaryKey(false)]
		public string PlainGameID { get; set; }

		[Required]
		public string Title { get; set; }

		public string IsThereAnyDealLink { get; set; }

		public string? Reviews { get; set; }

		public decimal BestPrice { get; set; }

		public int BestPercentOff { get; set; }

		public string? BestPercentStore { get; set; }

		public decimal? BestPercentStoreRegularPrice { get; set; }

		public string? BestPercentStoreLink { get; set; }

		public string RawHtml { get; set; }

		public ulong? DiscordChannelID { get; set; }

		public ulong? DiscordMessageID { get; set; }

		public DateTime AddedOn { get; set; }
	}
}