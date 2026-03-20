namespace ShaosilBot.Core.Models.SQLite
{
	public class WordleWord : IEntity
	{
		[PrimaryKey(false)]
		public string Word { get; set; }

		[Required]
		public bool CanBeSolution { get; set; }
	}

	public class WordleGame : IEntity
	{
		[PrimaryKey(true)]
		public int ID { get; set; }

		[Required]
		public ulong UserID { get; set; }

		[ForeignKey(typeof(WordleWord), nameof(WordleWord.Word))]
		public string WordID { get; set; }

		[Required]
		public bool IsDaily { get; set; }

		public DateTimeOffset StartedTimestamp { get; set; }

		public DateTimeOffset? RemindedTimestamp { get; set; } // Active games over 2H since a guess will do a remind instead

		public List<WordleGuess> Guesses { get; set; }
	}

	public class WordleGuess : IEntity
	{
		[PrimaryKey(true)]
		public int ID { get; set; }

		[Required]
		public ulong UserID { get; set; }

		[ForeignKey(typeof(WordleGame), nameof(WordleGame.ID))]
		public int WordleGameID { get; set; }

		[Required]
		public string Guess { get; set; }

		public DateTimeOffset GuessTimestamp { get; set; }

		public WordleGame WordleGame { get; set; }
	}

	public class WordleStat : IEntity
	{
		[PrimaryKey(true)]
		public int ID { get; set; }

		[Required]
		public ulong UserID { get; set; }

		[ForeignKey(typeof(WordleWord), nameof(WordleWord.Word))]
		public string WordID { get; set; }

		[Required]
		public bool IsDaily { get; set; }

		[Required]
		public bool Solved { get; set; }

		[Required]
		public int NumGuesses { get; set; }

		public DateTimeOffset? EndedTimestamp { get; set; }
	}
}