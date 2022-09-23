namespace ShaosilBot.Tests
{
	public static class HelperExtensions
	{
		public static ulong NextULong(this Random random)
		{
			var ulongBytes = new byte[8];
			random.NextBytes(ulongBytes);
			return BitConverter.ToUInt64(ulongBytes);
		}
	}
}