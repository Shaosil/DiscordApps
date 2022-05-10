using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShaosilBot.DependencyInjection;
using System.Threading.Tasks;

namespace ShaosilBot
{
    public class Program
    {
        public static async Task Main()
        {
            var host = new HostBuilder()
                .ConfigureFunctionsWorkerDefaults()
                .ConfigureServices((context, services) =>
                {
                    services.AddHttpClient();

                    services.AddScoped((d) => new DiscordSocketConfig { GatewayIntents = GatewayIntents.DirectMessages });
                    services.AddSingleton(sp => DiscordSocketClientProvider.GetSocketClient(sp));
                    services.AddSingleton(sp => DiscordRestClientProvider.GetRestClient(sp));
                })
                .Build();

            await host.RunAsync();
        }
    }
}