using ServerManager.Core.Models;

namespace ServerManager.Core.Interfaces
{
	public interface IServerManagerCommand
	{
		Task<QueueMessageResponse> Process(QueueMessage message);
	}
}