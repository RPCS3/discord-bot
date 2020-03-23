using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.EventHandlers.LogParsing.POCOs;
using CompatBot.EventHandlers.LogParsing.SourceHandlers;
using DSharpPlus;
using DSharpPlus.Entities;
using IrdLibraryClient;

namespace CompatBot.Utils.ResultFormatters
{
    internal static partial class LogParserResult
    {
        private static readonly Client compatClient = new Client();
        private static readonly IrdClient irdClient = new IrdClient();
        private static readonly PsnClient.Client psnClient = new PsnClient.Client();

        private static readonly RegexOptions DefaultSingleLine = RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Singleline;
        // RPCS3 v0.0.3-3-3499d08 Alpha | HEAD
        // RPCS3 v0.0.4-6422-95c6ac699 Alpha | HEAD
        // RPCS3 v0.0.5-7104-a19113025 Alpha | HEAD
        // RPCS3 v0.0.5-42b4ce13a Alpha | minor
        private static readonly Regex BuildInfoInLog = new Regex(
            @"RPCS3 v(?<version_string>(?<version>(\d|\.)+)(-(?<build>\d+))?-(?<commit>[0-9a-f]+|unknown)) (?<stage>\w+)( \| (?<branch>[^|]+))?( \| Firmware version: (?<fw_version_installed>[^|\r\n]+)( \| (?<unknown>.*))?)?\r?$",
            DefaultSingleLine);
        private static readonly Regex CpuInfoInLog = new Regex(
            @"(?<cpu_model>[^|@]+?)\s*(((CPU\s*)?@\s*(?<cpu_speed>.+)\s*GHz\s*)|((APU|(with )?Radeon) [^|]+)|((\w+[\- ]Core )?Processor))?\s* \| (?<thread_count>\d+) Threads \| (?<memory_amount>[0-9\.\,]+) GiB RAM( \| TSC: (?<tsc>\S+))?( \| (?<cpu_extensions>.*?))?\r?$",
            DefaultSingleLine);
        internal static readonly Regex OsInfoInLog = new Regex(
            @"Operating system: (?<os_type>[^,]+), (Name: (?<posix_name>[^,]+), Release: (?<posix_release>[^,]+), Version: (?<posix_version>[^\r\n]+)|Major: (?<os_version_major>\d+), Minor: (?<os_version_minor>\d+), Build: (?<os_version_build>\d+), Service Pack: (?<os_service_pack>[^,]+), Compatibility mode: (?<os_compat_mode>[^,\r\n]+))\r?$",
            DefaultSingleLine);
        private static readonly Regex LinuxKernelVersion = new Regex(@"(?<version>\d+\.\d+\.\d+(-\d+)?)", DefaultSingleLine);
        private static readonly Regex ProgramHashPatch = new Regex(@"(?<hash>\w+) \(<-\s*(?<patch_count>\d+)\)", DefaultSingleLine);
        private static readonly char[] NewLineChars = {'\r', '\n'};

        // rpcs3-v0.0.5-7105-064d0619_win64.7z
        // rpcs3-v0.0.5-7105-064d0619_linux64.AppImage
        private static readonly Regex BuildInfoInUpdate = new Regex(@"rpcs3-v(?<version>(\d|\.)+)(-(?<build>\d+))?-(?<commit>[0-9a-f]+)_", DefaultSingleLine);
        private static readonly Regex VulkanDeviceInfo = new Regex(@"'(?<device_name>.+)' running on driver (?<version>.+)\r?$", DefaultSingleLine);
        private static readonly Regex IntelGpuModel = new Regex(@"Intel\s?(®|\(R\))? (?<gpu_model>(?<gpu_family>(\w| )+Graphics)( (?<gpu_model_number>P?\d+))?)(\s+\(|$)", DefaultSingleLine);
        private static readonly Regex InstallPath = new Regex(@"[A-Z]:/(?<program_files>Program Files( \(x86\))?/)?(?<desktop>([^/]+/)+Desktop/)?(?<rpcs3_folder>[^/]+/)*GuiConfigs/", DefaultSingleLine);

        private static readonly Version MinimumOpenGLVersion = new Version(4, 3);
        private static readonly Version MinimumFirmwareVersion = new Version(4, 80);
        private static readonly Version NvidiaFullscreenBugMinVersion = new Version(400, 0);
        private static readonly Version NvidiaFullscreenBugMaxVersion = new Version(499, 99);
        private static readonly Version NvidiaRecommendedOldWindowsVersion = new Version(435, 21); // linux is lagging
        private static readonly Version AmdRecommendedOldWindowsVersion = new Version(20, 1, 4);
        private static readonly Version AmdLastGoodOpenGLWindowsVersion = new Version(20, 1, 4);
        private static readonly Version NvidiaFullscreenBugFixed = new Version(0, 0, 6, 8204);

