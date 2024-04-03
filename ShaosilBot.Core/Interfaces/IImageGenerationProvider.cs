using Discord;
using ShaosilBot.Core.Models.InvokeAI;

namespace ShaosilBot.Core.Interfaces
{
	public interface IImageGenerationProvider
	{
		IReadOnlyCollection<string> ValidSchedulers { get; }

		Task<bool> IsOnline();
		Dictionary<string, string> GetConfigValidModels();
		Task<IEnumerable<ModelsRoot.Model>> GetModelsOfType(string type);
		Task<FriendlyEnqueueResult> EnqueueBatchItem(IUserMessage message, IUser requestor, string posPrompt, string negPrompt, int width, int height, string? seedStr, string? model, string scheduler, int steps, string cfg);
		Task<List<Board>> GetAllBoards();
		Task<Board?> GetBoardByName(string boardName);
		Task<Board> CreateBoard(string boardName);
		Task<QueueItemCollection.QueueItem?> GetCurrentQueueItem();
		Task<QueueItemCollection> GetPendingQueueItems();
		bool TryCancelQueueItem(IUser user, string ID, out string response);
		Task<FriendlyEnqueueResult> RequeueImage(string imageName, IUserMessage message, IUser requestor, string? posPrompt = null, string? negPrompt = null, string? seedStr = null, string? model = null, int? steps = null);
		bool TryDeleteImage(IUser user, string imageName, out string deleteResponse);
	}
}