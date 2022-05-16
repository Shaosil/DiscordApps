using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShaosilBot.SlashCommands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ShaosilBot.DependencyInjection
{
    public class DiscordSocketClientProvider
    {
        private readonly static DiscordSocketConfig _config = new DiscordSocketConfig { GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.DirectMessages };
        private readonly static DiscordSocketClient _client = new DiscordSocketClient(_config);

        public static DiscordSocketClient GetSocketClient(IServiceProvider provider)
        {
            if (_client.LoginState == LoginState.LoggedOut)
            {
                var logger = provider.GetService<ILogger<DiscordSocketClient>>();
                // Todo: will this get hit more than once?
                _client.Log += async (msg) => await Task.Run(() => logger.LogInformation($"SOCKET CLIENT: {msg}"));
                _client.Ready += async () =>
                {
                    await KeepAlive();
                    await SyncCommands();
                };
                _client.MessageReceived += MessageHandler;

                _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BotToken")).GetAwaiter().GetResult();
                _client.StartAsync().GetAwaiter().GetResult();
            }

            return _client;
        }

        public static async Task KeepAlive()
        {
            await _client.SetGameAsync("with his robot junk");
        }

        private static async Task MessageHandler(SocketMessage socketMessage)
        {
            if (socketMessage.Channel.GetChannelType() == ChannelType.DM && !socketMessage.Author.IsBot && socketMessage.Content.ToUpper().Contains("STOP"))
            {
                // Update times unsubscribed lol
                var currentSubscribers = await CatFactsCommand.GetSubscribersAsync();
                var matchingSubscriber = currentSubscribers.FirstOrDefault(s => s.IDNum == socketMessage.Author.Id);
                if (matchingSubscriber != null)
                {
                    matchingSubscriber.TimesUnsubscribed++;
                    await CatFactsCommand.UpdateSubscribersAsync(currentSubscribers);
                }
                string extraMessage = matchingSubscriber?.TimesUnsubscribed > 1 ? $" (Wowza! You have subscribed {matchingSubscriber.TimesUnsubscribed} times!) " : string.Empty;
                await socketMessage.Author.SendMessageAsync($"Thanks for subscribing to Cat Facts Digest (CFD){extraMessage}! Be prepared to boost that feline knowledge every hour, on the hour, between the hours of 10:00 AM and 10:00 PM EST! *Meow!*");
            }
        }

        private async static Task SyncCommands()
        {
            var guilds = _client.Guilds;

            foreach (var guild in guilds)
            {
                // For now, just create all commands if the guild has none
                var existingCommands = await guild.GetApplicationCommandsAsync();
                if (existingCommands.Count > 0) continue;

                await guild.DeleteApplicationCommandsAsync();
                await guild.CreateApplicationCommandAsync(new SlashCommandBuilder { Name = "test-command", Description = "Getting closer to world domination", DefaultMemberPermissions = GuildPermission.Administrator }.Build());
                await guild.CreateApplicationCommandAsync(new SlashCommandBuilder { Name = "wow", Description = "Wow." }.Build());
                await guild.CreateApplicationCommandAsync(new SlashCommandBuilder { Name = "cat-fact", Description = "Thank you for subscribing to cat facts! Text STOP to unsubscribe." }.Build());
                await guild.CreateApplicationCommandAsync(new SlashCommandBuilder
                {
                    Name = "xkcd",
                    Description = "Get a random XKCD comic, or optionally a specific one!",
                    Options = new List<SlashCommandOptionBuilder> { new SlashCommandOptionBuilder { Name = "comic-num", Type = ApplicationCommandOptionType.Integer, MinValue = 0, Description = "The number of the comic to pull. 0 for current. Omit for random." } }
                }.Build());
                await guild.CreateApplicationCommandAsync(new SlashCommandBuilder { Name = "git-blame", Description = "Blame a random one of (Shaosil, Syrelash, mbmminer, or Skom)" }.Build());
            }
        }
    }
}
