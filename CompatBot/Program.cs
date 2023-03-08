using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CirrusCiClient;
using CompatBot.Commands;
using CompatBot.Commands.Converters;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.Utils;
using CompatBot.Utils.Extensions;
using DSharpPlus;
using DSharpPlus.CommandsNext;
using DSharpPlus.Entities;
using DSharpPlus.Interactivity.Extensions;
using DSharpPlus.SlashCommands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Fortune = CompatBot.Commands.Fortune;

namespace CompatBot;

internal static class Program
{
    private static readonly SemaphoreSlim InstanceCheck = new(0, 1);
    private static readonly SemaphoreSlim ShutdownCheck = new(0, 1);
    // pre-load the assembly so it won't fail after framework update while the process is still running
    private static readonly Assembly DiagnosticsAssembly = Assembly.Load(typeof(Process).Assembly.GetName());
    internal const ulong InvalidChannelId = 13;

    internal static async Task Main(string[] args)
    {
        Config.TelemetryClient?.TrackEvent("startup");

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
                Config.Log.Info("Checking OpenSSL system configuration...");
                await OpenSslConfigurator.CheckAndFixSystemConfigAsync().ConfigureAwait(false);
                    
                Config.Log.Info("Checking for updates...");
                try
                {
                    var (updated, stdout) = await Sudo.Bot.GitPullAsync(Config.Cts.Token).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(stdout) && updated)
                        Config.Log.Debug(stdout);
                    if (updated)
                    {
                        Sudo.Bot.Restart(InvalidChannelId, "Restarted due to new bot updates not present in this Docker image");
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
                StatsStorage.BackgroundSaveAsync(),
                CompatList.ImportCompatListAsync(),
                Config.GetAzureDevOpsClient().GetPipelineDurationAsync(Config.Cts.Token),
                Config.GetCurrentGitRevisionAsync(Config.Cts.Token),
                CirrusCi.GetPipelineDurationAsync(Config.Cts.Token),
                Sudo.Bot.UpdateCheckScheduledAsync(Config.Cts.Token)
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

            var config = new DiscordConfiguration
            {
                Token = Config.Token,
                TokenType = TokenType.Bot,
                MessageCacheSize = Config.MessageCacheSize,
                LoggerFactory = Config.LoggerFactory,
                Intents = DiscordIntents.All,
            };
            using var client = new DiscordClient(config);
            var commands = client.UseCommandsNext(new()
            {
                StringPrefixes = new[] {Config.CommandPrefix, Config.AutoRemoveCommandPrefix},
                Services = new ServiceCollection().BuildServiceProvider(),
            });
            commands.RegisterConverter(new TextOnlyDiscordChannelConverter());
#if DEBUG
            commands.RegisterCommands<DevOnly>();
#endif
            commands.RegisterCommands<Misc>();
            commands.RegisterCommands<CompatList>();
            commands.RegisterCommands<Sudo>();
            commands.RegisterCommands<CommandsManagement>();
            commands.RegisterCommands<ContentFilters>();
            commands.RegisterCommands<Warnings>();
            commands.RegisterCommands<Explain>();
            commands.RegisterCommands<Psn>();
            commands.RegisterCommands<Invites>();
            commands.RegisterCommands<Moderation>();
            commands.RegisterCommands<Ird>();
            commands.RegisterCommands<BotMath>();
            commands.RegisterCommands<Pr>();
            commands.RegisterCommands<Events>();
            commands.RegisterCommands<E3>();
            commands.RegisterCommands<BotStats>();
            commands.RegisterCommands<Hardware>();
            commands.RegisterCommands<Syscall>();
            commands.RegisterCommands<ForcedNicknames>();
            commands.RegisterCommands<Minesweeper>();
            commands.RegisterCommands<Fortune>();
            if (!string.IsNullOrEmpty(Config.AzureComputerVisionKey))
                commands.RegisterCommands<Vision>();

            var slashCommands = client.UseSlashCommands();
            // Only register to rpcs3 guild for now.
            slashCommands.RegisterCommands<SlashMisc>(Config.BotGuildId);

            commands.CommandErrored += UnknownCommandHandler.OnError;

            client.UseInteractivity(new());

            client.Ready += async (c, _) =>
            {
                Config.Log.Info("Bot is ready to serve!");
                Config.Log.Info("");
                Config.Log.Info($"Bot user id : {c.CurrentUser.Id} ({c.CurrentUser.Username})");
                var owners = c.CurrentApplication.Owners.ToList();
                var msg = new StringBuilder($"Bot admin id{(owners.Count == 1 ? "": "s")}:");
                if (owners.Count > 1)
                    msg.AppendLine();
                await using var db = new BotDb();
                foreach (var owner in owners)
                {
                    msg.AppendLine($"\t{owner.Id} ({owner.Username ?? "???"}#{owner.Discriminator ?? "????"})");
                    if (!await db.Moderator.AnyAsync(m => m.DiscordId == owner.Id, Config.Cts.Token).ConfigureAwait(false))
                        await db.Moderator.AddAsync(new() {DiscordId = owner.Id, Sudoer = true}, Config.Cts.Token).ConfigureAwait(false);
                }
                await db.SaveChangesAsync(Config.Cts.Token).ConfigureAwait(false);
                Config.Log.Info(msg.ToString().TrimEnd);
                Config.Log.Info("");
            };
            client.GuildAvailable += async (c, gaArgs) =>
            {
                await BotStatusMonitor.RefreshAsync(c).ConfigureAwait(false);
                Watchdog.DisconnectTimestamps.Clear();
                Watchdog.TimeSinceLastIncomingMessage.Restart();
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
                        Starbucks.CheckBacklogAsync(c, gaArgs.Guild).ContinueWith(_ => Config.Log.Info($"Starbucks backlog checked in {gaArgs.Guild.Name}."), TaskScheduler.Default),
                        DiscordInviteFilter.CheckBacklogAsync(c, gaArgs.Guild).ContinueWith(_ => Config.Log.Info($"Discord invites backlog checked in {gaArgs.Guild.Name}."), TaskScheduler.Default)
                    ).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Config.Log.Warn(e, "Error running backlog tasks");
                }
                Config.Log.Info($"All moderation backlogs checked in {gaArgs.Guild.Name}.");
            };
            client.GuildAvailable += (c, _) => UsernameValidationMonitor.MonitorAsync(c, true);
            client.GuildUnavailable += (_, guArgs) =>
            {
                Config.Log.Warn($"{guArgs.Guild.Name} is unavailable");
                return Task.CompletedTask;
            };
#if !DEBUG
/*
                client.GuildDownloadCompleted += async gdcArgs =>
                                                 {
                                                     foreach (var guild in gdcArgs.Guilds)
                                                         await ModProvider.SyncRolesAsync(guild.Value).ConfigureAwait(false);
                                                 };
*/
#endif
            client.MessageReactionAdded += Starbucks.Handler;
            client.MessageReactionAdded += ContentFilterMonitor.OnReaction;

