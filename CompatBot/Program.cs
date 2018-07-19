using System;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Converters;
using CompatBot.Database;
using CompatBot.EventHandlers;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;

namespace CompatBot
{
    internal static class Program
    {
        internal static async Task Main(string[] args)
        {
            if (string.IsNullOrEmpty(Config.Token))
            {
                Console.WriteLine("No token was specified.");
                return;
            }

            if (!await DbImporter.UpgradeAsync(BotDb.Instance, Config.Cts.Token))
                return;


            var config = new DiscordConfiguration
            {
                Token = Config.Token,
                TokenType = TokenType.Bot,
                UseInternalLogHandler = true,
                //LogLevel = LogLevel.Debug,
            };

            using (var client = new DiscordClient(config))
            {
                var commands = client.UseCommandsNext(new CommandsNextConfiguration {StringPrefixes = new[] {Config.CommandPrefix}});
                commands.RegisterConverter(new CustomDiscordChannelConverter());
                commands.RegisterCommands<Misc>();
                commands.RegisterCommands<CompatList>();
                commands.RegisterCommands<Sudo>();
                commands.RegisterCommands<Antipiracy>();
                commands.RegisterCommands<Warnings>();
                commands.RegisterCommands<Explain>();

                client.Ready += async r =>
                                {
                                    Console.WriteLine("Bot is ready to serve!");
                                    Console.WriteLine();
                                    Console.WriteLine($"Bot user id : {r.Client.CurrentUser.Id} ({r.Client.CurrentUser.Username})");
                                    Console.WriteLine($"Bot admin id : {Config.BotAdminId} ({(await r.Client.GetUserAsync(Config.BotAdminId)).Username})");
                                    Console.WriteLine();
                                    Console.WriteLine("Checking starbucks backlog...");
                                    await r.Client.CheckBacklog().ConfigureAwait(false);
                                    Console.WriteLine("Starbucks checked.");
                                };
                client.MessageReactionAdded += Starbucks.Handler;

                client.MessageCreated += AntipiracyMonitor.OnMessageCreated; // should be first
                client.MessageCreated += ProductCodeLookup.OnMessageMention;
                client.MessageCreated += LogInfoHandler.OnMessageCreated;
                client.MessageCreated += LogsAsTextMonitor.OnMessageCreated;

                client.MessageUpdated += AntipiracyMonitor.OnMessageEdit;

                try
                {
                    await client.ConnectAsync();
                }
                catch (Exception e)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(e.Message);
                    Console.ResetColor();
                    Console.WriteLine("Terminating.");
                    return;
                }

                if (args.Length > 1 && ulong.TryParse(args[1], out var channelId))
                {
                    Console.WriteLine("Found channelId: " + args[1]);
                    var channel = await client.GetChannelAsync(channelId).ConfigureAwait(false);
                    await channel.SendMessageAsync("Bot is up and running").ConfigureAwait(false);
                }


                while (!Config.Cts.IsCancellationRequested)
                {
                    if (client.Ping > 1000)
                        await client.ReconnectAsync();
                    await Task.Delay(TimeSpan.FromMinutes(1), Config.Cts.Token).ContinueWith(dt => {/* in case it was cancelled */}).ConfigureAwait(false);
                }
            }
            Console.WriteLine("Exiting");
        }
    }
}
