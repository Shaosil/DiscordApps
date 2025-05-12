using Discord;
using ShaosilBot.Core.Models.ImageGeneration;

namespace ShaosilBot.Core.Interfaces
{
	public interface IImageGenerationProvider
	{
		IReadOnlyCollection<string> ValidSamplers { get; }

		Task<bool> IsOnline();
		Dictionary<string, string> GetConfigValidModels();
		Task<IEnumerable<string>> GetModelsOfType(string type, bool unfiltered = false);
		Task<SharedEnqueueResult> EnqueuePrompt(IUserMessage message, IUser requestor, string posPrompt, string? pNegPrompt, int? width, int? height, string? seedStr, string? model, string? sampler, int? pSteps, int? pCfg);
		bool TryCancelQueueItem(IUser user, Guid ID, out string response);
		bool TryDeleteImage(IUser user, Guid imageName, out string deleteResponse);
	}
}