            client.MessageCreated += Watchdog.OnMessageCreated;
            client.MessageCreated += ContentFilterMonitor.OnMessageCreated; // should be first
            client.MessageCreated += GlobalMessageCache.OnMessageCreated;
            var mediaScreenshotMonitor = new MediaScreenshotMonitor(client);
            if (!string.IsNullOrEmpty(Config.AzureComputerVisionKey))
                client.MessageCreated += mediaScreenshotMonitor.OnMessageCreated;
            client.MessageCreated += ProductCodeLookup.OnMessageCreated;
            client.MessageCreated += LogParsingHandler.OnMessageCreated;
            client.MessageCreated += LogAsTextMonitor.OnMessageCreated;
            client.MessageCreated += DiscordInviteFilter.OnMessageCreated;
            client.MessageCreated += PostLogHelpHandler.OnMessageCreated;
            client.MessageCreated += BotReactionsHandler.OnMessageCreated;
            client.MessageCreated += GithubLinksHandler.OnMessageCreated;
            client.MessageCreated += NewBuildsMonitor.OnMessageCreated;
            client.MessageCreated += TableFlipMonitor.OnMessageCreated;
            client.MessageCreated += IsTheGamePlayableHandler.OnMessageCreated;
            client.MessageCreated += EmpathySimulationHandler.OnMessageCreated;

