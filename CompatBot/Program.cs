﻿using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CompatBot.Commands;
using CompatBot.Commands.Checks;
using CompatBot.Commands.Converters;
using CompatBot.Commands.Processors;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils.Extensions;
using DSharpPlus.Commands.Processors.TextCommands;
using DSharpPlus.Commands.Processors.TextCommands.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.UserSecrets;
using NLog;
using NLog.Extensions.Logging;

namespace CompatBot;

internal static class Program
{
    private static readonly SemaphoreSlim InstanceCheck = new(0, 1);
    private static readonly SemaphoreSlim ShutdownCheck = new(0, 1);
    // preload the assembly so it won't fail after framework update while the process is still running
    private static readonly Assembly DiagnosticsAssembly = Assembly.Load(typeof(Process).Assembly.GetName());
    internal const ulong InvalidChannelId = 13;

    internal static async Task Main(string[] args)
    {
        Config.TelemetryClient?.TrackEvent("startup");

        //AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromMilliseconds(100));
        Regex.CacheSize = 200; // default is 15, we need more for content filter
        
        Console.WriteLine("Confinement: " + SandboxDetector.Detect());
        if (args.Length > 0 && args[0] == "--dry-run")
        {
            await OpenSslConfigurator.CheckAndFixSystemConfigAsync().ConfigureAwait(false);
            Console.WriteLine("Database path: " + Path.GetDirectoryName(Path.GetFullPath(DbImporter.GetDbPath("fake.db", Environment.SpecialFolder.ApplicationData))));
            if (Assembly.GetEntryAssembly()?.GetCustomAttribute<UserSecretsIdAttribute>() != null)
                Console.WriteLine("Bot config path: " + Path.GetDirectoryName(Path.GetFullPath(Config.GoogleApiConfigPath)));
            return;
        }

        if (Environment.ProcessId == 0)
            Config.Log.Info("Well, this was unexpected");
        var singleInstanceCheckThread = new Thread(() =>
        {
            using var instanceLock = new Mutex(false, @"Global\RPCS3 Compatibility Bot");
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
        });
        try
        {
            singleInstanceCheckThread.Start();
            if (!await InstanceCheck.WaitAsync(1000).ConfigureAwait(false))
            {
                Config.Log.Fatal("Another instance is already running.");
                return;
            }

            if (string.IsNullOrEmpty(Config.Token) || Config.Token.Length < 16)
            {
                Config.Log.Fatal("No token was specified.");
                return;
            }

            if (SandboxDetector.Detect() == SandboxType.Docker)
            {
                Config.Log.Info("Checking OpenSSL system configuration…");
                await OpenSslConfigurator.CheckAndFixSystemConfigAsync().ConfigureAwait(false);
                    
                Config.Log.Info("Checking for updates…");
                try
                {
                    var (updated, stdout) = await Bot.GitPullAsync(Config.Cts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(stdout) && updated)
                        Config.Log.Debug(stdout);
                    if (updated)
                    {
                        Bot.Restart(InvalidChannelId, "Restarted due to new bot updates not present in this Docker image");
                        return;
                    }
                }
                catch (Exception e)
                {
                    Config.Log.Error(e, "Failed to check for updates");
                }
            }

            if (!await DbImporter.UpgradeAsync(Config.Cts.Token).ConfigureAwait(false))
                return;

            await SqlConfiguration.RestoreAsync().ConfigureAwait(false);
            Config.Log.Debug("Restored configuration variables from persistent storage");

            await StatsStorage.RestoreAsync().ConfigureAwait(false);
            Config.Log.Debug("Restored stats from persistent storage");

            var backgroundTasks = Task.WhenAll(
                AmdDriverVersionProvider.RefreshAsync(),
#if !DEBUG
                ThumbScrapper.GameTdbScraper.RunAsync(Config.Cts.Token),
                //TitleUpdateInfoProvider.RefreshGameUpdateInfoAsync(Config.Cts.Token),
#endif
                DiscLanguageProvider.RefreshAsync(Config.Cts.Token),
                StatsStorage.BackgroundSaveAsync(),
                CompatList.ImportCompatListAsync(),
                Config.GetAzureDevOpsClient().GetPipelineDurationAsync(Config.Cts.Token),
                new GithubClient.Client(Config.GithubToken).GetPipelineDurationAsync(Config.Cts.Token),
                Config.GetCurrentGitRevisionAsync(Config.Cts.Token),
                Bot.UpdateCheckScheduledAsync(Config.Cts.Token)
            );

            try
            {
                if (!Directory.Exists(Config.IrdCachePath))
                    Directory.CreateDirectory(Config.IrdCachePath);
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, $"Failed to create new folder {Config.IrdCachePath}: {e.Message}");
            }

            var clientInfoLogged = false;
            var mediaScreenshotMonitor = new MediaScreenshotMonitor();
            var clientBuilder = DiscordClientBuilder
                .CreateDefault(Config.Token, DiscordIntents.All)
                .ConfigureLogging(builder => builder.AddNLog(LogManager.Configuration))
                .UseZstdCompression()
                .UseCommands((services, extension) =>
                {
                    var textCommandProcessor = new TextCommandProcessor(new()
                    {
                        PrefixResolver = new DefaultPrefixResolver(
                            true,
                            Config.CommandPrefix,
                            Config.AutoRemoveCommandPrefix
                        ).ResolvePrefixAsync,
                        IgnoreBots = true,
                        EnableCommandNotFoundException = true,
                    });
                    var appCommandProcessor = new SlashCommandProcessor(new()
                    {
                        RegisterCommands = true,
                        UnconditionallyOverwriteCommands = Config.EnableBulkDiscordCommandOverwrite,
                    });
                    textCommandProcessor.AddConverter<TextOnlyDiscordChannelConverter>();
                    extension.AddProcessor(textCommandProcessor);
                    extension.AddProcessor(appCommandProcessor);
                    
                    extension.AddCommands(Assembly.GetExecutingAssembly());
                    //extension.AddChecks(Assembly.GetExecutingAssembly()); //todo: use this after the bug is fixed
                    extension.AddCheck<LimitedToSpecificChannelsCheck>();
                    extension.AddCheck<RequiredRoleContextCheck>();
                    /*
                    if (!string.IsNullOrEmpty(Config.AzureComputerVisionKey))
                        extension.AddCommands<Vision>();
                    */

                    extension.CommandErrored += CommandErroredHandler.OnError;
                }, new()
                {
                    RegisterDefaultCommandProcessors = true,
                    UseDefaultCommandErrorHandler = false,
                    CommandExecutor = new CustomCommandExecutor(),
#if DEBUG
                    //DebugGuildId = Config.BotGuildId, // this forces app commands to be guild-limited, which doesn't work well
#endif
                })
                .UseInteractivity()
                .ConfigureEventHandlers(config =>
                {
                    config.HandleSessionCreated(async (c, sceArgs) =>
                    {
                        if (clientInfoLogged)
                            return;
                        
                        Config.Log.Info("Bot is ready to serve!");
                        Config.Log.Info("");
                        Config.Log.Info($"Bot user id : {c.CurrentUser.Id} ({c.CurrentUser.Username})");
                        var owners = c.CurrentApplication.Owners?.ToList() ?? [];
                        var msg = new StringBuilder($"Bot admin id{(owners.Count == 1 ? "": "s")}:");
                        if (owners.Count > 1)
                            msg.AppendLine();
                        await using var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false);
                        foreach (var owner in owners)
                        {
                            msg.AppendLine($"\t{owner.Id} ({owner.Username ?? "???"}#{owner.Discriminator ?? "????"})");
                            if (!await wdb.Moderator.AnyAsync(m => m.DiscordId == owner.Id, Config.Cts.Token).ConfigureAwait(false))
                                await wdb.Moderator.AddAsync(new() {DiscordId = owner.Id, Sudoer = true}, Config.Cts.Token).ConfigureAwait(false);
                        }
                        await wdb.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                        Config.Log.Info(msg.ToString().TrimEnd);
                        Config.Log.Info("");
                        clientInfoLogged = true;
                    });
                    config.HandleGuildAvailable(MultiEventHandlerWrapper<GuildAvailableEventArgs>.CreateUnordered([
                        OnGuildAvailableAsync,
                        (c, _) => UsernameValidationMonitor.MonitorAsync(c, true)
                    ]));
                    config.HandleGuildUnavailable((_, guArgs) =>
                    {
                        Config.Log.Warn($"{guArgs.Guild.Name} is unavailable");
                        return Task.CompletedTask;
                    });
#if !DEBUG
                    /*
                    config.HandleGuildDownloadCompleted(async (_, gdcArgs) =>
                    {
                        foreach (var guild in gdcArgs.Guilds)
                            await ModProvider.SyncRolesAsync(guild.Value).ConfigureAwait(false);
                    });
                    */
#endif
                    config.HandleMessageReactionAdded(MultiEventHandlerWrapper<MessageReactionAddedEventArgs>.CreateUnordered([
                            Starbucks.Handler,
                            ContentFilterMonitor.OnReaction,
                    ]));
                    config.HandleMessageCreated(new MultiEventHandlerWrapper<MessageCreatedEventArgs>(
                        [
                            ContentFilterMonitor.OnMessageCreated, // should be first
                            DiscordInviteFilter.OnMessageCreated,
                        ],
                        [
                            //Watchdog.OnMessageCreated,
                            GlobalMessageCache.OnMessageCreated,
                            mediaScreenshotMonitor.OnMessageCreated,
                            ProductCodeLookup.OnMessageCreated,
                            LogParsingHandler.OnMessageCreated,
                            LogAsTextMonitor.OnMessageCreated,
                            PostLogHelpHandler.OnMessageCreated,
                            BotReactionsHandler.OnMessageCreated,
                            GithubLinksHandler.OnMessageCreated,
                            NewBuildsMonitor.OnMessageCreated,
                            TableFlipMonitor.OnMessageCreated,
                            IsTheGamePlayableHandler.OnMessageCreated,
                            EmpathySimulationHandler.OnMessageCreated,
                        ]
                    ).OnEvent);
                    config.HandleMessageUpdated(new MultiEventHandlerWrapper<MessageUpdatedEventArgs>(
                        [
                            ContentFilterMonitor.OnMessageUpdated,
                            DiscordInviteFilter.OnMessageUpdated,
                        ],
                        [
                            GlobalMessageCache.OnMessageUpdated,
                            EmpathySimulationHandler.OnMessageUpdated,
                        ]
                    ).OnEvent);
                    config.HandleMessageDeleted(MultiEventHandlerWrapper<MessageDeletedEventArgs>.CreateUnordered([
                        //todo: make this ordered?
                        EmpathySimulationHandler.OnMessageDeleted,
                        ThumbnailCacheMonitor.OnMessageDeleted,
                        DeletedMessagesMonitor.OnMessageDeleted,
                        GlobalMessageCache.OnMessageDeleted,
                    ]));
                    config.HandleMessagesBulkDeleted(GlobalMessageCache.OnMessagesBulkDeleted);
                    config.HandleUserUpdated(MultiEventHandlerWrapper<UserUpdatedEventArgs>.CreateUnordered([
                        UsernameSpoofMonitor.OnUserUpdated,
                        UsernameZalgoMonitor.OnUserUpdated,
                    ]));
                    config.HandleGuildMemberAdded(MultiEventHandlerWrapper<GuildMemberAddedEventArgs>.CreateUnordered([
                        Greeter.OnMemberAdded,
                        UsernameSpoofMonitor.OnMemberAdded,
                        UsernameZalgoMonitor.OnMemberAdded,
                        UsernameValidationMonitor.OnMemberAdded,
                        UsernameRaidMonitor.OnMemberAdded,
                    ]));
                    config.HandleGuildMemberUpdated(MultiEventHandlerWrapper<GuildMemberUpdatedEventArgs>.CreateUnordered([
                        UsernameSpoofMonitor.OnMemberUpdated,
                        UsernameZalgoMonitor.OnMemberUpdated,
                        UsernameValidationMonitor.OnMemberUpdated,
                        UsernameRaidMonitor.OnMemberUpdated,
                    ]));
                    config.HandleComponentInteractionCreated(MultiEventHandlerWrapper<ComponentInteractionCreatedEventArgs>.CreateUnordered([
                        GlobalButtonHandler.OnComponentInteraction,
#if DEBUG
                        (_, args) =>
                        {
                            Config.Log.Debug($"ComponentInteraction: type: {args.Interaction.Type}, id: {args.Interaction.Data.CustomId}, user: {args.Interaction.User}");
                            return Task.CompletedTask;
                        },
#endif
                    ]));
                });
            using var client = clientBuilder.Build();
            mediaScreenshotMonitor.Client = client;

