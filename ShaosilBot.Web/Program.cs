using System.Globalization;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Docker.DotNet;
using Microsoft.AspNetCore.HttpLogging;
using Quartz;
using Quartz.AspNetCore;
using Serilog;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using ShaosilBot.Core.Singletons;
using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Web.CustomAuth;
// Configure root paths
var builder = WebApplication.CreateBuilder(args);
string? basePath = builder.Configuration.GetValue<string>("FilesBasePath");
string? logPath = builder.Configuration.GetValue<string>("LoggingPath");
if (basePath == null || !Directory.CreateDirectory(basePath).Exists)
{
	throw new Exception("Startup aborted - FilesBasePath does not exist! Check appsettings.json is configured correctly.");
}

// Configure Kestrel
builder.WebHost.ConfigureKestrel((context, options) =>
{
	options.ListenAnyIP(5000);
});

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddHttpClient();

// Singletons
builder.Services.AddSingleton<IFileAccessHelper, FileAccessHelper>();
builder.Services.AddSingleton((sp) => new DiscordSocketConfig
{
	GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildMessageReactions | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
	LogLevel = LogSeverity.Debug
});
builder.Services.AddSingleton((sp) => new DiscordRestConfig
{
	LogLevel = LogSeverity.Debug
});
builder.Services.AddSingleton<IDiscordGatewayMessageHandler, DiscordGatewayMessageHandler>();
builder.Services.AddSingleton<IDiscordSocketClientProvider, DiscordSocketClientProvider>();
builder.Services.AddSingleton<IDiscordRestClientProvider, DiscordRestClientProvider>();
builder.Services.AddSingleton(_ => new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient());
builder.Services.AddSingleton<IDockerProvider, DockerProvider>();
builder.Services.AddSingleton<ISlashCommandProvider, SlashCommandProvider>();
builder.Services.AddSingleton<IChatGPTConnection, ChatGPTConnection>();
builder.Services.AddSingleton<IChatGPTProvider, ChatGPTProvider>();
builder.Services.AddSingleton<IGuildHelper, GuildHelper>();
builder.Services.AddSingleton<IQuartzProvider, QuartzProvider>();
builder.Services.AddSingleton<IImageGenerationProvider, ComfyUIProvider>();

// Add scoped services, including all derivitives of BaseCommand
builder.Services.AddScoped<UtilitiesAuthorizationAttribute>();
builder.Services.AddScoped<ISQLiteProvider, SQLiteProvider>();
builder.Services.AddScoped<IMessageCommandProvider, MessageCommandProvider>();
builder.Services.AddScoped<IHttpUtilities, HttpUtilities>();
builder.Services.AddScoped<SlashCommandWrapper>();
builder.Services.AddScoped<ITwitchProvider, TwitchProvider>();
builder.Services.AddScoped<IGameDealSearchProvider, GameDealSearchProvider>();
var derivedCommandTypes = typeof(BaseCommand).Assembly.DefinedTypes.Where(t => t.BaseType == typeof(BaseCommand)).ToList();
foreach (var commandType in derivedCommandTypes)
{
	builder.Services.AddScoped(commandType);
}

builder.Services.AddQuartz(c =>
{
	c.UsePersistentStore(s =>
	{
		s.UseProperties = true;
		s.PerformSchemaValidation = false;
		s.UseMicrosoftSQLite($"Data Source={Path.Combine(basePath, "data.db")}");
		s.UseNewtonsoftJsonSerializer();
	});

})
.AddQuartzServer(c => { c.WaitForJobsToComplete = true; })
.Configure<QuartzOptions>(q =>
{
	q.Add("quartz.jobStore.acquireTriggersWithinLock", "true");
	q.Add("quartz.jobStore.txIsolationLevelSerializable", "true");
	q.Add("quartz.jobStore.lockHandler.type", typeof(Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore).AssemblyQualifiedName);
});

builder.Services.AddHttpLogging(logging =>
{
	logging.LoggingFields = HttpLoggingFields.All;
	logging.RequestBodyLogLimit = 4096;
	logging.ResponseBodyLogLimit = 4096;
});

// Logging
if (!string.IsNullOrWhiteSpace(logPath))
{
	Log.Logger = new LoggerConfiguration()
		.MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
		.MinimumLevel.Override("Quartz", Serilog.Events.LogEventLevel.Information)
		.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
		.MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
		.Enrich.WithThreadId()
		.WriteTo.Console()
		.WriteTo.File(Path.Combine(logPath, "Applog-.txt"), outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss:fff} ({ThreadId}) [{Level:u3}] {Message:lj}{NewLine}{Exception}",
			rollingInterval: RollingInterval.Day, retainedFileTimeLimit: TimeSpan.FromDays(7))
		.CreateLogger();
	builder.Host.UseSerilog();
}
else
{
	Console.WriteLine("Warning - no logging path specified, logging to file will be disabled!");
}

// Build and configure
var app = builder.Build();
//app.UseHttpLogging(); // Enable for detailed HTTP logging at a slight performance cost
bool isDev = app.Environment.IsDevelopment();
app.MapControllers();
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");

// Ensure all SQLite tables are up to date
using (var scope = app.Services.CreateScope())
{
	scope.ServiceProvider.GetRequiredService<ISQLiteProvider>().UpdateSchema();
}

// Init the Quartz scheduler jobs if not in development mode
if (!isDev) app.Services.GetRequiredService<IQuartzProvider>().SetupPersistantJobs();

// Init the necessary components and launch the app
app.Services.GetRequiredService<IFileAccessHelper>().InitDataDirectory();
await app.Services.GetRequiredService<IDiscordRestClientProvider>().Init();
await app.Services.GetRequiredService<IDiscordSocketClientProvider>().Init(isDev);
app.Run();