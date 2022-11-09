using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using CompatBot.Utils;
using DSharpPlus.Entities;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.PerfCounterCollector;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using NLog;
using NLog.Extensions.Logging;
using NLog.Filters;
using NLog.Targets;
using NLog.Targets.Wrappers;
using ILogger = NLog.ILogger;
using LogLevel = NLog.LogLevel;

namespace CompatBot;

internal static class Config
{
    private static IConfigurationRoot config = null!;
    private static TelemetryClient? telemetryClient;
    private static readonly DependencyTrackingTelemetryModule DependencyTrackingTelemetryModule = new();
    private static readonly PerformanceCollectorModule PerformanceCollectorModule = new();

    internal static readonly ILogger Log;
    internal static readonly ILoggerFactory LoggerFactory;
    internal static readonly ConcurrentDictionary<string, string?> InMemorySettings = new();
    internal static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();

    public static readonly CancellationTokenSource Cts = new();
    public static readonly Stopwatch Uptime = Stopwatch.StartNew();

    // these settings could be configured either through `$ dotnet user-secrets`, or through environment variables (e.g. launchSettings.json, etc)
    public static string CommandPrefix => config.GetValue(nameof(CommandPrefix), "!")!;
    public static string AutoRemoveCommandPrefix => config.GetValue(nameof(AutoRemoveCommandPrefix), ".")!;
    public static ulong BotGuildId => config.GetValue(nameof(BotGuildId), 272035812277878785ul);                  // discord server where the bot is supposed to be
    public static ulong BotGeneralChannelId => config.GetValue(nameof(BotGeneralChannelId), 272035812277878785ul);// #rpcs3; main or general channel where noobs come first thing
    public static ulong BotChannelId => config.GetValue(nameof(BotChannelId), 291679908067803136ul);              // #build-updates; this is used for new build announcements
    public static ulong BotSpamId => config.GetValue(nameof(BotSpamId), 319224795785068545ul);                    // #bot-spam; this is a dedicated channel for bot abuse
    public static ulong BotLogId => config.GetValue(nameof(BotLogId), 436972161572536329ul);                      // #bot-log; a private channel for admin mod queue
    public static ulong BotRulesChannelId => config.GetValue(nameof(BotRulesChannelId), 311894275015049216ul);    // #rules-info; used to give links to rules
    public static ulong ThumbnailSpamId => config.GetValue(nameof(ThumbnailSpamId), 475678410098606100ul);        // #bot-data; used for whatever bot needs to keep (cover embeds, etc)
    public static ulong DeletedMessagesLogChannelId => config.GetValue(nameof(DeletedMessagesLogChannelId), 0ul);

    public static TimeSpan ModerationBacklogThresholdInHours => TimeSpan.FromHours(config.GetValue(nameof(ModerationBacklogThresholdInHours), 1));
    public static TimeSpan DefaultTimeoutInSec => TimeSpan.FromSeconds(config.GetValue(nameof(DefaultTimeoutInSec), 30));
    public static TimeSpan SocketDisconnectCheckIntervalInSec => TimeSpan.FromSeconds(config.GetValue(nameof(SocketDisconnectCheckIntervalInSec), 10));
    public static TimeSpan LogParsingTimeoutInSec => TimeSpan.FromSeconds(config.GetValue(nameof(LogParsingTimeoutInSec), 30));
    public static TimeSpan BuildTimeDifferenceForOutdatedBuildsInDays => TimeSpan.FromDays(config.GetValue(nameof(BuildTimeDifferenceForOutdatedBuildsInDays), 3));
    public static TimeSpan ShutupTimeLimitInMin => TimeSpan.FromMinutes(config.GetValue(nameof(ShutupTimeLimitInMin), 5));
    public static TimeSpan ForcedNicknamesRecheckTimeInHours => TimeSpan.FromHours(config.GetValue(nameof(ForcedNicknamesRecheckTimeInHours), 3));
    public static TimeSpan IncomingMessageCheckIntervalInMin => TimeSpan.FromMinutes(config.GetValue(nameof(IncomingMessageCheckIntervalInMin), 10));
    public static TimeSpan MetricsIntervalInSec => TimeSpan.FromSeconds(config.GetValue(nameof(MetricsIntervalInSec), 10));

