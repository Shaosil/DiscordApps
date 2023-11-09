using ServerManager.Core.Models;
using static ServerManager.Core.Models.QueueMessage;

namespace ShaosilBot.Core.Interfaces
{
	public interface IRabbitMQProvider
	{
		Task<QueueMessageResponse> SendCommand(eCommandType commandType, string instructions, object[] args);
	}
}