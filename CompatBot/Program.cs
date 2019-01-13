﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CompatBot.Commands;
using CompatBot.Commands.Converters;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.ThumbScrapper;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace CompatBot
{
    internal static class Program
    {
        private static readonly SemaphoreSlim InstanceCheck = new SemaphoreSlim(0, 1);
        private static readonly SemaphoreSlim ShutdownCheck = new SemaphoreSlim(0, 1);
        private const ulong InvalidChannelId = 13;

        internal static async Task Main(string[] args)
        {
            var singleInstanceCheckThread = new Thread(() =>
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
            var rpcs3UpdateCheckThread = new Thread(client =>
                                                    {
                                                        try
                                                        {
                                                            while (!Config.Cts.IsCancellationRequested)
                                                            {
                                                                try
                                                                {
                                                                    CompatList.UpdatesCheck.CheckForRpcs3Updates((DiscordClient)client, null).ConfigureAwait(false).GetAwaiter().GetResult();
                                                                }
                                                                catch { }
                                                                Task.Delay(TimeSpan.FromHours(1), Config.Cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
                                                            }
                                                        }
                                                        catch (TaskCanceledException) { }
                                                        catch (Exception e) { Config.Log.Error(e);}
                                                    }){ IsBackground = true };

            try
            {
                singleInstanceCheckThread.Start();
                if (!InstanceCheck.Wait(1000))
                {
                    Config.Log.Fatal("Another instance is already running.");
                    return;
                }

                if (string.IsNullOrEmpty(Config.Token))
                {
                    Config.Log.Fatal("No token was specified.");
                    return;
                }

                using (var db = new BotDb())
                    if (!await DbImporter.UpgradeAsync(db, Config.Cts.Token))
                        return;

                using (var db = new ThumbnailDb())
                    if (!await DbImporter.UpgradeAsync(db, Config.Cts.Token))
                        return;

                var backgroundTasks = Task.WhenAll(
                    AmdDriverVersionProvider.RefreshAsync(),
                    new PsnScraper().RunAsync(Config.Cts.Token),
                    GameTdbScraper.RunAsync(Config.Cts.Token),
                    new AppveyorClient.Client().GetBuildAsync(Guid.NewGuid().ToString(), Config.Cts.Token)
                );
                Config.Log.Debug($"Started background tasks with status {backgroundTasks.Status}");

                try
                {
                    if (!Directory.Exists(Config.IrdCachePath))
                        Directory.CreateDirectory(Config.IrdCachePath);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, $"Failed to create new folder {Config.IrdCachePath}: {e.Message}");
                }

                var config = new DiscordConfiguration
                {
                    Token = Config.Token,
                    TokenType = TokenType.Bot,
                    //UseInternalLogHandler = true,
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
                    commands.RegisterCommands<CommandsManagement>();
                    commands.RegisterCommands<Antipiracy>();
                    commands.RegisterCommands<Warnings>();
                    commands.RegisterCommands<Explain>();
                    commands.RegisterCommands<Psn>();
                    commands.RegisterCommands<Invites>();
                    commands.RegisterCommands<Moderation>();
                    commands.RegisterCommands<Ird>();
                    commands.RegisterCommands<BotMath>();
                    commands.RegisterCommands<Pr>();

                    client.Ready += async r =>
                                    {
                                        Config.Log.Info("Bot is ready to serve!");
                                        Config.Log.Info("");
                                        Config.Log.Info($"Bot user id : {r.Client.CurrentUser.Id} ({r.Client.CurrentUser.Username})");
                                        Config.Log.Info($"Bot admin id : {Config.BotAdminId} ({(await r.Client.GetUserAsync(Config.BotAdminId)).Username})");
                                        Config.Log.Info("");
                                    };
                    client.GuildAvailable += async gaArgs =>
                                             {
                                                 if (gaArgs.Guild.Id != Config.BotGuildId)
                                                 {
#if DEBUG
                                                     Config.Log.Warn($"Unknown discord server {gaArgs.Guild.Id} ({gaArgs.Guild.Name})");
#else
                                                     Config.Log.Warn($"Unknown discord server {gaArgs.Guild.Id} ({gaArgs.Guild.Name}), leaving...");
                                                     await gaArgs.Guild.LeaveAsync().ConfigureAwait(false);
#endif
                                                     return;
                                                 }

                                                 Config.Log.Info($"Server {gaArgs.Guild.Name} is available now");
                                                 Config.Log.Info($"Checking moderation backlogs in {gaArgs.Guild.Name}...");
                                                 try
                                                 {
                                                     await Task.WhenAll(
                                                         Starbucks.CheckBacklogAsync(gaArgs.Client, gaArgs.Guild).ContinueWith(_ => Config.Log.Info($"Starbucks backlog checked in {gaArgs.Guild.Name}.")),
                                                         DiscordInviteFilter.CheckBacklogAsync(gaArgs.Client, gaArgs.Guild).ContinueWith(_ => Config.Log.Info($"Discord invites backlog checked in {gaArgs.Guild.Name}.")),
                                                         FakeEmulatorFilter.CheckBacklogAsync(gaArgs.Client, gaArgs.Guild).ContinueWith(_ => Config.Log.Info($"Fake Emulator links backlog checked in {gaArgs.Guild.Name}."))
                                                     ).ConfigureAwait(false);
                                                 }
                                                 catch (Exception e)
                                                 {
                                                     Config.Log.Warn(e, "Error running backlog tasks");
                                                 }
                                                 Config.Log.Info($"All moderation backlogs checked in {gaArgs.Guild.Name}.");
                                             };
                    client.GuildUnavailable += guArgs =>
                                               {
                                                   Config.Log.Warn($"{guArgs.Guild.Name} is unavailable");
                                                   return Task.CompletedTask;
                                               };

                    client.MessageReactionAdded += Starbucks.Handler;

                    client.MessageCreated += AntipiracyMonitor.OnMessageCreated; // should be first
                    client.MessageCreated += ProductCodeLookup.OnMessageCreated;
                    client.MessageCreated += LogParsingHandler.OnMessageCreated;
                    client.MessageCreated += LogAsTextMonitor.OnMessageCreated;
                    client.MessageCreated += DiscordInviteFilter.OnMessageCreated;
                    client.MessageCreated += BotShutupHandler.OnMessageCreated;
                    client.MessageCreated += AppveyorLinksHandler.OnMessageCreated;
                    client.MessageCreated += GithubLinksHandler.OnMessageCreated;
                    client.MessageCreated += NewBuildsMonitor.OnMessageCreated;
                    client.MessageCreated += TableFlipMonitor.OnMessageCreated;
                    client.MessageCreated += FakeEmulatorFilter.OnMessageCreated;

                    client.MessageUpdated += AntipiracyMonitor.OnMessageUpdated;
                    client.MessageUpdated += DiscordInviteFilter.OnMessageUpdated;
                    client.MessageUpdated += FakeEmulatorFilter.OnMessageUpdated;

                    client.MessageDeleted += ThumbnailCacheMonitor.OnMessageDeleted;

                    client.UserUpdated += UsernameSpoofMonitor.OnUserUpdated;
                    client.GuildMemberAdded += Greeter.OnMemberAdded;
                    client.GuildMemberAdded += UsernameSpoofMonitor.OnMemberAdded;
                    client.GuildMemberUpdated += UsernameSpoofMonitor.OnMemberUpdated;

                    client.DebugLogger.LogMessageReceived += (sender, eventArgs) =>
                    {
                        Action<string> logLevel = Config.Log.Info;
                        if (eventArgs.Level == LogLevel.Debug)
                            logLevel = Config.Log.Debug;
                        //else if (eventArgs.Level == LogLevel.Info)
                        //    logLevel = botLog.Info;
                        else if (eventArgs.Level == LogLevel.Warning)
                            logLevel = Config.Log.Warn;
                        else if (eventArgs.Level == LogLevel.Error)
                            logLevel = Config.Log.Error;
                        else if (eventArgs.Level == LogLevel.Critical)
                            logLevel = Config.Log.Fatal;
                        logLevel(eventArgs.Message);
                    };

                    try
                    {
                        await client.ConnectAsync();
                    }
                    catch (Exception e)
                    {
                        Config.Log.Fatal(e, "Terminating");
                        return;
                    }

                    if (args.Length > 1 && ulong.TryParse(args[1], out var channelId))
                    {
                        Config.Log.Info($"Found channelId: {args[1]}");
                        DiscordChannel channel;
                        if (channelId == InvalidChannelId)
                        {
                            channel = await client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
                            await channel.SendMessageAsync("Bot has suffered some catastrophic failure and was restarted").ConfigureAwait(false);
                        }
                        else
                        {
                            channel = await client.GetChannelAsync(channelId).ConfigureAwait(false);
                            await channel.SendMessageAsync("Bot is up and running").ConfigureAwait(false);
                        }
                    }

                    Config.Log.Debug("Running RPC3 update check thread");
                    rpcs3UpdateCheckThread.Start(client);

                    while (!Config.Cts.IsCancellationRequested)
                    {
                        if (client.Ping > 1000)
                            await client.ReconnectAsync();
                        await Task.Delay(TimeSpan.FromMinutes(1), Config.Cts.Token).ContinueWith(dt => {/* in case it was cancelled */}).ConfigureAwait(false);
                    }
                }
                await backgroundTasks.ConfigureAwait(false);
            }
            catch (TaskCanceledException) { }
            catch(Exception e)
            {
                Config.Log.Fatal(e, "Experienced catastrofic failure, attempting to restart...");
                Sudo.Bot.Restart(InvalidChannelId);
            }
            finally
            {
                ShutdownCheck.Release();
                if (singleInstanceCheckThread.IsAlive)
                    singleInstanceCheckThread.Join(100);
                if (rpcs3UpdateCheckThread.IsAlive)
                    rpcs3UpdateCheckThread.Join(100);
                Config.Log.Info("Exiting");
            }
        }
    }
}