            client.MessageUpdated += GlobalMessageCache.OnMessageUpdated;
            client.MessageUpdated += ContentFilterMonitor.OnMessageUpdated;
            client.MessageUpdated += DiscordInviteFilter.OnMessageUpdated;
            client.MessageUpdated += EmpathySimulationHandler.OnMessageUpdated;

            client.MessageDeleted += GlobalMessageCache.OnMessageDeleted;
            if (Config.DeletedMessagesLogChannelId > 0)
                client.MessageDeleted += DeletedMessagesMonitor.OnMessageDeleted;
            client.MessageDeleted += ThumbnailCacheMonitor.OnMessageDeleted;
            client.MessageDeleted += EmpathySimulationHandler.OnMessageDeleted;

            client.MessagesBulkDeleted += GlobalMessageCache.OnMessagesBulkDeleted;
                
            client.UserUpdated += UsernameSpoofMonitor.OnUserUpdated;
            client.UserUpdated += UsernameZalgoMonitor.OnUserUpdated;

            client.GuildMemberAdded += Greeter.OnMemberAdded;
            client.GuildMemberAdded += UsernameSpoofMonitor.OnMemberAdded;
            client.GuildMemberAdded += UsernameZalgoMonitor.OnMemberAdded;
            client.GuildMemberAdded += UsernameValidationMonitor.OnMemberAdded;
            client.GuildMemberAdded += UsernameRaidMonitor.OnMemberAdded;

            client.GuildMemberUpdated += UsernameSpoofMonitor.OnMemberUpdated;
            client.GuildMemberUpdated += UsernameZalgoMonitor.OnMemberUpdated;
            client.GuildMemberUpdated += UsernameValidationMonitor.OnMemberUpdated;
            client.GuildMemberUpdated += UsernameRaidMonitor.OnMemberUpdated;
#if DEBUG
            client.ComponentInteractionCreated += (_, args) =>
            {
                Config.Log.Debug($"ComponentInteraction: type: {args.Interaction.Type}, id: {args.Interaction.Data.CustomId}, user: {args.Interaction.User}");
                return Task.CompletedTask;
            };
#endif
            client.ComponentInteractionCreated += GlobalButtonHandler.OnComponentInteraction;
                
            Watchdog.DisconnectTimestamps.Enqueue(DateTime.UtcNow);

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
            await using (var db = new BotDb())
            {
                var chState = db.BotState.FirstOrDefault(k => k.Key == "bot-restart-channel");
                if (chState != null)
                {
                    if (ulong.TryParse(chState.Value, out var ch))
                        channelId = ch;
                    db.BotState.Remove(chState);
                }
                var msgState = db.BotState.FirstOrDefault(i => i.Key == "bot-restart-msg");
                if (msgState != null)
                {
                    restartMsg = msgState.Value;
                    db.BotState.Remove(msgState);
                }
                await db.SaveChangesAsync().ConfigureAwait(false);
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
                Watchdog.Watch(client),
                InviteWhitelistProvider.CleanupAsync(client),
                UsernameValidationMonitor.MonitorAsync(client),
                Psn.Check.MonitorFwUpdates(client, Config.Cts.Token),
                Watchdog.SendMetrics(client),
                Watchdog.CheckGCStats(),
                mediaScreenshotMonitor.ProcessWorkQueue()
            );

            while (!Config.Cts.IsCancellationRequested)
            {
                if (client.Ping > 1000)
                    Config.Log.Warn($"High ping detected: {client.Ping}");
                await Task.Delay(TimeSpan.FromMinutes(1), Config.Cts.Token).ContinueWith(_ => {/* in case it was cancelled */}, TaskScheduler.Default).ConfigureAwait(false);
            }
            await backgroundTasks.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            if (!Config.InMemorySettings.ContainsKey("shutdown"))
                Config.Log.Fatal(e, "Experienced catastrophic failure, attempting to restart...");
        }
        finally
        {
            Config.TelemetryClient?.Flush();
            ShutdownCheck.Release();
            if (singleInstanceCheckThread.IsAlive)
                singleInstanceCheckThread.Join(100);
        }
        if (!Config.InMemorySettings.ContainsKey("shutdown"))
            Sudo.Bot.Restart(InvalidChannelId, null);
    }
}