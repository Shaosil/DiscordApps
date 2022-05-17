using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShaosilBot.Providers;
using ShaosilBot.Singletons;
using System.Threading.Tasks;

namespace ShaosilBot
{
    public class Program
    {
        public static async Task Main()
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureLogging(builder =>
                {
                    builder.AddConsole();
                })
                .ConfigureServices((context, services) =>
                {
                    // Scoped
                    services.AddScoped<CatFactsProvider>();
                    services.AddScoped((sp) => new DiscordSocketConfig { GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.DirectMessages });

                    // Singletons
                    services.AddHttpClient();
                    services.AddSingleton<DataBlobProvider>();
                    services.AddSingleton<DiscordSocketClientProvider>();
                    services.AddSingleton<DiscordRestClientProvider>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}