            //Watchdog.DisconnectTimestamps.Enqueue(DateTime.UtcNow);

            try
            {
                await client.ConnectAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Config.Log.Error(e, "Failed to connect to Discord: " + e.Message);
                throw;
            }

            ulong? channelId = null;
            string? restartMsg = null;
            await using (var wdb = await BotDb.OpenWriteAsync().ConfigureAwait(false))
            {
                var chState = wdb.BotState.FirstOrDefault(k => k.Key == "bot-restart-channel");
                if (chState != null)
                {
                    if (ulong.TryParse(chState.Value, out var ch))
                        channelId = ch;
                    wdb.BotState.Remove(chState);
                }
                var msgState = wdb.BotState.FirstOrDefault(i => i.Key == "bot-restart-msg");
                if (msgState != null)
                {
                    restartMsg = msgState.Value;
                    wdb.BotState.Remove(msgState);
                }
                await wdb.SaveChangesAsync().ConfigureAwait(false);
            }
            if (string.IsNullOrEmpty(restartMsg))
                restartMsg = null;

            if (channelId.HasValue)
            {
                Config.Log.Info($"Found channelId {channelId}");
                DiscordChannel channel;
                if (channelId == InvalidChannelId)
                {
                    channel = await client.GetChannelAsync(Config.ThumbnailSpamId).ConfigureAwait(false);
                    await channel.SendMessageAsync(restartMsg ?? "Bot has suffered some catastrophic failure and was restarted").ConfigureAwait(false);
                }
                else
                {
                    channel = await client.GetChannelAsync(channelId.Value).ConfigureAwait(false);
                    await channel.SendMessageAsync("Bot is up and running").ConfigureAwait(false);
                }
            }
            else
            {
                Config.Log.Debug($"Args count: {args.Length}");
                var pArgs = args.Select(a => a == Config.Token ? "<Token>" : $"[{a}]");
                Config.Log.Debug("Args: " + string.Join(" ", pArgs));
            }

