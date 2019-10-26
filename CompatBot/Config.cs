using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using CompatBot.Utils;
using DSharpPlus.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Extensions.Logging;
using NLog.Filters;
using NLog.Targets;
using NLog.Targets.Wrappers;
using ILogger = NLog.ILogger;
using LogLevel = NLog.LogLevel;

namespace CompatBot
{
    internal static class Config
    {
        private static readonly IConfigurationRoot config;
        internal static readonly ILogger Log;
        internal static readonly ILoggerFactory LoggerFactory;
        internal static readonly ConcurrentDictionary<string, string> inMemorySettings = new ConcurrentDictionary<string, string>();

        public static readonly CancellationTokenSource Cts = new CancellationTokenSource();
        public static readonly TimeSpan ModerationTimeThreshold = TimeSpan.FromHours(12);
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan LogParsingTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan BuildTimeDifferenceForOutdatedBuilds = TimeSpan.FromDays(3);
        public static readonly TimeSpan ShutupTimeLimit = TimeSpan.FromMinutes(5);
        public static readonly Stopwatch Uptime = Stopwatch.StartNew();

        // these settings could be configured either through `$ dotnet user-secrets`, or through environment variables (e.g. launchSettings.json, etc)
        public static string CommandPrefix => config.GetValue(nameof(CommandPrefix), "!");
        public static string AutoRemoveCommandPrefix => config.GetValue(nameof(AutoRemoveCommandPrefix), ".");
        public static ulong BotGuildId => config.GetValue(nameof(BotGuildId), 272035812277878785ul);                  // discord server where the bot is supposed to be
        public static ulong BotGeneralChannelId => config.GetValue(nameof(BotGeneralChannelId), 272035812277878785ul);// #rpcs3; main or general channel where noobs come first thing
        public static ulong BotChannelId => config.GetValue(nameof(BotChannelId), 291679908067803136ul);              // #compatbot; this is used for !compat/!top results and new builds announcements
        public static ulong BotSpamId => config.GetValue(nameof(BotSpamId), 319224795785068545ul);                    // #bot-spam; this is a dedicated channel for bot abuse
        public static ulong BotLogId => config.GetValue(nameof(BotLogId), 436972161572536329ul);                      // #bot-log; a private channel for admin mod queue
        public static ulong BotRulesChannelId => config.GetValue(nameof(BotRulesChannelId), 311894275015049216ul);    // #rules-info; used to give links to rules
        public static ulong BotAdminId => config.GetValue(nameof(BotAdminId), 267367850706993152ul);                  // discord user id for a bot admin
        public static ulong ThumbnailSpamId => config.GetValue(nameof(ThumbnailSpamId), 475678410098606100ul);        // whatever private chat where bot can upload game covers for future embedding
        public static int ProductCodeLookupHistoryThrottle => config.GetValue(nameof(ProductCodeLookupHistoryThrottle), 7);
        public static int TopLimit => config.GetValue(nameof(TopLimit), 15);
        public static int AttachmentSizeLimit => config.GetValue(nameof(AttachmentSizeLimit), 8 * 1024 * 1024);
        public static int LogSizeLimit => config.GetValue(nameof(LogSizeLimit), 64 * 1024 * 1024);
        public static int MinimumBufferSize => config.GetValue(nameof(MinimumBufferSize), 512);
        public static int BuildNumberDifferenceForOutdatedBuilds => config.GetValue(nameof(BuildNumberDifferenceForOutdatedBuilds), 10);
        public static int MinimumPiracyTriggerLength => config.GetValue(nameof(MinimumPiracyTriggerLength), 4);

        public static string Token => config.GetValue(nameof(Token), "");
        public static string LogPath => config.GetValue(nameof(LogPath), "./logs/"); // paths are relative to the working directory
        public static string IrdCachePath => config.GetValue(nameof(IrdCachePath), "./ird/");

        internal static string CurrentLogPath => Path.GetFullPath(Path.Combine(LogPath, "bot.log"));

        public static string GoogleApiConfigPath 
        {
            get
            {
                if (SandboxDetector.Detect() == "Docker")
                    return "/bot-config/credentials.json";

                if (Assembly.GetEntryAssembly().GetCustomAttribute<UserSecretsIdAttribute>() is UserSecretsIdAttribute attribute)
                {
                    var path = Path.GetDirectoryName(PathHelper.GetSecretsPathFromSecretsId(attribute.UserSecretsId));
                    path = Path.Combine(path, "credentials.json");
                    if (File.Exists(path))
                        return path;
                }
                
                return "Properties/credentials.json";
            }
        }

        public static class Colors
        {
            public static readonly DiscordColor Help = DiscordColor.Azure;
            public static readonly DiscordColor DownloadLinks = new DiscordColor(0x3b88c3);
            public static readonly DiscordColor Maintenance = new DiscordColor(0xffff00);

            public static readonly DiscordColor CompatStatusNothing = new DiscordColor(0x455556); // colors mimic compat list statuses
            public static readonly DiscordColor CompatStatusLoadable = new DiscordColor(0xe74c3c);
            public static readonly DiscordColor CompatStatusIntro = new DiscordColor(0xe08a1e);
            public static readonly DiscordColor CompatStatusIngame = new DiscordColor(0xf9b32f);
            public static readonly DiscordColor CompatStatusPlayable = new DiscordColor(0x1ebc61);
            public static readonly DiscordColor CompatStatusUnknown = new DiscordColor(0x3198ff);

