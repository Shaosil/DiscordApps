using Discord;
using Discord.WebSocket;
using Microsoft.AspNetCore.HttpLogging;
using OpenAI.GPT3.Extensions;
using Quartz;
using Serilog;
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
builder.Services.AddHttpClient();
builder.Services.AddSingleton<IFileAccessHelper, FileAccessHelper>();
builder.Services.AddSingleton((sp) => new DiscordSocketConfig { GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildMessageReactions | GatewayIntents.GuildMessages | GatewayIntents.MessageContent });
builder.Services.AddSingleton<IDiscordGatewayMessageHandler, DiscordGatewayMessageHandler>();
builder.Services.AddSingleton<IDiscordSocketClientProvider, DiscordSocketClientProvider>();
builder.Services.AddSingleton<IDiscordRestClientProvider, DiscordRestClientProvider>();
builder.Services.AddSingleton<ISlashCommandProvider, SlashCommandProvider>();
builder.Services.AddSingleton<IChatGPTProvider, ChatGPTProvider>();
builder.Services.AddSingleton<IGuildHelper, GuildHelper>();
builder.Services.AddSingleton<IQuartzProvider, QuartzProvider>();

// Add scoped services, including all derivitives of BaseCommand
builder.Services.AddScoped<IHttpUtilities, HttpUtilities>();
builder.Services.AddScoped<SlashCommandWrapper>();
builder.Services.AddScoped<ITwitchProvider, TwitchProvider>();
var derivedCommandTypes = typeof(BaseCommand).Assembly.DefinedTypes.Where(t => t.BaseType == typeof(BaseCommand)).ToList();
foreach (var commandType in derivedCommandTypes)
{
	builder.Services.AddScoped(commandType);
}

builder.Services.AddOpenAIService(a =>
{
	a.ApiKey = builder.Configuration.GetValue<string>("OpenAIAPIKey") ?? string.Empty;
	a.Organization = builder.Configuration.GetValue<string>("OpenAIOrganization") ?? string.Empty;
	a.DefaultModelId = "gpt-3.5-turbo";
});

builder.Services.AddQuartz(c =>
{
	c.UseMicrosoftDependencyInjectionJobFactory();

	c.UsePersistentStore(s =>
	{
		string connString = $"Data Source={Path.Combine(builder.Configuration.GetValue<string>("FilesBasePath")!, "quartz.db")}";
		QuartzProvider.EnsureSchemaExists(connString); // Will create DB file and tables if needed

		s.UseProperties = true;
		s.UseMicrosoftSQLite(connString);
		s.UseJsonSerializer();
	});

}).AddQuartzServer(c => { c.WaitForJobsToComplete = true; });

builder.Services.AddHttpLogging(logging =>
{
	logging.LoggingFields = HttpLoggingFields.All;
	logging.RequestBodyLogLimit = 4096;
	logging.ResponseBodyLogLimit = 4096;
});

// Logging
Log.Logger = new LoggerConfiguration()
	.MinimumLevel.Information()
	.Enrich.WithThreadId()
	.WriteTo.Console()
	.WriteTo.File("../Logs/Applog-.txt", outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss:fff} ({ThreadId}) [{Level:u3}] {Message:lj}{NewLine}{Exception}",
		rollingInterval: RollingInterval.Day, retainedFileTimeLimit: TimeSpan.FromDays(7))
	.CreateLogger();
builder.Host.UseSerilog();

// Build and configure
var app = builder.Build();
app.UseHttpLogging(); // Enable for detailed HTTP logging at a slight performance cost
bool isDev = app.Environment.IsDevelopment();
if (!isDev) app.UseHsts().UseHttpsRedirection();
app.MapControllers();

// Init the Quartz scheduler jobs if not in development mode
if (!isDev) app.Services.GetRequiredService<IQuartzProvider>().SetupPersistantJobs();

// Init the websocket and rest clients
app.Services.GetService<IDiscordRestClientProvider>()!.Init();
app.Services.GetService<IDiscordSocketClientProvider>()!.Init(isDev);
DiscordSocketClientProvider.Client.Ready += async () => await app.Services.GetService<ISlashCommandProvider>()!.BuildGuildCommands();

app.Run();