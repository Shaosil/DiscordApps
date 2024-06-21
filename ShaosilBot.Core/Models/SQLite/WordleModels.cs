namespace ShaosilBot.Core.Models.SQLite
{
	public class WordleWord : ITable
	{
		[PrimaryKey(false)]
		public string Word { get; set; }

		[Required]
		public bool CanBeSolution { get; set; }
	}

	public class WordleGuess : ITable
	{
		[PrimaryKey(true)]
		public int ID { get; set; }

		[Required]
		public ulong UserID { get; set; }

		[ForeignKey(typeof(WordleWord), nameof(WordleWord.Word))]
		public string WordID { get; set; }

		[Required]
		public string Guess { get; set; }
	}

	public class WordleStat : ITable
	{
		[PrimaryKey(true)]
		public int ID { get; set; }

		[Required]
		public ulong UserID { get; set; }

		[ForeignKey(typeof(WordleWord), nameof(WordleWord.Word))]
		public string WordID { get; set; }

		[Required]
		public bool Solved { get; set; }

		[Required]
		public int NumGuesses { get; set; }
	}
}