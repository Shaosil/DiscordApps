namespace ShaosilBot.Core.Models
{
	public class ChannelVisibility
	{
		public const string SelectMenuID = "ChannelVisibilitiesSelect";

		public string Description { get; set; }
		public ulong MessageID { get; set; }
		public ulong Role { get; set; }
		public List<Mapping> Mappings { get; set; } = new();

		public class Mapping
		{
			public string Value { get; set; }
			public List<ulong> Channels { get; set; }
		}
	}
}