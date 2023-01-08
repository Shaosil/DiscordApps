namespace ShaosilBot.Core.Models
{
	public class ChannelVisibility
	{
		public string Description { get; set; }
		public ulong MessageID { get; set; }
		public ulong Role { get; set; }
		public List<Mapping> Mappings { get; set; }

		public class Mapping
		{
			public string Emoji { get; set; }
			public List<ulong> Channels { get; set; }
		}
	}
}