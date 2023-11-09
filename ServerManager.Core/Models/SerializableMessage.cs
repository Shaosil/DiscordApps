using Newtonsoft.Json;
using System.Text;

namespace ServerManager.Core.Models
{
	[Serializable]
	public abstract class SerializableMessage<T> where T : class
	{
		public byte[] Serialize()
		{
			return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this, Formatting.Indented));
		}

		public static T Deserialize(byte[] bytes)
		{
			string json = Encoding.UTF8.GetString(bytes);
			return JsonConvert.DeserializeObject<T>(json)!;
		}
	}
}