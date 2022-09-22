using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShaosilBot.Tests.Models
{
    public abstract class SerializableModel { }
    
    public static class SerializableModelExtension
    {
        public static string Serialize<T>(this T model) where T : SerializableModel
        {
            return JsonSerializer.Serialize(model, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault });
        }
    }
}