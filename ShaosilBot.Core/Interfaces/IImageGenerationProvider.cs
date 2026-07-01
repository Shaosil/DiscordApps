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
		Task<SharedEnqueueResult> EnqueuePrompt(IUserMessage message, IUser requestor, string posPrompt, string? negPrompt, int? width, int? height,
			string? seedStr, string? model, string? sampler, int? steps, int? cfg, string? imageName);
		Task<KeyValuePair<bool, string>> TryCancelQueueItem(IUser user, Guid ID);
		Task<KeyValuePair<bool, string>> TryDeleteImage(IUser user, Guid imageName);
	}
}