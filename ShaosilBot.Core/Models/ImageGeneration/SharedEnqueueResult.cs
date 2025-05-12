namespace ShaosilBot.Core.Models.ImageGeneration
{
	public class SharedEnqueueResult
	{
		public string PositivePrompt { get; init; }
		public string NegativePrompt { get; init; }
		public ulong Seed { get; init; }
		public string Model { get; init; }
		public int Steps { get; init; }
		public int CFG { get; init; }
		public int LinePos { get; init; }
		public Guid BatchID { get; init; }

		public bool Success { get; init; }
		public string ErrorMessage { get; init; }

		public SharedEnqueueResult(string positivePrompt, string negativePrompt, ulong seed, string model, int steps, int cfg, int linePos, Guid batchID)
		{
			PositivePrompt = positivePrompt;
			NegativePrompt = negativePrompt;
			Seed = seed;
			Model = model;
			Steps = steps;
			CFG = cfg;
			LinePos = linePos;
			BatchID = batchID;

			Success = true;
		}

		public SharedEnqueueResult(string errorMessage)
		{
			Success = false;
			ErrorMessage = errorMessage;
		}
	}
}