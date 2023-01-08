using System.Text.Json.Serialization;

namespace ShaosilBot.Core.Models
{
	public class ImgurData
    {
        [JsonPropertyName("data")]
        public List<ImgurImage> Images { get; set; }

        public class ImgurImage
        {
            public string id { get; set; }
            public string title { get; set; }
            public string description { get; set; }
            public int datetime { get; set; }
            public string type { get; set; }
            public bool animated { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public int size { get; set; }
            public int views { get; set; }
            public int bandwidth { get; set; }
            public string vote { get; set; }
            public bool favorite { get; set; }
            public bool? nsfw { get; set; }
            public string section { get; set; }
            public string account_url { get; set; }
            public string account_id { get; set; }
            public bool is_ad { get; set; }
            public bool in_most_viral { get; set; }
            public bool has_sound { get; set; }
            public List<string> tags { get; set; }
            public int ad_type { get; set; }
            public string ad_url { get; set; }
            public string edited { get; set; }
            public bool in_gallery { get; set; }
            public string link { get; set; }
            public string gifv { get; set; }
            public string mp4 { get; set; }
            public int? mp4_size { get; set; }
            public bool? looping { get; set; }
        }
    }
}