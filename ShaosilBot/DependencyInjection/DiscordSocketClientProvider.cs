using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShaosilBot.SlashCommands;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ShaosilBot.DependencyInjection
{
    public class DiscordSocketClientProvider
    {
        private readonly static DiscordSocketConfig _config = new DiscordSocketConfig { GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.DirectMessages };
        private readonly static DiscordSocketClient _client = new DiscordSocketClient(_config);
        private static HttpClient _httpClient;

        public static DiscordSocketClient GetSocketClient(IServiceProvider provider)
        {
            if (_httpClient == null)
                _httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient();

            if (_client.LoginState == LoginState.LoggedOut)
            {
                bool ready = false;
                var logger = provider.GetService<ILogger<DiscordSocketClient>>();
                // Todo: will this get hit more than once?
                _client.Log += async (msg) => await Task.Run(() => logger.LogInformation($"SOCKET CLIENT: {msg}"));
                _client.Ready += async () =>
                {
                    _ = KeepAlive();
                    ready = true;

                    // Uncomment to refresh commands
                    //await SyncCommands();
                };
                _client.MessageReceived += MessageHandler;

                _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BotToken")).GetAwaiter().GetResult();
                _client.StartAsync().GetAwaiter().GetResult();

                // Stall here until socket client is ready
                while (!ready) { Thread.Sleep(10); }
            }

            return _client;
        }

        public static async Task KeepAlive()
        {
            // Loop every 9 minutes and update our status to keep the functions app 
            while (true)
            {
                await _client.SetGameAsync("with his robot junk");
                Thread.Sleep(TimeSpan.FromMinutes(9));
            }
        }

        private static async Task MessageHandler(SocketMessage socketMessage)
        {
            if (socketMessage.Channel.GetChannelType() == ChannelType.DM && !socketMessage.Author.IsBot && socketMessage.Content.ToUpper().Contains("STOP"))
            {
                // Update times unsubscribed lol
                var currentSubscribers = await CatFactsCommand.GetSubscribersAsync(_httpClient);
                var matchingSubscriber = currentSubscribers.FirstOrDefault(s => s.IDNum == socketMessage.Author.Id);
                if (matchingSubscriber != null)
                {
                    matchingSubscriber.TimesUnsubscribed++;
                    await CatFactsCommand.UpdateSubscribersAsync(currentSubscribers, _httpClient);
                }
                string extraMessage = matchingSubscriber?.TimesUnsubscribed > 1 ? $" (Wowza! You have subscribed {matchingSubscriber.TimesUnsubscribed} times!) " : string.Empty;
                //await foreach (var message in socketMessage.Channel.GetMessagesAsync().Flatten())
                //{
                //    if (message.Author.IsBot)
                //        await message.DeleteAsync();
                //}
                await socketMessage.Author.SendMessageAsync($"Thanks for subscribing to Cat Facts Digest (CFD){extraMessage}! Be prepared to boost that feline knowledge every hour, on the hour, between the hours of 10 AM and 5:00 PM EST! *Meow!*");
            }
        }

        private async static Task SyncCommands()
        {
            var guild = _client.GetGuild(628019972316069890);
            await guild.DeleteApplicationCommandsAsync();
            await guild.CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "test-command", Description = "Getting closer to world domination", DefaultMemberPermissions = GuildPermission.Administrator }.Build());
            await guild.CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "wow", Description = "Wow." }.Build());
            await guild.CreateApplicationCommandAsync(new SlashCommandBuilder() { Name = "cat-fact", Description = "Thank you for subscribing to cat facts! Text STOP to unsubscribe." }.Build());
        }
    }
}
