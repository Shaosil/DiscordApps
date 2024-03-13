namespace ShaosilBot.Core.Models.InvokeAI
{
	public record FriendlyEnqueueResult(string BatchID, int LinePos, string PositivePrompt, string NegativePrompt, uint Seed, string Model, int Steps, string CFG);
}