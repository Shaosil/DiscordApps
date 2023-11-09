using Serilog;
using ServerManager;
using ServerManager.Processors;

// Logging
string logLocation = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs/ServerManagerLog-.txt");
Log.Logger = new LoggerConfiguration()
	.Enrich.WithThreadId()
	.WriteTo.Console()
	.WriteTo.File(logLocation, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss:fff} ({ThreadId}) [{Level:u3}] {Message:lj}{NewLine}{Exception}",
		rollingInterval: RollingInterval.Day, retainedFileTimeLimit: TimeSpan.FromDays(3))
	.CreateLogger();

IHost host = Host.CreateDefaultBuilder(args)
	.ConfigureServices(services =>
	{
		services.AddSingleton<BDSCommand>();

		services.AddHostedService<CommandProcessor>();
		services.AddWindowsService();

		// Use the following ADMIN PowerShell commands to create the service after your first publish:
		/*
			sc.exe create ServerManager binpath="<YOUR PUBLISH LOCATION>" start=auto depend=RabbitMQ

			sc.exe description ServerManager "Uses RabbitMQ to handle various PC tasks."

			sc.exe start ServerManager
		*/

		// The following can be used in a .bat file for easy publishing:
		/*
			@echo off

			:: Stop the ServerManager service
			sc.exe stop ServerManager

			:: Execute dotnet publish command
			SET PROJ_PATH="<THIS PROJECT LOCATION>"
			dotnet publish %PROJ_PATH% --configuration Release --framework net7.0 --runtime win-x64 --output "<YOUR PUBLISH LOCATION>" --self-contained false

			:: Restart the ServerManager service
			sc.exe start ServerManager

			:: Pause to keep the command prompt open
			pause
		*/
	})
	.UseSerilog()
	.Build();

host.Run();