﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Commands.Converters;
using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.ThumbScrapper;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using Microsoft.Extensions.DependencyInjection;

namespace CompatBot
{
    internal static class Program
    {
        private static readonly SemaphoreSlim InstanceCheck = new SemaphoreSlim(0, 1);
        private static readonly SemaphoreSlim ShutdownCheck = new SemaphoreSlim(0, 1);

        internal static async Task Main(string[] args)
        {
            var thread = new Thread(() =>
                                    {
                                        using (var instanceLock = new Mutex(false, @"Global\RPCS3 Compatibility Bot"))
                                        {
                                            if (instanceLock.WaitOne(1000))
                                                try
                                                {
                                                    InstanceCheck.Release();
                                                    ShutdownCheck.Wait();
                                                }
                                                finally
                                                {
                                                    instanceLock.ReleaseMutex();
                                                }
                                        }
                                    });
            try
            {
                thread.Start();
                if (!InstanceCheck.Wait(1000))
                {
                    Console.WriteLine("Another instance is already running.");
                    return;
                }

                if (string.IsNullOrEmpty(Config.Token))
                {
                    Console.WriteLine("No token was specified.");
                    return;
                }

                using (var db = new BotDb())
                    if (!await DbImporter.UpgradeAsync(db, Config.Cts.Token))
                        return;

                using (var db = new ThumbnailDb())
                    if (!await DbImporter.UpgradeAsync(db, Config.Cts.Token))
                        return;

                var psnScrappingTask = new PsnScraper().Run(Config.Cts.Token);

                var config = new DiscordConfiguration
                {
                    Token = Config.Token,
                    TokenType = TokenType.Bot,
                    UseInternalLogHandler = true,
                    //LogLevel = LogLevel.Debug,
                };

                using (var client = new DiscordClient(config))
                {
                    var commands = client.UseCommandsNext(new CommandsNextConfiguration
                    {
                        StringPrefixes = new[] {Config.CommandPrefix},
                        Services = new ServiceCollection().BuildServiceProvider(),
                    });
                    commands.RegisterConverter(new CustomDiscordChannelConverter());
                    commands.RegisterCommands<Misc>();
                    commands.RegisterCommands<CompatList>();
                    commands.RegisterCommands<Sudo>();
                    commands.RegisterCommands<Antipiracy>();
                    commands.RegisterCommands<Warnings>();
                    commands.RegisterCommands<Explain>();
                    commands.RegisterCommands<Psn>();
                    commands.RegisterCommands<Invites>();

                    client.Ready += async r =>
                                    {
                                        Console.WriteLine("Bot is ready to serve!");
                                        Console.WriteLine();
                                        Console.WriteLine($"Bot user id : {r.Client.CurrentUser.Id} ({r.Client.CurrentUser.Username})");
                                        Console.WriteLine($"Bot admin id : {Config.BotAdminId} ({(await r.Client.GetUserAsync(Config.BotAdminId)).Username})");
                                        Console.WriteLine();
                                    };
                    client.GuildAvailable += async gaArgs =>
                                             {
                                                 gaArgs.Client.DebugLogger.LogMessage(LogLevel.Info, "", $"{gaArgs.Guild.Name} is available now", DateTime.Now);
                                                 gaArgs.Client.DebugLogger.LogMessage(LogLevel.Info, "", $"Checking moderation backlogs in {gaArgs.Guild.Name}...", DateTime.Now);
                                                 await Task.WhenAll(
                                                     Starbucks.CheckBacklogAsync(gaArgs.Client, gaArgs.Guild).ContinueWith(_ => Console.WriteLine($"Starbucks backlog checked in {gaArgs.Guild.Name}.")),
                                                     DiscordInviteFilter.CheckBacklogAsync(gaArgs.Client, gaArgs.Guild).ContinueWith(_ => Console.WriteLine($"Discord invites backlog checked in {gaArgs.Guild.Name}."))
                                                 ).ConfigureAwait(false);
                                                 Console.WriteLine($"All moderation backlogs checked in {gaArgs.Guild.Name}.");
                                             };
                    client.GuildUnavailable += guArgs =>
                                               {
                                                   guArgs.Client.DebugLogger.LogMessage(LogLevel.Warning, "", $"{guArgs.Guild.Name} is unavailable", DateTime.Now);
                                                   return Task.CompletedTask;
                                               };


                    client.MessageReactionAdded += Starbucks.Handler;

                    client.MessageCreated += AntipiracyMonitor.OnMessageCreated; // should be first
                    client.MessageCreated += ProductCodeLookup.OnMessageCreated;
                    client.MessageCreated += LogInfoHandler.OnMessageCreated;
                    client.MessageCreated += LogsAsTextMonitor.OnMessageCreated;
                    client.MessageCreated += DiscordInviteFilter.OnMessageCreated;
                    client.MessageCreated += BotShutupHandler.OnMessageCreated;

                    client.MessageUpdated += AntipiracyMonitor.OnMessageUpdated;
                    client.MessageUpdated += DiscordInviteFilter.OnMessageUpdated;

                    client.MessageDeleted += ThumbnailCacheMonitor.OnMessageDeleted;

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
                await psnScrappingTask.ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
            }
            finally
            {
                ShutdownCheck.Release();
                thread.Join(100);
                Console.WriteLine("Exiting");
            }
        }
    }
}
