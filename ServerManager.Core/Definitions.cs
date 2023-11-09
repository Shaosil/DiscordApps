namespace ServerManager.Core
{
	public class QueueNames
	{
		public const string COMMAND_QUEUE = "server-manager-commands";
		public const string COMMAND_RESPONSE_QUEUE = "server-manager-commands-callback";
	}

	public static class SupportedCommands
	{
		public static class BDS
		{
			public const string Status = "status";
			public const string Startup = "startup";
			public const string ListPlayers = "list-players";
			public const string Shutdown = "shutdown";
			public const string Logs = "logs";
		}
	}
}