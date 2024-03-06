using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServerManager.Core;
using ServerManager.Core.Models;
using static ServerManager.Core.Models.QueueMessage;

string commandType = args.Length > 0 ? args[0] : string.Empty;
string command = args.Length > 1 ? args[1] : string.Empty;
string[] commandArgs = args.Skip(2).ToArray();

// Collect command type if not passed
if (string.IsNullOrWhiteSpace(commandType))
{
	Console.Write("Command type: ");
	commandType = Console.ReadLine() ?? string.Empty;
}

// Validate this is a supported command type
if (!Enum.TryParse<eCommandType>(commandType, true, out var targetCommandType))
{
	ExitApp("ERROR: Unknown command type!");
}

// Collect command if not passed
if (string.IsNullOrWhiteSpace(command))
{
	Console.Write("Command: ");
	command = Console.ReadLine() ?? string.Empty;
}

Console.WriteLine($"Sending {targetCommandType}.{command} command with {commandArgs.Length} args...");
Console.WriteLine();

var services = new ServiceCollection();
services.AddLogging(l =>
{
	l.AddConsole();
});
var provider = services.BuildServiceProvider();
var rabbitMQ = new RabbitMQProvider(provider.GetRequiredService<ILogger<RabbitMQProvider>>());

// Wait no longer than 60 seconds
Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
var completedTask = await Task.WhenAny(rabbitMQ.SendCommand(targetCommandType, command, args), timeoutTask);

Console.WriteLine();
if (completedTask == timeoutTask)
{
	ExitApp("Timeout while waiting for response.");
}
else
{
	var result = ((Task<QueueMessageResponse>)completedTask).Result;
	ExitApp($"Response from server:\n\n{result.Response}");
}

void ExitApp(string message)
{
	Console.WriteLine(message);
	Console.WriteLine();
	Console.Write("Press any key to continue...");
	Console.ReadKey();

	Environment.Exit(0);
}