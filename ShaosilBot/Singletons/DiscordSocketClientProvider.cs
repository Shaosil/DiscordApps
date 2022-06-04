using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using ShaosilBot.Providers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShaosilBot.Singletons
{
    public class DiscordSocketClientProvider
    {
        private readonly ILogger<DiscordSocketClientProvider> _logger;
        private readonly CatFactsProvider _catFactsProvider;
        public DiscordSocketClient Client { get; private set; }

        public DiscordSocketClientProvider(ILogger<DiscordSocketClientProvider> logger, DiscordSocketConfig config, CatFactsProvider catFactsProvider)
        {
            _logger = logger;
            _catFactsProvider = catFactsProvider;
            Client = new DiscordSocketClient(config);

            // Initialize bot and login
            Client.Log += async (msg) => await Task.Run(() => LogSocketMessage(msg));
            Client.Ready += async () =>
            {
                await KeepAlive();
                await SyncCommands();
            };
            Client.MessageReceived += MessageHandler;

            Client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BotToken")).GetAwaiter().GetResult();
            Client.StartAsync().GetAwaiter().GetResult();
        }

        public async Task KeepAlive()
        {
            await Client.SetGameAsync("favorites");
        }

        private void LogSocketMessage(LogMessage message)
        {
            var sb = new StringBuilder($"SOCKET CLIENT: {message.Message}");
            if (message.Exception != null)
                _logger.LogError(message.Exception, sb.ToString());
            else
            {
                switch (message.Severity)
                {
                    case LogSeverity.Debug: _logger.LogTrace(sb.ToString()); break;
                    case LogSeverity.Verbose: _logger.LogDebug(sb.ToString()); break;
                    case LogSeverity.Info: _logger.LogInformation(sb.ToString()); break;
                    case LogSeverity.Warning: _logger.LogWarning(sb.ToString()); break;
                    case LogSeverity.Error: _logger.LogError(sb.ToString()); break;
                }
            }
        }

        private async Task MessageHandler(SocketMessage socketMessage)
        {
            bool containsStop = socketMessage.Content.ToUpper().Contains("STOP");
            bool containsUnsub = socketMessage.Content.ToUpper().Contains("UNSUB");
            bool containsResub = socketMessage.Content.ToUpper().Contains("RESUB");

            if (socketMessage.Channel.GetChannelType() == ChannelType.DM && !socketMessage.Author.IsBot)
            {
                // Update times unsubscribed lol
                var currentSubscribers = await _catFactsProvider.GetSubscribersAsync(true);
                var matchingSubscriber = currentSubscribers.FirstOrDefault(s => s.ID == socketMessage.Author.Id);
                if (matchingSubscriber != null)
                {
                    if (matchingSubscriber.CurrentlySubbed)
                    {
                        if (containsUnsub)
                        {
                            matchingSubscriber.CurrentlySubbed = false;
                            await socketMessage.Author.SendMessageAsync("Successfully unsubscribed. Text RESUB at any time to resubscribe. Take care meow!");
                            await _catFactsProvider.UpdateSubscribersAsync(currentSubscribers);
                        }
                        else if (containsStop)
                        {
                            matchingSubscriber.TimesUnsubscribed++;
                            string extraMessage = matchingSubscriber?.TimesUnsubscribed > 1 ? $" (Wowza! You have subscribed {matchingSubscriber.TimesUnsubscribed} times!) " : string.Empty;
                            await socketMessage.Author.SendMessageAsync($"Thanks for subscribing to Cat Facts Digest (CFD){extraMessage}! Be prepared to boost that feline knowledge every hour, on the hour, between the hours of 10:00 AM and 10:00 PM EST! *Meow!*");
                            await _catFactsProvider.UpdateSubscribersAsync(currentSubscribers);
                        }
                    }
                    else if (containsResub)
                    {
                        matchingSubscriber.CurrentlySubbed = true;
                        await socketMessage.Author.SendMessageAsync($"Successfully resubscribed. Welcome back to the wonderful world of cat facts! Here's a bonus one to kickstart you again: {await _catFactsProvider.GetRandomCatFact()}");
                        await _catFactsProvider.UpdateSubscribersAsync(currentSubscribers);
                    }
                }
            }
        }

        private async Task SyncCommands()
        {
            var guilds = Client.Guilds;

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
                await guild.CreateApplicationCommandAsync(new SlashCommandBuilder
                { 
                    Name = "git-blame",
                    Description = "Blame a random or specific user.",
                    Options = new List<SlashCommandOptionBuilder>
                    {
                        new SlashCommandOptionBuilder { Name = "target-user", Type = ApplicationCommandOptionType.User, Description = "Blame someone specific" },
                        new SlashCommandOptionBuilder { Name = "functions", Type = ApplicationCommandOptionType.Integer, Description = "Extra utility functions",
                            Choices = new List<ApplicationCommandOptionChoiceProperties>
                            {
                                new ApplicationCommandOptionChoiceProperties { Name = "Toggle Subscription", Value = 0 },
                                new ApplicationCommandOptionChoiceProperties { Name = "List Blameables", Value = 1 }
                            }
                        }
                    }
                }.Build());
                await guild.CreateApplicationCommandAsync(new SlashCommandBuilder { Name = "whackabot", Description = "Starts or continues an epic smackdown!",
                    Options = new List<SlashCommandOptionBuilder> { new SlashCommandOptionBuilder { Name = "weapon-change", Type = ApplicationCommandOptionType.String, Description = "Choose your weapon" } }
                }.Build());
            }
        }
    }
}