namespace ServerManager.Core.Models
{
	[Serializable]
	public class QueueMessage : SerializableMessage<QueueMessage>
	{
		public enum eCommandType { BDS, InvokeAI }

		public eCommandType CommandType { get; set; }

		public string Instructions { get; set; }

		public object[] Arguments { get; set; }
	}
}