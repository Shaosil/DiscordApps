using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShaosilBot.Interfaces;
using ShaosilBot.Middleware;
using ShaosilBot.Providers;
using ShaosilBot.Singletons;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace ShaosilBot
{
	public class Program
    {
        public static async Task Main()
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults(app =>
                {
                    app.UseWhen<TwitchMiddleware>(context => context.FunctionDefinition.EntryPoint == $"{typeof(TwitchCallback).FullName}.Run");
                })
                .ConfigureServices((context, services) =>
                {
                    // Singletons
                    services.AddHttpClient();
                    services.AddSingleton<IDataBlobProvider, DataBlobProvider>();
                    services.AddSingleton<IDiscordGatewayMessageHandler, DiscordGatewayMessageHandler>();
                    services.AddSingleton<IDiscordSocketClientProvider, DiscordSocketClientProvider>();
                    services.AddSingleton<IDiscordRestClientProvider, DiscordRestClientProvider>();
					services.AddSingleton<ISlashCommandProvider, SlashCommandProvider>();

					// Add scoped services of all derivitives of BaseCommand
					services.AddScoped<IHttpUtilities, HttpUtilities>();
					services.AddScoped<SlashCommandWrapper>();
					services.AddScoped<ITwitchMiddlewareHelper, TwitchMiddlewareHelper>();
                    services.AddScoped<ITwitchProvider, TwitchProvider>();
                    services.AddScoped((sp) => new DiscordSocketConfig { GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildMessageReactions });
                    var derivedCommandTypes = Assembly.GetExecutingAssembly().DefinedTypes.Where(t => t.BaseType == typeof(SlashCommands.BaseCommand)).ToList();
                    foreach (var commandType in derivedCommandTypes)
                    {
                        services.AddScoped(commandType);
                    }
                })
                .Build();

            await host.RunAsync();
        }
    }
}