        private static readonly Dictionary<string, string> RsxPresentModeMap = new Dictionary<string, string>
        {
            ["0"] = "VK_PRESENT_MODE_IMMEDIATE_KHR",    // no vsync
            ["1"] = "VK_PRESENT_MODE_MAILBOX_KHR",      // fast sync
            ["2"] = "VK_PRESENT_MODE_FIFO_KHR",         // vsync
            ["3"] = "VK_PRESENT_MODE_FIFO_RELAXED_KHR", // adaptive vsync
        };

        private static readonly HashSet<string> KnownSyncFolders = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "OneDrive",
            "MEGASync",
            "RslSync",
            "BTSync",
            "Google Drive",
            "Google Backup",
            "Dropbox",
        };

        private static readonly Dictionary<string, string> KnownDiscOnPsnIds = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            // Demon's Souls
            {"BLES00932", "NPEB01202"},
            {"BLUS30443", "NPUB30910"},
            //{"BCJS30022", "NPJA00102"},
            {"BCJS70013", "NPJA00102"},

            // White Knight Chronicles II
            {"BCJS30042", "NPJA00104"}
        };

        private static readonly string[] Known1080pIds =
        {
            "NPEB00258", "NPUB30162", "NPJB00068", // scott pilgrim
        };

        private static readonly string[] KnownDisableVertexCacheIds =
        {
            "NPEB00258", "NPUB30162", "NPJB00068", // scott pilgrim
            "NPEB00303", "NPUB30242", "NPHB00229", // crazy taxi
        };

        private static readonly HashSet<string> KnownNoApproximateXFloatIds = new HashSet<string>
        {
            "BLES02247", "BLUS31604", "BLJM61346", "NPEB02436", "NPUB31848", "NPJB00769", // p5
            "BLES00932", "BLUS30443", // DeS
        };

        private static readonly HashSet<string> KnownFpsUnlockPatchIds = new HashSet<string>
        {
            "BLES00932", "BLUS30443", // DeS
            "BLUS30481", "BLES00826", "BLJM60223", // Nier
            "BLUS31197", "NPUB31251", "NPEB01407", "BLJM61043", "BCAS20311", // DoD3
            "BLUS31405", // jojo asb
            "BLJS10318", // jojo eoh
        };

        private static readonly HashSet<string> KnownWriteColorBuffersIds = new HashSet<string>
        {
            "BLES00932", "BLUS30443", "BCJS70013", "BCJS30022", // DeS
            "BLUS30481", "BLES00826", "BLJM60223", // Nier
            "BCAS25003", "BCES00510", "BCES00516", "BCES00799", "BCJS37001", "BCUS98111", "BCKS15003", "NPUA70080", // God of War 3 / Demo
            "BCES00006", "BCUS98137", "NPEA00333", "NPUA80499", // Motorstorm
            "BCES00484", "BCUS98242", "NPEA00315", "NPUA80661", // Motorstorm Apocalypse
            "BCES00129", "BCUS98155", // Motorstorm Pacific Rift
            "NPEA90090", "NPUA70140", "NPEA90033", // Motorstorm demos
            "BLJM60528", "NPJB00235", "NPHB00522", "NPJB90534", //E.X. Troopers / demo
            "BLES01987", "BLUS30964", "BLJS10160", // The Witch and the Hundred Knight
            "BCAS20100", "BCES00664", "NPEA00057", "NPJA00031", "NPUA80105", // wipeout hd
        };

        private static readonly HashSet<string> KnownMotionControlsIds = new HashSet<string>
        {
            "BCES00797", "BCES00802", "BCUS98164", "BCJS30040", "NPEA90053", "NPEA90076", "NPUA70088", "NPUA70112", // heavy rain
            "BCAS25017", "BCES01121", "BCES01122", "BCES01123", "BCUS98298", "NPEA00513", "NPUA81087", "NPEA90127", "NPJA90259", "NPUA72074", "NPJA00097", // beyond two souls
            "NPEA00094", "NPEA00250", "NPJA00039", "NPUA80083", // flower
            "NPEA00036", "NPUA80069", "NPJA00004", // locoroco
            "BCES01284", "BCUS98247", "BCUS99142", "NPEA00429", "NPUA80875", "NPEA90120", "NPUA70250", // sly cooper 4
        };

        private static readonly HashSet<string> KnownGamesThatRequireInterpreter = new HashSet<string>
        {
            "NPEB00630", "NPUB30493", "NPJB00161", // daytona usa
            "BCAS25017", "BCES01121", "BCES01122", "BCES01123", "BCUS98298", "NPEA00513", "NPUA81087", "NPEA90127", "NPJA90259", "NPUA72074", "NPJA00097", // beyond two souls
        };

        private static readonly HashSet<string> KnownGamesThatWorkWithRelaxedZcull = new HashSet<string>
        {
            "BLAS50296", "BLES00680", "BLES01179", "BLES01294", "BLUS30418", "BLUS30711", "BLUS30758", //rdr
            "BLJM60314", "BLJM60403", "BLJM61181", "BLKS20315",
            "NPEB00833", "NPHB00465", "NPHB00466", "NPUB30638", "NPUB30639",
            "NPUB50139", // max payne 3 / rdr bundle???
            "BLAS55005", "BLES00246", "BLJM57001", "BLJM67001", "BLKS25001", "BLUS30109", "BLUS30148", //mgs4
            "NPEB00027", "NPEB02182", "NPEB90116", "NPJB00698", "NPJB90149", "NPUB31633",
            "NPHB00065", "NPHB00067",
            "BCAS20100", "BCES00664", "NPEA00057", "NPJA00031", "NPUA80105", // wipeout hd
        };

        private static readonly HashSet<string> KnownBogusLicenses = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "UP0700-NPUB30932_00-NNKDLFULLGAMEPTB.rap",
            "EP0700-NPEB01158_00-NNKDLFULLGAMEPTB.rap",
        };

        private static readonly HashSet<string> KnownCustomLicenses = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "EP4062-NPEB02436_00-PPERSONA5X000000.rap",
            "UP2611-NPUB31848_00-PPERSONA5X000000.rap",
        };

        // v1.5 https://wiki.rpcs3.net/index.php?title=Help:Game_Patches#SPU_MLAA
        private static readonly HashSet<string> KnownMlaaSpuHashes = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "7b5ea49122ec7f023d4a72452dc7a9208d9d6dbf",
            "7cd211ff1cbd33163eb0711440dccbb3c1dbcf6c",
            "45f98378f0837fc6821f63576f65d47d10f9bbcb",
            "82b3399c8e6533ba991eedb0e139bf20c7783bac",
            "191fe1c92c8360992b3240348e70ea37d50812d4",
            "530c255936b07b25467a58e24ceff5fd4e2960b7",
            "702d0205a89d445d15dc0f96548546c4e2e7a59f",
            "969cf3e9db75f52a6b41074ccbff74106b709854",
            "976d2128f08c362731413b75c934101b76c3d73b",
            "2239af4827b17317522bd6323c646b45b34ebf14",
            "5177cbc4bf45c8a0a6968c2a722da3a9e6cfb28b",
            "9001b44fd7278b5a6fa5385939fe928a0e549394",
            "794795c449beef176d076816284849d266f55f99",
            "931132fd48a40bce0bec28e21f760b1fc6ca4364",
            "1549476fe258150ff9f902229ffaed69a932a9c1",
            "a129a01a270246c85df18eee0e959ef4263b6510",
            "ac189d7f87091160a94e69803ac0cff0a8bb7813",
            "df5b1c3353cc36bb2f0fb59197d849bb99c3fecd",
            "e3780fe1dc8953f849ac844ec9688ff4da3ca3ae",
        };

        private static readonly TimeSpan OldBuild = TimeSpan.FromDays(30);
        private static readonly TimeSpan VeryOldBuild = TimeSpan.FromDays(60);
        //private static readonly TimeSpan VeryVeryOldBuild = TimeSpan.FromDays(90);
        private static readonly TimeSpan AncientBuild = TimeSpan.FromDays(180);
        private static readonly TimeSpan PrehistoricBuild = TimeSpan.FromDays(365);

        private static readonly char[] PrioritySeparator = {' '};
        private static readonly string[] EmojiPriority = { "😱", "💢", "‼", "❗",  "❌", "⁉", "⚠", "❔", "✅", "ℹ" };
        private const string EnabledMark = "[x]";
        private const string DisabledMark = "[\u00a0]";

        public static async Task<DiscordEmbedBuilder> AsEmbedAsync(this LogParseState state, DiscordClient client, DiscordMessage message, ISource source)
        {
            DiscordEmbedBuilder builder;
            state.CompleteCollection ??= state.WipCollection;
            state.CompleteMultiValueCollection ??= state.WipMultiValueCollection;
            var collection = state.CompleteCollection;
            if (collection?.Count > 0)
            {
                var ldrGameSerial = collection["ldr_game_serial"] ?? collection["ldr_path_serial"];
                if (collection["serial"] is string serial
                    && KnownDiscOnPsnIds.TryGetValue(serial, out var psnSerial)
                    && !string.IsNullOrEmpty(ldrGameSerial)
                    && ldrGameSerial.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase)
                    && ldrGameSerial.Equals(psnSerial, StringComparison.InvariantCultureIgnoreCase))
                {
                    collection["serial"] = psnSerial;
                    collection["game_category"] = "HG";
                }
                var titleUpdateInfoTask = psnClient.GetTitleUpdatesAsync(collection["serial"], Config.Cts.Token);
                var gameInfo = await client.LookupGameInfoAsync(collection["serial"], collection["game_title"], true, category: collection["game_category"]).ConfigureAwait(false);
                try
                {
                    var titleUpdateInfo = await titleUpdateInfoTask.ConfigureAwait(false);
                    if (titleUpdateInfo?.Tag?.Packages?.LastOrDefault()?.Version is string tuVersion)
                        collection["game_update_version"] = tuVersion;

                }
                catch {}
                builder = new DiscordEmbedBuilder(gameInfo) {ThumbnailUrl = null}; // or this will fuck up all formatting
                collection["embed_title"] = builder.Title ?? "";
                if (state.Error == LogParseState.ErrorCode.PiracyDetected)
                {
                    var msg = "__You are being denied further support until you legally dump the game__.\n" +
                              "Please note that the RPCS3 community and its developers do not support piracy.\n" +
                              "Most of the issues with pirated dumps occur due to them having been tampered with in some way " +
                              "and therefore act unpredictably on RPCS3.\n" +
                              "If you need help obtaining legal dumps, please read [the quickstart guide](https://rpcs3.net/quickstart).";
                    builder.WithColor(Config.Colors.LogAlert)
                        .WithTitle("Pirated content detected")
                        .WithDescription(msg);
                }
                else
                {
                    CleanupValues(state);
                    BuildInfoSection(builder, collection);
                    var colA = BuildCpuSection(collection);
                    var colB = BuildGpuSection(collection);
                    BuildSettingsSections(builder, collection, colA, colB);
                    BuildLibsSection(builder, collection);
                    await BuildNotesSectionAsync(builder, state, client).ConfigureAwait(false);
                }
            }
            else
            {
                builder = new DiscordEmbedBuilder
                {
                    Description = "Log analysis failed, most likely cause is an empty log. Please try again.",
                    Color = Config.Colors.LogResultFailed,
                };
            }
            builder.AddAuthor(client, message, source, state);
            return builder;
        }

        private static void CleanupValues(LogParseState state)
        {
            var items = state.CompleteCollection;
            var multiItems = state.CompleteMultiValueCollection;
            if (items["strict_rendering_mode"] == "true")
                items["resolution_scale"] = "Strict Mode";
            if (items["spu_threads"] == "0")
                items["spu_threads"] = "Auto";
            if (items["spu_secondary_cores"] != null)
                items["thread_scheduler"] = items["spu_secondary_cores"];
            if (items["vulkan_initialized_device"] != null)
                items["gpu_info"] = items["vulkan_initialized_device"];
            else if (items["driver_manuf_new"] != null)
                items["gpu_info"] = items["driver_manuf_new"];
            else if (items["vulkan_gpu"] != "\"\"")
                items["gpu_info"] = items["vulkan_gpu"];
            else if (items["d3d_gpu"] != "\"\"")
                items["gpu_info"] = items["d3d_gpu"];
            else if (items["driver_manuf"] != null)
                items["gpu_info"] = items["driver_manuf"];
            if (!string.IsNullOrEmpty(items["gpu_info"]))
            {
                items["gpu_info"] = items["gpu_info"].StripMarks();
                items["driver_version_info"] = GetVulkanDriverVersion(items["vulkan_initialized_device"], multiItems["vulkan_found_device"]) ??
                                               GetVulkanDriverVersion(items["gpu_info"], multiItems["vulkan_found_device"]) ??
                                               GetOpenglDriverVersion(items["gpu_info"], items["driver_version_new"] ?? items["driver_version"]) ??
                                               GetVulkanDriverVersionRaw(items["gpu_info"], items["vulkan_driver_version_raw"]);
            }
            if (items["driver_version_info"] != null)
                items["gpu_info"] += $" ({items["driver_version_info"]})";

            if (multiItems["vulkan_compatible_device_name"] is UniqueList<string> vulkanDevices && vulkanDevices.Any())
            {
                var devices = vulkanDevices
                    .Distinct()
                    .Select(n => new {name = n.StripMarks(), driverVersion = GetVulkanDriverVersion(n, multiItems["vulkan_found_device"])})
                    .Reverse()
                    .ToList();
                if (string.IsNullOrEmpty(items["gpu_info"]) && devices.Count > 0)
                {
                    var discreteGpu = devices.FirstOrDefault(d => IsNvidia(d.name))
                                      ?? devices.FirstOrDefault(d => IsAmd(d.name))
                                      ?? devices.First();
                    items["discrete_gpu_info"] = $"{discreteGpu.name} ({discreteGpu.driverVersion})";
                    items["driver_version_info"] = discreteGpu.driverVersion;
                }
                items["gpu_available_info"] = string.Join(Environment.NewLine, devices.Select(d => $"{d.name} ({d.driverVersion})"));
            }

            if (items["af_override"] is string af)
            {
                if (af == "0")
                    items["af_override"] = "Auto";
                else if (af == "1")
                    items["af_override"] = "Disabled";
            }
            if (items["zcull"] == "true")
                items["zcull_status"] = "Disabled";
            else if (items["relaxed_zcull"] == "true")
                items["zcull_status"] = "Relaxed";
            else
                items["zcull_status"] = "Full";
            if (items["lib_loader"] is string libLoader)
            {
                var liblv2 = libLoader.Contains("liblv2", StringComparison.InvariantCultureIgnoreCase);
                var auto = libLoader.Contains("auto", StringComparison.InvariantCultureIgnoreCase);
                var manual = libLoader.Contains("manual", StringComparison.InvariantCultureIgnoreCase);
                var strict = libLoader.Contains("strict", StringComparison.InvariantCultureIgnoreCase);
                if (auto && manual)
                    items["lib_loader"] = "Auto & manual select";
                else if (liblv2 && manual)
                    items["lib_loader"] = "Liblv2.sprx & manual";
                else if (liblv2 && strict)
                    items["lib_loader"] = "Liblv2.sprx & strict";
                else if (auto)
                    items["lib_loader"] = "Auto";
                else if (manual)
                    items["lib_loader"] = "Manual selection";
                else
                    items["lib_loader"] = "Liblv2.sprx only";

            }

            static string reformatDecoder(string dec)
            {
                if (string.IsNullOrEmpty(dec))
                    return dec;

                var match = Regex.Match(dec, @"(?<name>[^(]+)(\((?<type>.+)\))?", RegexOptions.ExplicitCapture | RegexOptions.Singleline);
                var n = match.Groups["name"].Value.TrimEnd();
                var t = match.Groups["type"].Value;
                return string.IsNullOrEmpty(t) ? n : $"{n}/{t}";
            }

            items["ppu_decoder"] = reformatDecoder(items["ppu_decoder"]);
            items["spu_decoder"] = reformatDecoder(items["spu_decoder"]);
            if (items["win_path"] != null)
                items["os_type"] = "Windows";
            else if (items["lin_path"] != null)
                items["os_type"] = "Linux";
            if (items["os_type"] == "Windows" && GetWindowsVersion((items["driver_version_new"] ?? items["driver_version"])) is string winVersion)
                items["os_windows_version"] = winVersion;
            if (items["library_list"] is string libs)
            {
                var libList = libs.Split('\n').Select(l => l.Trim(' ', '\t', '-', '\r', '[', ']')).Where(s => !string.IsNullOrEmpty(s)).ToList();
                items["library_list"] = libList.Count > 0 ? string.Join(", ", libList) : "None";
            }
            else
                items["library_list"] = "None";
            if (items["app_version"] is string appVer && !appVer.Equals("Unknown", StringComparison.InvariantCultureIgnoreCase))
                items["game_version"] = appVer;
            else if (items["disc_app_version"] is string discAppVer && !discAppVer.Equals("Unknown", StringComparison.InvariantCultureIgnoreCase))
                items["game_version"] = discAppVer;
            else if (items["disc_package_version"] is string discPkgVer && !discPkgVer.Equals("Unknown", StringComparison.InvariantCultureIgnoreCase))
                items["game_version"] = discPkgVer;
            if (items["game_version"] is string gameVer && gameVer.StartsWith("0"))
                items["game_version"] = gameVer[1..];
            if (items["game_update_version"] is string gameUpVer && gameUpVer.StartsWith("0"))
                items["game_update_version"] = gameUpVer[1..];

            foreach (var key in items.AllKeys)
            {
                var value = items[key];
                if ("true".Equals(value, StringComparison.CurrentCultureIgnoreCase))
                    value = EnabledMark;
                else if ("false".Equals(value, StringComparison.CurrentCultureIgnoreCase))
                    value = DisabledMark;
                items[key] = value.Sanitize(false);
            }
        }

        private static void PageSection(DiscordEmbedBuilder builder, string notesContent, string sectionName)
        {
            if (!string.IsNullOrEmpty(notesContent))
            {
                var fields = notesContent.Split(Environment.NewLine).BreakInFieldContent(100).ToList();
                if (fields.Count > 1)
                    for (var idx = 0; idx < fields.Count; idx++)
                        builder.AddField($"{sectionName} #{idx + 1} of {fields.Count}", fields[idx].content);
                else
                    builder.AddField(sectionName, fields[0].content);
            }
        }

        private static async Task<UpdateInfo> CheckForUpdateAsync(NameValueCollection items)
        {
            if (string.IsNullOrEmpty(items["build_and_specs"]))
                return null;

            var currentBuildCommit = items["build_commit"];
            if (string.IsNullOrEmpty(currentBuildCommit))
                currentBuildCommit = null;
            var updateInfo = await compatClient.GetUpdateAsync(Config.Cts.Token, currentBuildCommit).ConfigureAwait(false);
            if (updateInfo?.ReturnCode != 1 && currentBuildCommit != null)
                updateInfo = await compatClient.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
            var link = updateInfo?.LatestBuild?.Windows?.Download ?? updateInfo?.LatestBuild?.Linux?.Download;
            if (string.IsNullOrEmpty(link))
                return null;

            var latestBuildInfo = BuildInfoInUpdate.Match(link.ToLowerInvariant());
            if (latestBuildInfo.Success && VersionIsTooOld(items, latestBuildInfo, updateInfo))
                return updateInfo;

            return null;
        }

        private static bool VersionIsTooOld(NameValueCollection items, Match update, UpdateInfo updateInfo)
        {
            if ((updateInfo.GetUpdateDelta() is TimeSpan updateTimeDelta) && (updateTimeDelta < Config.BuildTimeDifferenceForOutdatedBuilds))
                return false;

            if (Version.TryParse(items["build_version"], out var logVersion) && Version.TryParse(update.Groups["version"].Value, out var updateVersion))
            {
                if (logVersion < updateVersion)
                    return true;

                if (int.TryParse(items["build_number"], out var logBuild) && int.TryParse(update.Groups["build"].Value, out var updateBuild))
                {
                    if (logBuild + Config.BuildNumberDifferenceForOutdatedBuilds < updateBuild)
                        return true;
                }
                return false;
            }
            return !SameCommits(items["build_commit"], update.Groups["commit"].Value);
        }

        private static bool SameCommits(string commitA, string commitB)
        {
            if (string.IsNullOrEmpty(commitA) && string.IsNullOrEmpty(commitB))
                return true;

            if (string.IsNullOrEmpty(commitA) || string.IsNullOrEmpty(commitB))
                return false;

            var len = Math.Min(commitA.Length, commitB.Length);
            return commitA[..len] == commitB[..len];
        }

        private static string GetOpenglDriverVersion(string gpuInfo, string version)
        {
            if (string.IsNullOrEmpty(version))
                return null;

            if (gpuInfo.Contains("Radeon", StringComparison.InvariantCultureIgnoreCase) ||
                gpuInfo.Contains("AMD", StringComparison.InvariantCultureIgnoreCase) ||
                gpuInfo.Contains("ATI ", StringComparison.InvariantCultureIgnoreCase))
                return AmdDriverVersionProvider.GetFromOpenglAsync(version).ConfigureAwait(false).GetAwaiter().GetResult();

            return version;
        }

        private static string GetVulkanDriverVersion(string gpu, UniqueList<string> foundDevices)
        {
            if (string.IsNullOrEmpty(gpu) || !foundDevices.Any())
                return null;

            var info = (from line in foundDevices
                    let m = VulkanDeviceInfo.Match(line)
                    where m.Success
                    select m
                ).FirstOrDefault(m => m.Groups["device_name"].Value == gpu);
            var result = info?.Groups["version"].Value;
            if (string.IsNullOrEmpty(result))
                return null;

            if (gpu.Contains("Radeon", StringComparison.InvariantCultureIgnoreCase) ||
                gpu.Contains("AMD", StringComparison.InvariantCultureIgnoreCase) ||
                gpu.Contains("ATI ", StringComparison.InvariantCultureIgnoreCase))
            {
                if (gpu.Contains("RADV", StringComparison.InvariantCultureIgnoreCase))
                    return result;

                return AmdDriverVersionProvider.GetFromVulkanAsync(result).ConfigureAwait(false).GetAwaiter().GetResult();
            }

            if (gpu.Contains("Intel", StringComparison.InvariantCultureIgnoreCase)
                && Version.TryParse(result, out var ver)
                && ver.Minor > 400
                && ver.Build >= 0)
            {
                var build = ((ver.Minor & 0b_11) << 12) | ver.Build;
                var minor = ver.Minor >> 2;
                return new Version(ver.Major, minor, build).ToString();
            }

            if (result.EndsWith(".0.0"))
                result = result[..^4];
            if (result.Length > 3 && result[^2] == '.')
                result = result[..^1] + "0" + result[^1];
            return result;
        }

        private static string GetVulkanDriverVersionRaw(string gpuInfo, string version)
        {
            if (string.IsNullOrEmpty(version))
                return null;

            var ver = int.Parse(version);
            if (IsAmd(gpuInfo))
            {
                var major = (ver >> 22) & 0x3ff;
                var minor = (ver >> 12) & 0x3ff;
                var patch = ver & 0xfff;
                var result = $"{major}.{minor}.{patch}";
                if (gpuInfo.Contains("RADV", StringComparison.InvariantCultureIgnoreCase))
                    return result;

                return AmdDriverVersionProvider.GetFromVulkanAsync(result).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            else
            {
                var major = (ver >> 22) & 0x3ff;
                var minor = (ver >> 14) & 0xff;
                var patch = ver & 0x3fff;
                if (major == 0 && gpuInfo.Contains("Intel", StringComparison.InvariantCultureIgnoreCase))
                    return $"{minor}.{patch}";

                if (IsNvidia(gpuInfo))
                {
                    if (patch == 0)
                        return $"{major}.{minor}";
                    return $"{major}.{minor:00}.{(patch >> 6) & 0xff}.{patch & 0x3f}";
                }

                return $"{major}.{minor}.{patch}";
            }
        }

        private static string GetWindowsVersion(string driverVersionString)
        {
            // see https://docs.microsoft.com/en-us/windows-hardware/drivers/display/wddm-2-1-features#driver-versioning
            if (string.IsNullOrEmpty(driverVersionString) || !Version.TryParse(driverVersionString, out var driverVer))
                return null;

            return driverVer.Major switch
            {
                6 => "XP", //XDDM
                7 => "Vista",
                8 => "7",
                9 => "8",
                10 => "8.1",
                int v when v >= 20 && v < 30 => ((v % 10) switch
                {
                    // see https://en.wikipedia.org/wiki/Windows_Display_Driver_Model#WDDM_2.0
                    0 => "10",
                    1 => "10 1607",
                    2 => "10 1703",
                    3 => "10 1709",
                    4 => "10 1803",
                    5 => "10 1809",
                    6 => "10 1903",
                    7 => "10 2004",
                    _ => null,
                }),
                _ => null,
            };
        }

        private static string GetWindowsVersion(Version windowsVersion) =>
            windowsVersion.Major switch
            {
                5 => windowsVersion.Minor switch
                {
                    0 => "2000",
                    1 => "XP",
                    2 => "XP x64",
                    _ => null,
                },
                6 => windowsVersion.Minor switch
                {
                    0 => "Vista",
                    1 => "7",
                    2 => "8",
                    3 => "8.1",
                    _ => null
                },
                10 => windowsVersion.Build switch
                {
                    int v when v < 10240 => ("10 TH1 Build " + v),
                    10240 => "10 1507",
                    int v when v < 10586 => ("10 TH2 Build " + v),
                    10586 => "10 1511",
                    int v when v < 14393 => ("10 RS1 Build " + v),
                    14393 => "10 1607",
                    int v when v < 15063 => ("10 RS2 Build " + v),
                    15063 => "10 1703",
                    int v when v < 16299 => ("10 RS3 Build " + v),
                    16299 => "10 1709",
                    int v when v < 17134 => ("10 RS4 Build " + v),
                    17134 => "10 1803",
                    int v when v < 17763 => ("10 RS5 Build " + v),
                    17763 => "10 1809",
                    int v when v < 18362 => ("10 19H1 Build " + v),
                    18362 => "10 1903",
                    18363 => "10 1909",
                    int v when v < 19041 => ("10 20H1 Build " + v),
                    19041 => "10 2004",
                    int v when v < 20000 => ("10 20H2 Build " + v),
                    _ => ("10 ??? Build " + windowsVersion.Build)
                },
                _ => null,
            };

        private static string GetLinuxVersion(string release, string version)
        {
            var kernelVersion = release;
            if (LinuxKernelVersion.Match(release) is Match m && m.Success)
                kernelVersion = m.Groups["version"].Value;
            if (version.Contains("Ubuntu", StringComparison.InvariantCultureIgnoreCase))
                return "Ubuntu " + kernelVersion;

            if (version.Contains("Debian", StringComparison.InvariantCultureIgnoreCase))
                return "Debian " + kernelVersion;

            if (release.Contains("-MANJARO", StringComparison.InvariantCultureIgnoreCase))
                return "Manjaro " + kernelVersion;

            if (release.Contains("-ARCH", StringComparison.InvariantCultureIgnoreCase))
                return "Arch " + kernelVersion;

            if (release.Contains(".fc"))
            {
                var ver = release.Split('.').FirstOrDefault(p => p.StartsWith("fc"))?[2..];
                return "Fedora " + ver;
            }

            return "Linux " + kernelVersion;
        }

        private static bool IsAmd(string gpuInfo)
        {
            return gpuInfo.Contains("Radeon", StringComparison.InvariantCultureIgnoreCase) ||
                   gpuInfo.Contains("AMD", StringComparison.InvariantCultureIgnoreCase) ||
                   gpuInfo.Contains("ATI ", StringComparison.InvariantCultureIgnoreCase);
        }

        private static bool IsNvidia(string gpuInfo)
        {
            return gpuInfo.Contains("GeForce", StringComparison.InvariantCultureIgnoreCase) ||
                   gpuInfo.Contains("nVidia", StringComparison.InvariantCultureIgnoreCase) ||
                   gpuInfo.Contains("Quadro", StringComparison.InvariantCultureIgnoreCase) ||
                   gpuInfo.Contains("GTX", StringComparison.InvariantCultureIgnoreCase);
        }

        private static string GetTimeFormat(long microseconds)
        {
            if (microseconds < 1000)
                return $"{microseconds} µs";
            if (microseconds < 1_000_000)
                return $"{microseconds / 1000.0:0.##} ms";
            return $"{microseconds / 1_000_000.0:0.##} s";
        }

        private static List<string> SortLines(List<string> notes, DiscordEmoji piracyEmoji = null)
        {
            if (notes == null || notes.Count < 2)
                return notes;

            var priorityList = new List<string>(EmojiPriority);
            if (piracyEmoji != null)
                priorityList.Insert(0, piracyEmoji.ToString());
            return notes
                .Select(s =>
                        {
                            var prioritySymbol = s.Split(PrioritySeparator, 2)[0];
                            var priority = priorityList.IndexOf(prioritySymbol);
                            return new
                            {
                                priority = priority == -1 ? 69 : priority,
                                line = s
                            };
                        })
                .OrderBy(i => i.priority)
                .Select(i => i.line)
                .ToList();
        }

        private static Dictionary<string, int> GetPatches(UniqueList<string> patchList, in bool onlyApplied)
        {
            if (!patchList.Any())
                return new Dictionary<string, int>(0);

            var result = new Dictionary<string, int>(patchList.Count);
            foreach (var patch in patchList)
            {
                var match = ProgramHashPatch.Match(patch);
                if (match.Success && int.TryParse(match.Groups["patch_count"].Value, out var pCount))
                    if (!onlyApplied || pCount > 0)
                        result[match.Groups["hash"].Value] = pCount;
            }
            return result;
        }

        private static HashSet<string> GetHashes(string hashList)
        {
            if (string.IsNullOrEmpty(hashList))
                return new HashSet<string>(0, StringComparer.InvariantCultureIgnoreCase);

            return new HashSet<string>(hashList.Split(Environment.NewLine), StringComparer.InvariantCultureIgnoreCase);
        }

        internal static DiscordEmbedBuilder AddAuthor(this DiscordEmbedBuilder builder, DiscordClient client, DiscordMessage message, ISource source, LogParseState state = null)
        {
            if (state?.Error == LogParseState.ErrorCode.PiracyDetected)
                return builder;

            if (message != null)
            {
                var author = message.Author;
                var member = client.GetMember(message.Channel?.Guild, author);
                string msg;
                if (member == null)
                    msg = $"Log from {author.Username.Sanitize()} | {author.Id}\n";
                else
                    msg = $"Log from {member.DisplayName.Sanitize()} | {member.Id}\n";
                msg += " | " + (source?.SourceType ?? "Unknown source");
                if (state?.ReadBytes > 0 && source?.LogFileSize > 0 && source.LogFileSize < 2L*1024*1024*1024 && state.ReadBytes <= source.LogFileSize)
                    msg += $" | Parsed {state.ReadBytes * 100.0 / source.LogFileSize:0.##}%";
                else if (source?.SourceFilePosition > 0 && source.SourceFileSize > 0 && source.SourceFilePosition <= source.SourceFileSize)
                    msg += $" | Read {source.SourceFilePosition * 100.0 / source.SourceFileSize:0.##}%";
                else if (state?.ReadBytes > 0)
                    msg += $" | Parsed {state.ReadBytes} byte{(state.ReadBytes == 1 ? "" : "s")}";
                else if (source?.LogFileSize > 0)
                    msg += $" | {source.LogFileSize} byte{(source.LogFileSize == 1 ? "" : "s")}";
#if DEBUG
                if (state?.ParsingTime.TotalMilliseconds > 0)
                    msg += $" | {state.ParsingTime.TotalSeconds:0.###}s";
                msg += " | Test Bot Instance";
#endif
                builder.WithFooter(msg);
            }
            return builder;
        }

        private static (int numerator, int denumerator) Reduce(int numerator, int denumerator)
        {
            var gcd = Gcd(numerator, denumerator);
            return (numerator / gcd, denumerator / gcd);
        }

        private static int Gcd(int a, int b)
        {
            while (a != b)
            {
                if (a % b == 0)
                    return b;

                if (b % a == 0)
                    return a;

                if (a > b)
                    a -= b;
                if (b > a)
                    b -= a;
            }
            return a;
        }

        private static List<(string fatalError, int count, double similarity)> GroupSimilar(UniqueList<string> fatalErrors)
        {
            var result = new List<(string fatalError, int count, double similarity)>(fatalErrors.Count);
            if (fatalErrors.Count == 0)
                return result;

            result.Add((fatalErrors[0], 1, 1.0));
            if (fatalErrors.Count < 2)
                return result;

            foreach (var error in fatalErrors[1..])
            {
                int idx = -1;
                double similarity = 0.0;
                for (var i = 0; i < result.Count; i++)
                {
                    similarity = result[i].fatalError.GetFuzzyCoefficientCached(error);
                    if (similarity > 0.95)
                    {
                        idx = i;
                        break;
                    }
                }
                if (idx == -1)
                    result.Add((error, 1, 1.0));
                else
                {
                    var (e, c, s) = result[idx];
                    result[idx] = (e, c + 1, Math.Min(s, similarity));
                }
            }
            return result;
        }
    }
}