            public static readonly DiscordColor LogResultFailed = DiscordColor.Gray;

            public static readonly DiscordColor LogAlert = new DiscordColor(0xf04747); // colors mimic discord statuses
            public static readonly DiscordColor LogNotice = new DiscordColor(0xfaa61a);
            public static readonly DiscordColor LogInfo = new DiscordColor(0x43b581);
            public static readonly DiscordColor LogUnknown = new DiscordColor(0x747f8d);

            public static readonly DiscordColor PrOpen = new DiscordColor(0x2cbe4e);
            public static readonly DiscordColor PrMerged = new DiscordColor(0x6f42c1);
            public static readonly DiscordColor PrClosed = new DiscordColor(0xcb2431);

            public static readonly DiscordColor UpdateStatusGood = DiscordColor.Green;
            public static readonly DiscordColor UpdateStatusBad = DiscordColor.Yellow;
        }

        public static class Reactions
        {
            public static readonly DiscordEmoji Success = DiscordEmoji.FromUnicode("👌");
            public static readonly DiscordEmoji Failure = DiscordEmoji.FromUnicode("⛔");
            public static readonly DiscordEmoji Denied = DiscordEmoji.FromUnicode("👮");
            public static readonly DiscordEmoji Starbucks = DiscordEmoji.FromUnicode("☕");
            public static readonly DiscordEmoji Moderated = DiscordEmoji.FromUnicode("🔨");
            public static readonly DiscordEmoji No = DiscordEmoji.FromUnicode("😐");
            public static readonly DiscordEmoji PleaseWait = DiscordEmoji.FromUnicode("👀");
            public static readonly DiscordEmoji PiracyCheck = DiscordEmoji.FromUnicode("🔨");
            public static readonly DiscordEmoji Shutup = DiscordEmoji.FromUnicode("🔇");
            public static readonly DiscordEmoji BadUpdate = DiscordEmoji.FromUnicode("⚠");
        }

        public static class Moderation
        {
            public static readonly int StarbucksThreshold = 5;

            public static readonly IReadOnlyList<ulong> Channels = new List<ulong>
            {
                272875751773306881,
                319224795785068545,
            }.AsReadOnly();

            public static readonly IReadOnlyCollection<string> RoleWhiteList = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
            {
                "Administrator",
                "Community Manager",
                "Web Developer",
                "Moderator",
                "Lead Graphics Developer",
                "Lead Core Developer",
                "Developers",
                "Affiliated",
                "Contributors",
            };

            public static readonly IReadOnlyCollection<string> RoleSmartList = new HashSet<string>(RoleWhiteList, StringComparer.InvariantCultureIgnoreCase)
            {
                "Testers",
                "Helpers"
            };
        }

        static Config()
        {
            try
            {
                var args = Environment.CommandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length > 1)
                    inMemorySettings[nameof(Token)] = args[1];

                config = new ConfigurationBuilder()
                         .AddUserSecrets(Assembly.GetExecutingAssembly()) // lower priority
                         .AddEnvironmentVariables()
                         .AddInMemoryCollection(inMemorySettings)     // higher priority
                         .Build();
                Log = GetLog();
                LoggerFactory = new NLogLoggerFactory();
                Log.Info("Log path: " + CurrentLogPath);
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Error initializing settings: " + e.Message);
                Console.ResetColor();
            }
        }

        private static ILogger GetLog()
        {
            var config = new NLog.Config.LoggingConfiguration();
            var fileTarget = new FileTarget("logfile") {
                FileName = CurrentLogPath,
                ArchiveEvery = FileArchivePeriod.Day,
                ArchiveNumbering = ArchiveNumberingMode.DateAndSequence,
                KeepFileOpen = true,
                ConcurrentWrites = false,
                AutoFlush = false,
                OpenFileFlushTimeout = 1,
                Layout = "${longdate} ${sequenceid:padding=6} ${level:uppercase=true:padding=-5} ${message} ${onexception:" +
                            "${newline}${exception:format=ToString}" +
                            ":when=not contains('${exception:format=ShortType}','TaskCanceledException')}",
            };
            var asyncFileTarget = new AsyncTargetWrapper(fileTarget)
            {
                TimeToSleepBetweenBatches = 0,
                OverflowAction = AsyncTargetWrapperOverflowAction.Block,
                BatchSize = 500,
            };
            var logTarget = new ColoredConsoleTarget("logconsole") {
                Layout = "${longdate} ${level:uppercase=true:padding=-5} ${message} ${onexception:" +
                            "${newline}${exception:format=Message}" +
                            ":when=not contains('${exception:format=ShortType}','TaskCanceledException')}",
            };
#if DEBUG
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logTarget, "default"); // only echo messages from default logger to the console
#else
            config.AddRule(LogLevel.Info, LogLevel.Fatal, logTarget, "default");
#endif
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, asyncFileTarget);

            var filter = new ConditionBasedFilter { Condition = "contains('${message}','TaskCanceledException')", Action = FilterResult.Ignore, };
            foreach (var rule in config.LoggingRules)
                rule.Filters.Add(filter);
            LogManager.Configuration = config;
            return LogManager.GetLogger("default");
        }
    }
}