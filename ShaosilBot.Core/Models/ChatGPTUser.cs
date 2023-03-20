namespace ShaosilBot.Core.Models
{
	public class ChatGPTUser
	{
		public int AvailableTokens { get; set; } // Refilled each billing cycle, and when the number of users in the server changes. May be borrowed by other users

		public int BorrowedTokens { get; set; } // The amount of tokens that have been borrowed by others Overlaps with AvailableTokens.

		public int BorrowableTokens => AvailableTokens - BorrowedTokens; // Helper calculation

		public Dictionary<DateTime, int> TokensUsed { get; set; } = new Dictionary<DateTime, int>();
	}
}