    public static int ProductCodeLookupHistoryThrottle => config.GetValue(nameof(ProductCodeLookupHistoryThrottle), 7);
    public static int TopLimit => config.GetValue(nameof(TopLimit), 15);
    public static int AttachmentSizeLimit => config.GetValue(nameof(AttachmentSizeLimit), 8 * 1024 * 1024);
    public static int LogSizeLimit => config.GetValue(nameof(LogSizeLimit), 64 * 1024 * 1024);
    public static int MinimumBufferSize => config.GetValue(nameof(MinimumBufferSize), 512);
    public static int MessageCacheSize => config.GetValue(nameof(MessageCacheSize), 1024);
    public static int BuildNumberDifferenceForOutdatedBuilds => config.GetValue(nameof(BuildNumberDifferenceForOutdatedBuilds), 10);
    public static int MinimumPiracyTriggerLength => config.GetValue(nameof(MinimumPiracyTriggerLength), 4);
    public static int MaxSyscallResultLines => config.GetValue(nameof(MaxSyscallResultLines), 13);
    public static int ChannelMessageHistorySize => config.GetValue(nameof(ChannelMessageHistorySize), 100);
    public static int FunMultiplier => config.GetValue(nameof(FunMultiplier), 1);
    public static int MaxPositionsForHwSurveyResults => config.GetValue(nameof(MaxPositionsForHwSurveyResults), 10);
    public static string Token => config.GetValue(nameof(Token), "")!;
    public static string AzureDevOpsToken => config.GetValue(nameof(AzureDevOpsToken), "")!;
    public static string AzureComputerVisionKey => config.GetValue(nameof(AzureComputerVisionKey), "")!;
    public static string AzureComputerVisionEndpoint => config.GetValue(nameof(AzureComputerVisionEndpoint), "https://westeurope.api.cognitive.microsoft.com/")!;
    public static Guid AzureDevOpsProjectId => config.GetValue(nameof(AzureDevOpsProjectId), new Guid("3598951b-4d39-4fad-ad3b-ff2386a649de"));
    public static string AzureAppInsightsConnectionString => config.GetValue(nameof(AzureAppInsightsConnectionString), "")!;
    public static string GithubToken => config.GetValue(nameof(GithubToken), "")!;
    public static string PreferredFontFamily => config.GetValue(nameof(PreferredFontFamily), "")!;
    public static string LogPath => config.GetValue(nameof(LogPath), "./logs/")!; // paths are relative to the working directory
    public static string IrdCachePath => config.GetValue(nameof(IrdCachePath), "./ird/")!;
    public static double GameTitleMatchThreshold => config.GetValue(nameof(GameTitleMatchThreshold), 0.57);
    public static byte[] CryptoSalt => Convert.FromBase64String(config.GetValue(nameof(CryptoSalt), "")!);
    public static string RenameNameSuffix => config.GetValue(nameof(RenameNameSuffix), " (Rule 7)")!; 

    internal static class AllowedMentions
    {
        internal static readonly IMention[] UsersOnly = { UserMention.All };
        internal static readonly IMention[] Nothing = Array.Empty<IMention>();
    }

    internal static string CurrentLogPath => Path.GetFullPath(Path.Combine(LogPath, "bot.log"));

    public static string GoogleApiConfigPath 
    {
        get
        {
            if (SandboxDetector.Detect() == SandboxType.Docker)
                return "/bot-config/credentials.json";

            if (Assembly.GetEntryAssembly()?.GetCustomAttribute<UserSecretsIdAttribute>() is UserSecretsIdAttribute attribute
                && Path.GetDirectoryName(PathHelper.GetSecretsPathFromSecretsId(attribute.UserSecretsId)) is string path)
            {
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
        public static readonly DiscordColor DownloadLinks = new(0x3b88c3);
        public static readonly DiscordColor Maintenance = new(0xffff00);

        public static readonly DiscordColor CompatStatusNothing = new(0x455556); // colors mimic compat list statuses
        public static readonly DiscordColor CompatStatusLoadable = new(0xe74c3c);
        public static readonly DiscordColor CompatStatusIntro = new(0xe08a1e);
        public static readonly DiscordColor CompatStatusIngame = new(0xf9b32f);
        public static readonly DiscordColor CompatStatusPlayable = new(0x1ebc61);
        public static readonly DiscordColor CompatStatusUnknown = new(0x3198ff);

        public static readonly DiscordColor LogResultFailed = DiscordColor.Gray;

        public static readonly DiscordColor LogAlert = new(0xf04747); // colors mimic discord statuses
        public static readonly DiscordColor LogNotice = new(0xfaa61a);
        public static readonly DiscordColor LogInfo = new(0x43b581);
        public static readonly DiscordColor LogUnknown = new(0x747f8d);

        public static readonly DiscordColor PrOpen = new(0x2cbe4e);
        public static readonly DiscordColor PrMerged = new(0x6f42c1);
        public static readonly DiscordColor PrClosed = new(0xcb2431);

        public static readonly DiscordColor UpdateStatusGood = new(0x3b88c3);
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
        public static readonly DiscordEmoji ShutUp = DiscordEmoji.FromUnicode("🔇");
        public static readonly DiscordEmoji BadUpdate = DiscordEmoji.FromUnicode("⚠\ufe0f");
    }

    public static class Moderation
    {
        public const int StarbucksThreshold = 5;

        public static readonly IReadOnlyCollection<ulong> MediaChannels = new List<ulong>
        {
            272875751773306881, // #media
            319224795785068545,
        }.AsReadOnly();

        public static readonly IReadOnlyCollection<ulong> OcrChannels = new HashSet<ulong>(MediaChannels)
        {
            272035812277878785, // #rpcs3
            277227681836302338, // #help
            272875751773306881, // #media
        };

        public static readonly IReadOnlyCollection<ulong> LogParsingChannels = new HashSet<ulong>
        {
            277227681836302338, // #help
            272081036316377088, // donors
            319224795785068545, // #bot-spam
            442667232489897997, // testers
        };

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
        };

        public static readonly IReadOnlyCollection<string> RoleSmartList = new HashSet<string>(RoleWhiteList, StringComparer.InvariantCultureIgnoreCase)
        {
            "Testers",
            "Helpers",
            "Contributors",
        };

        public static readonly IReadOnlyCollection<string> SupporterRoleList = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "Fans",
            "Supporters",
            "Spectators",
            "Nitro Booster",
        };
    }

