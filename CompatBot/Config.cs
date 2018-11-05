using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DSharpPlus.Entities;
using NLog;
using NLog.Targets;
using NLog.Targets.Wrappers;

namespace CompatBot
{
    internal static class Config
    {
        public static readonly string CommandPrefix = "!";
        public static readonly ulong BotGuildId = 272035812277878785;       // discord server where the bot is supposed to be
        public static readonly ulong BotChannelId = 291679908067803136;     // #compatbot; this is used for !compat/!top results and new builds announcements
        public static readonly ulong BotSpamId = 319224795785068545;        // #bot-spam; this is a dedicated channel for bot abuse
        public static readonly ulong BotLogId = 436972161572536329;         // #bot-log; a private channel for admin mod queue
        public static readonly ulong BotRulesChannelId = 311894275015049216;// #rules-info; used to give links to rules
        public static readonly ulong BotAdminId = 267367850706993152;       // discord user id for a bot admin
        public static readonly ulong ThumbnailSpamId = 475678410098606100;  // whatever private chat where bot can upload game covers for future embedding

        public static readonly int ProductCodeLookupHistoryThrottle = 7;

        public static readonly int TopLimit = 15;
        public static readonly int AttachmentSizeLimit = 8 * 1024 * 1024;
        public static readonly int LogSizeLimit = 64 * 1024 * 1024;
        public static readonly int MinimumBufferSize = 512;

        public static readonly string Token;
        public static readonly string LogPath = "../../../logs/bot.log"; // paths are relative to the assembly, so this will put it in the project's root
        public static readonly string IrdCachePath = "./ird/";

        internal static readonly ILogger Log;

        public static readonly CancellationTokenSource Cts = new CancellationTokenSource();
        public static readonly TimeSpan ModerationTimeThreshold = TimeSpan.FromHours(12);
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan LogParsingTimeout = TimeSpan.FromSeconds(15);

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
        }

        public static class Reactions
        {
            public static readonly DiscordEmoji Success = DiscordEmoji.FromUnicode("👌");
            public static readonly DiscordEmoji Failure = DiscordEmoji.FromUnicode("⛔");
            public static readonly DiscordEmoji Denied = DiscordEmoji.FromUnicode("👮");
            public static readonly DiscordEmoji Starbucks = DiscordEmoji.FromUnicode("☕");
            public static readonly DiscordEmoji Moderated = DiscordEmoji.FromUnicode("🔨");
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
        }

        static Config()
        {
            try
            {
                var envVars = Environment.GetEnvironmentVariables();
                foreach (var member in typeof(Config).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (envVars.Contains(member.Name))
                    {
                        if (member.FieldType == typeof(ulong) && ulong.TryParse(envVars[member.Name] as string, out var ulongValue))
                            member.SetValue(null, ulongValue);
                        if (member.FieldType == typeof(int) && int.TryParse(envVars[member.Name] as string, out var intValue))
                            member.SetValue(null, intValue);
                        if (member.FieldType == typeof(string))
                            member.SetValue(null, envVars[member.Name] as string);
                    }
                }
                var args = Environment.CommandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length > 1)
                    Token = args[1];

                Log = GetLog();
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
                FileName = LogPath,
                ArchiveEvery = FileArchivePeriod.Day,
                ArchiveNumbering = ArchiveNumberingMode.DateAndSequence,
                KeepFileOpen = true,
                ConcurrentWrites = false,
                AutoFlush = false,
                OpenFileFlushTimeout = 1,
            };
            var asyncFileTarget = new AsyncTargetWrapper(fileTarget)
            {
                TimeToSleepBetweenBatches = 0,
                OverflowAction = AsyncTargetWrapperOverflowAction.Block,
                BatchSize = 500,
            };
            var logTarget = new ColoredConsoleTarget("logconsole");
#if DEBUG
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, logTarget, "default"); // only echo messages from default logger to the console
#else
            config.AddRule(LogLevel.Info, LogLevel.Fatal, logTarget, "default");
#endif
            config.AddRule(LogLevel.Debug, LogLevel.Fatal, asyncFileTarget);
            LogManager.Configuration = config;
            return LogManager.GetLogger("default");
        }
    }
}