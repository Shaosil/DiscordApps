namespace ShaosilBot.Core.Models.SQLite
{
	public class PollMessage : ITable
	{
		[PrimaryKey(false)]
		public ulong MessageID { get; set; }

		public ulong? SelectMenuMessageID { get; set; }

		[Required]
		public string Text { get; set; }

		public bool IsBlind { get; set; }

		public DateTimeOffset ExpiresAt { get; set; }

		public List<PollChoice> PollChoices { get; set; } = new();
	}

	public class PollChoice : ITable
	{
		[PrimaryKey(true)]
		public int ID { get; set; }

		[ForeignKey<PollMessage>(nameof(PollMessage.MessageID))]
		public ulong PollMessageID { get; set; }

		public int Order { get; set; }

		[Required]
		public string Text { get; set; }

		public PollMessage Message { get; set; }
		public List<PollUserVote> PollUserVotes { get; set; } = new();
	}

	public class PollUserVote : ITable
	{
		[PrimaryKey(true)]
		public int ID { get; set; }

		public ulong UserID { get; set; }

		[ForeignKey<PollChoice>(nameof(PollChoice.ID))]
		public int PollChoiceID { get; set; }

		public PollChoice Choice { get; set; }
	}
}