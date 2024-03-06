namespace ServerManager.Core
{
	public class QueueNames
	{
		public const string COMMAND_QUEUE = "server-manager-commands";
	}

	// Make sure all these are lower case so the switch statements can function
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

		public static class InvokeAI
		{
			public const string Status = "status";
			public const string Startup = "startup";
			public const string Shutdown = "shutdown";
		}
	}
}