using System.Text.Json.Serialization;

namespace ShaosilBot.Core.Models
{
	public class ChatGPTUser
	{
		public int AvailableTokens { get; set; } // Refilled each billing cycle, and when the number of users in the server changes. May be borrowed by other users

		public Dictionary<ulong, int> LentTokens { get; set; } = new Dictionary<ulong, int>(); // The amount of tokens that have been lent to others. Overlaps with AvailableTokens.

		[JsonIgnore]
		public int BorrowableTokens => AvailableTokens - LentTokens.Sum(t => t.Value); // Helper calculation - The amount of tokens that may still yet be borrowed by others

		public string? CustomUserPrompt { get; set; }

		public string? CustomAssistantPrompt { get; set; }

		public Dictionary<DateTime, int> TokensUsed { get; set; } = new Dictionary<DateTime, int>();
	}
}