    static Config()
    {
        try
        {
            RebuildConfiguration();
            Log = GetLog();
            LoggerFactory = new NLogLoggerFactory();
            Log.Info("Log path: " + CurrentLogPath);
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error initializing settings: " + e.Message);
            Console.ResetColor();
            throw;
        }
    }

    internal static void RebuildConfiguration()
    {
        config = new ConfigurationBuilder()
            .AddUserSecrets(Assembly.GetExecutingAssembly()) // lower priority
            .AddEnvironmentVariables()
            .AddInMemoryCollection(InMemorySettings)     // higher priority
            .Build();
    }

    private static ILogger GetLog()
    {
        var loggingConfig = new NLog.Config.LoggingConfiguration();
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
        var consoleTarget = new ColoredConsoleTarget("logconsole") {
            Layout = "${longdate} ${level:uppercase=true:padding=-5} ${message} ${onexception:" +
                     "${newline}${exception:format=Message}" +
                     ":when=not contains('${exception:format=ShortType}','TaskCanceledException')}",
        };
        var watchdogTarget = new MethodCallTarget("watchdog")
        {
            ClassName = typeof(Watchdog).AssemblyQualifiedName,
            MethodName = nameof(Watchdog.OnLogHandler),
        };
        watchdogTarget.Parameters.AddRange(new[]
        {
            new MethodCallParameter("${level}"),
            new MethodCallParameter("${message}"),
        });
#if DEBUG
        loggingConfig.AddRule(LogLevel.Trace, LogLevel.Fatal, consoleTarget, "default"); // only echo messages from default logger to the console
#else
            loggingConfig.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget, "default");
#endif
        loggingConfig.AddRule(LogLevel.Debug, LogLevel.Fatal, asyncFileTarget);
        loggingConfig.AddRule(LogLevel.Info, LogLevel.Fatal, watchdogTarget);

        var ignoreFilter1 = new ConditionBasedFilter { Condition = "contains('${message}','TaskCanceledException')", Action = FilterResult.Ignore, };
        var ignoreFilter2 = new ConditionBasedFilter { Condition = "contains('${message}','One or more pre-execution checks failed')", Action = FilterResult.Ignore, };
        foreach (var rule in loggingConfig.LoggingRules)
        {
            rule.Filters.Add(ignoreFilter1);
            rule.Filters.Add(ignoreFilter2);
            rule.FilterDefaultAction = FilterResult.Log;
        }
        LogManager.Configuration = loggingConfig;
        return LogManager.GetLogger("default");
    }

    public static BuildHttpClient? GetAzureDevOpsClient()
    {
        if (string.IsNullOrEmpty(AzureDevOpsToken))
            return null;

        var azureCreds = new VssBasicCredential("bot", AzureDevOpsToken);
        var azureConnection = new VssConnection(new Uri("https://dev.azure.com/nekotekina"), azureCreds);
        return azureConnection.GetClient<BuildHttpClient>();
    }

    public static TelemetryClient? TelemetryClient
    {
        get
        {
            if (string.IsNullOrEmpty(AzureAppInsightsConnectionString))
                return null;

            if (telemetryClient?.InstrumentationKey == AzureAppInsightsConnectionString)
                return telemetryClient;

            var telemetryConfig = TelemetryConfiguration.CreateDefault();
            telemetryConfig.ConnectionString = AzureAppInsightsConnectionString;
            telemetryConfig.TelemetryInitializers.Add(new HttpDependenciesParsingTelemetryInitializer());
            DependencyTrackingTelemetryModule.Initialize(telemetryConfig);
            PerformanceCollectorModule.Initialize(telemetryConfig);
            return telemetryClient = new TelemetryClient(telemetryConfig);
        }
    }
}