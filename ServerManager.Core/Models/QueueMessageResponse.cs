namespace ServerManager.Core.Models
{
	[Serializable]
	public class QueueMessageResponse : SerializableMessage<QueueMessageResponse>
	{
		public string Response { get; private set; }

		public QueueMessageResponse(string response)
		{
			Response = response;
		}
	}
}