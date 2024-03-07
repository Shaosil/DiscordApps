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
		services.AddHttpClient();

		services.AddSingleton<BDSCommand>();
		services.AddSingleton<InvokeAICommand>();

		services.AddHostedService<CommandProcessor>();
	})
	.UseSerilog()
	.Build();

host.Run();