            Config.Log.Debug("Running RPCS3 update check thread");
            backgroundTasks = Task.WhenAll(
                backgroundTasks,
                NewBuildsMonitor.MonitorAsync(client),
                //Watchdog.Watch(client),
                InviteWhitelistProvider.CleanupAsync(client),
                UsernameValidationMonitor.MonitorAsync(client),
                Psn.Check.MonitorFwUpdates(client, Config.Cts.Token),
                //Watchdog.SendMetrics(client),
                //Watchdog.CheckGCStats(),
                mediaScreenshotMonitor.ProcessWorkQueue()
            );

            while (!Config.Cts.IsCancellationRequested)
            {
                var latency = client.GetConnectionLatency(Config.BotGuildId);
                if (latency.TotalSeconds > 1)
                    Config.Log.Warn($"High ping detected: {latency}");
                await Task.Delay(TimeSpan.FromMinutes(1), Config.Cts.Token).ContinueWith(_ => {/* in case it was cancelled */}, TaskScheduler.Default).ConfigureAwait(false);
            }
            await backgroundTasks.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (!Config.InMemorySettings.ContainsKey("shutdown"))
                Config.Log.Fatal(e, "Experienced catastrophic failure, attempting to restart…");
        }
        finally
        {
            Config.TelemetryClient?.Flush();
            ShutdownCheck.Release();
            if (singleInstanceCheckThread.IsAlive)
                singleInstanceCheckThread.Join(100);
        }
        if (!Config.InMemorySettings.ContainsKey("shutdown"))
            Bot.Restart(InvalidChannelId, null);
    }

    private static async Task OnGuildAvailableAsync(DiscordClient c, GuildAvailableEventArgs gaArgs)
    {
        await BotStatusMonitor.RefreshAsync(c).ConfigureAwait(false);
        //Watchdog.DisconnectTimestamps.Clear();
        //Watchdog.TimeSinceLastIncomingMessage.Restart();
        if (gaArgs.Guild.Id != Config.BotGuildId)
        {
#if DEBUG
            Config.Log.Warn($"Unknown discord server {gaArgs.Guild.Id} ({gaArgs.Guild.Name})");
#else
            Config.Log.Warn($"Unknown discord server {gaArgs.Guild.Id} ({gaArgs.Guild.Name}), leaving…");
            await gaArgs.Guild.LeaveAsync().ConfigureAwait(false);
#endif
            return;
        }

        Config.Log.Info($"Server {gaArgs.Guild.Name} is available now");
        Config.Log.Info($"Checking moderation backlogs in {gaArgs.Guild.Name}…");
        try
        {
            await Task.WhenAll(Starbucks.CheckBacklogAsync(c, gaArgs.Guild).ContinueWith(_ => Config.Log.Info($"Starbucks backlog checked in {gaArgs.Guild.Name}."), TaskScheduler.Default), DiscordInviteFilter.CheckBacklogAsync(c, gaArgs.Guild).ContinueWith(_ => Config.Log.Info($"Discord invites backlog checked in {gaArgs.Guild.Name}."), TaskScheduler.Default)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Config.Log.Warn(e, "Error running backlog tasks");
        }
        Config.Log.Info($"All moderation backlogs checked in {gaArgs.Guild.Name}.");
    }
}