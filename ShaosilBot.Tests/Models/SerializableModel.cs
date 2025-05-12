using Newtonsoft.Json;

namespace ShaosilBot.Tests.Models
{
	public abstract class SerializableModel { }

	public static class SerializableModelExtension
	{
		public static string Serialize<T>(this T model) where T : SerializableModel
		{
			return JsonConvert.SerializeObject(model, new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.Ignore });
		}
	}
}