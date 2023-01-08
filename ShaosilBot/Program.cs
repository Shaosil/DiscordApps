using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace ShaosilBot
{
	public class Program
    {
        public static async Task Main()
        {
            var host = new HostBuilder().Build();

            await host.RunAsync();
        }
    }
}