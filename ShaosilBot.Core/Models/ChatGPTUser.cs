using System.Text.Json.Serialization;

namespace ShaosilBot.Core.Models
{
	public class ChatGPTUser
	{
		// Refilled each billing cycle, and when the number of users in the server changes. May be borrowed by other users
		public int AvailableInputTokens { get; set; }
		public int AvailableOutputTokens { get; set; }

		// Custom user and bot prompts
		public string? CustomUserPrompt { get; set; }
		public string? CustomAssistantPrompt { get; set; }

		// Keeps track of input and output tokens used
		public Dictionary<DateTime, int> InputTokensUsed { get; set; } = new Dictionary<DateTime, int>();
		public Dictionary<DateTime, int> OutputTokensUsed { get; set; } = new Dictionary<DateTime, int>();

		// The amount of tokens that have been lent to others. Overlaps with AvailableTokens.
		public Dictionary<ulong, int> LentInputTokens { get; set; } = new Dictionary<ulong, int>();
		public Dictionary<ulong, int> LentOutputTokens { get; set; } = new Dictionary<ulong, int>();

		// Helper calculations
		[JsonIgnore]
		public int TotalAvailableTokens => (AvailableInputTokens + AvailableOutputTokens);
		[JsonIgnore]
		public int TotalTokensUsed => InputTokensUsed.Sum(i => i.Value) + OutputTokensUsed.Sum(o => o.Value);
		[JsonIgnore]
		public int BorrowableTokens => TotalAvailableTokens - (LentInputTokens.Sum(t => t.Value) + LentOutputTokens.Sum(t => t.Value));
	}
}