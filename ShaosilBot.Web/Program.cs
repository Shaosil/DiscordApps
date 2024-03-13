using Discord;
using Discord.Rest;
using Discord.WebSocket;
using Microsoft.AspNetCore.HttpLogging;
using OpenAI.Extensions;
using OpenAI.ObjectModels;
using Quartz;
using Quartz.AspNetCore;
using Serilog;
using ServerManager.Core;
using ServerManager.Core.Interfaces;
using ShaosilBot.Core.Interfaces;
using ShaosilBot.Core.Providers;
using ShaosilBot.Core.Singletons;
using ShaosilBot.Core.SlashCommands;
using ShaosilBot.Web.CustomAuth;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseIIS();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddScoped<UtilitiesAuthorizationAttribute>();
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
builder.Services.AddSingleton<IRabbitMQProvider, RabbitMQProvider>();
builder.Services.AddSingleton<ISlashCommandProvider, SlashCommandProvider>();
builder.Services.AddSingleton<IChatGPTProvider, ChatGPTProvider>();
builder.Services.AddSingleton<IGuildHelper, GuildHelper>();
builder.Services.AddSingleton<IQuartzProvider, QuartzProvider>();
builder.Services.AddSingleton<IImageGenerationProvider, InvokeAIProvider>();

// Add scoped services, including all derivitives of BaseCommand
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

builder.Services.AddOpenAIService(a =>
{
	a.ApiKey = builder.Configuration.GetValue<string>("OpenAIAPIKey") ?? string.Empty;
	a.Organization = builder.Configuration.GetValue<string>("OpenAIOrganization") ?? string.Empty;
	a.DefaultModelId = Models.Gpt_3_5_Turbo;
});

builder.Services.AddQuartz(c =>
{
	c.UsePersistentStore(s =>
	{
		s.UseProperties = true;
		s.PerformSchemaValidation = false;
		s.UseMicrosoftSQLite($"Data Source={Path.Combine(builder.Configuration.GetValue<string>("FilesBasePath")!, "data.db")}");
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
Log.Logger = new LoggerConfiguration()
	.MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
	.MinimumLevel.Override("Quartz", Serilog.Events.LogEventLevel.Information)
	.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
	.Enrich.WithThreadId()
	.WriteTo.Console()
	.WriteTo.File("../Logs/Applog-.txt", outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss:fff} ({ThreadId}) [{Level:u3}] {Message:lj}{NewLine}{Exception}",
		rollingInterval: RollingInterval.Day, retainedFileTimeLimit: TimeSpan.FromDays(7))
	.CreateLogger();
builder.Host.UseSerilog();

// Build and configure
var app = builder.Build();
//app.UseHttpLogging(); // Enable for detailed HTTP logging at a slight performance cost
bool isDev = app.Environment.IsDevelopment();
if (!isDev) app.UseHsts().UseHttpsRedirection();
app.MapControllers();

// Ensure all SQLite tables are up to date
using (var scope = app.Services.CreateScope())
{
	scope.ServiceProvider.GetRequiredService<ISQLiteProvider>().UpdateSchema();
}

// Init the Quartz scheduler jobs if not in development mode
if (!isDev) app.Services.GetRequiredService<IQuartzProvider>().SetupPersistantJobs();

// Init the necessary components and launch the app
await app.Services.GetRequiredService<IDiscordRestClientProvider>().Init();
await app.Services.GetRequiredService<IDiscordSocketClientProvider>().Init(isDev);
app.Run();