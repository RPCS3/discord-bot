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

        private static readonly RegexOptions DefaultSingleLine = RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Singleline;
        // RPCS3 v0.0.3-3-3499d08 Alpha | HEAD
        // RPCS3 v0.0.4-6422-95c6ac699 Alpha | HEAD
        // RPCS3 v0.0.5-7104-a19113025 Alpha | HEAD
        // RPCS3 v0.0.5-42b4ce13a Alpha | minor
        private static readonly Regex BuildInfoInLog = new Regex(
            @"RPCS3 v(?<version_string>(?<version>(\d|\.)+)(-(?<build>\d+))?-(?<commit>[0-9a-f]+|unknown)) (?<stage>\w+)( \| (?<branch>[^|]+))?( \| Firmware version: (?<fw_version_installed>[^|\r\n]+)( \| (?<unknown>.*))?)?\r?$",
            DefaultSingleLine);
        private static readonly Regex CpuInfoInLog = new Regex(
            @"(?<cpu_model>[^|@]+)(@\s*(?<cpu_speed>.+)\s*GHz\s*)? \| (?<thread_count>\d+) Threads \| (?<memory_amount>[0-9\.\,]+) GiB RAM( \| (?<cpu_extensions>.*?))?\r?$",
            DefaultSingleLine);
        private static readonly Regex OsInfoInLog = new Regex(
            @"Operating system: (?<os_type>[^,]+), (Name: (?<posix_name>[^,]+), Release: (?<posix_release>[^,]+), Version: (?<posix_version>[^,\r\n]+)|Major: (?<os_version_major>\d+), Minor: (?<os_version_minor>\d+), Build: (?<os_version_build>\d+), Service Pack: (?<os_service_pack>[^,]+), Compatibility mode: (?<os_compat_mode>[^,\r\n]+))\r?$",
            DefaultSingleLine);
        private static readonly Regex LinuxKernelVersion = new Regex(@"(?<version>\d+\.\d+\.\d+(-\d+)?)", DefaultSingleLine);
        private static readonly char[] NewLineChars = {'\r', '\n'};

        // rpcs3-v0.0.5-7105-064d0619_win64.7z
        // rpcs3-v0.0.5-7105-064d0619_linux64.AppImage
        private static readonly Regex BuildInfoInUpdate = new Regex(@"rpcs3-v(?<version>(\d|\.)+)(-(?<build>\d+))?-(?<commit>[0-9a-f]+)_", DefaultSingleLine);
        private static readonly Regex VulkanDeviceInfo = new Regex(@"'(?<device_name>.+)' running on driver (?<version>.+)\r?$", DefaultSingleLine);
        private static readonly Regex IntelGpuModel = new Regex(@"Intel\s?(®|\(R\))? (?<gpu_model>(?<gpu_family>(\w| )+Graphics)( (?<gpu_model_number>P?\d+))?)(\s+\(|$)", DefaultSingleLine);

        private static readonly Version MinimumOpenGLVersion = new Version(4, 3);
        private static readonly Version MinimumFirmwareVersion = new Version(4, 80);
        private static readonly Version NvidiaFullscreenBugMinVersion = new Version(400, 0);
        private static readonly Version NvidiaFullscreenBugMaxVersion = new Version(499, 99);
        private static readonly Version NvidiaRecommendedOldWindowsVersion = new Version(399, 24);

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
        };

        private static readonly string[] KnownMegaSpuBlockSizeIds =
        {
            "BLUS30481", "BLES00826", "BLJM60223", // nier
        };

        private static readonly HashSet<string> KnownWriteColorBuffersIds = new HashSet<string>
        {
            "BLES00932", "BLUS30443", "BCJS70013", "BCJS30022", // DeS
            "BLUS30481", "BLES00826", "BLJM60223", // Nier
            "BCES00510", "BCUS98111", "BCJS37001", "NPUA70080", // God of War 3 / Demo
        };

        private static readonly HashSet<string> KnownMotionControlsIds = new HashSet<string>
        {
            "BCES00797", "BCES00802", "BCUS98164", "BCJS30040", "NPEA90053", "NPEA90076", "NPUA70088", "NPUA70112", // heavy rain
            "BCAS25017", "BCES01121", "BCES01122", "BCES01123", "BCUS98298", "NPEA00513", "NPUA81087", "NPEA90127", "NPJA90259", "NPUA72074", "NPJA00097", // beyond two souls
            "NPEA00094", "NPEA00250", "NPJA00039", "NPUA80083", // flower
            "NPEA00036", "NPUA80069", "NPJA00004", // locoroco
        };

        private static readonly HashSet<string> KnownGamesThatRequireInterpreter = new HashSet<string>
        {
            "NPEB00630", "NPUB30493", "NPJB00161", // daytona usa
            "BCAS25017", "BCES01121", "BCES01122", "BCES01123", "BCUS98298", "NPEA00513", "NPUA81087", "NPEA90127", "NPJA90259", "NPUA72074", "NPJA00097", // beyond two souls
        };

        private static readonly HashSet<string> KnownBogusLicenses = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "UP0700-NPUB30932_00-NNKDLFULLGAMEPTB.rap",
            "EP0700-NPEB01158_00-NNKDLFULLGAMEPTB.rap",
        };

        private static readonly TimeSpan OldBuild = TimeSpan.FromDays(30);
        private static readonly TimeSpan VeryOldBuild = TimeSpan.FromDays(60);
        //private static readonly TimeSpan VeryVeryOldBuild = TimeSpan.FromDays(90);
        private static readonly TimeSpan AncientBuild = TimeSpan.FromDays(180);
        private static readonly TimeSpan PrehistoricBuild = TimeSpan.FromDays(365);

        private static readonly char[] PrioritySeparator = {' '};
        private static readonly string[] EmojiPriority = { "😱", "💢", "‼", "❗",  "❌", "⁉", "⚠", "❔", "✅", "ℹ" };
        private const string EnabledMark = "[x]";
        private const string DisabledMark = "[ ]";

        public static async Task<DiscordEmbedBuilder> AsEmbedAsync(this LogParseState state, DiscordClient client, DiscordMessage message, ISource source)
        {
            DiscordEmbedBuilder builder;
            var collection = state.CompleteCollection ?? state.WipCollection;
            if (collection?.Count > 0)
            {
                if (collection["serial"] is string serial
                    && KnownDiscOnPsnIds.TryGetValue(serial, out var psnSerial)
                    && collection["ldr_game_serial"] is string ldrGameSerial
                    && ldrGameSerial.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase)
                    && ldrGameSerial.Equals(psnSerial, StringComparison.InvariantCultureIgnoreCase))
                {
                    collection["serial"] = psnSerial;
                    collection["game_category"] = "HG";
                }
                var gameInfo = await client.LookupGameInfoAsync(collection["serial"], collection["game_title"], true).ConfigureAwait(false);
                builder = new DiscordEmbedBuilder(gameInfo) {ThumbnailUrl = null}; // or this will fuck up all formatting
                if (state.Error == LogParseState.ErrorCode.PiracyDetected)
                {
                    state.PiracyContext = state.PiracyContext.Sanitize();
                    var msg = "__You are being denied further support until you legally dump the game__.\n" +
                              "Please note that the RPCS3 community and its developers do not support piracy.\n" +
                              "Most of the issues with pirated dumps occur due to them having been tampered with in some way " +
                              "and therefore act unpredictably on RPCS3.\n" +
                              "If you need help obtaining legal dumps, please read [the quickstart guide](https://rpcs3.net/quickstart).";
                    builder.WithColor(Config.Colors.LogAlert)
                        .WithTitle("Pirated release detected")
                        .WithDescription(msg);
                }
                else
                {
                    CleanupValues(collection);
                    BuildInfoSection(builder, collection);
                    var colA = BuildCpuSection(collection);
                    var colB = BuildGpuSection(collection);
                    BuildSettingsSections(builder, collection, colA, colB);
                    BuildLibsSection(builder, collection);
                    await BuildNotesSectionAsync(builder, state, collection, client).ConfigureAwait(false);
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

        private static void CleanupValues(NameValueCollection items)
        {
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
                items["driver_version_info"] = GetOpenglDriverVersion(items["gpu_info"], items["driver_version_new"] ?? items["driver_version"]) ??
                                               GetVulkanDriverVersion(items["vulkan_initialized_device"], items["vulkan_found_device"]) ??
                                               GetVulkanDriverVersionRaw(items["gpu_info"], items["vulkan_driver_version_raw"]);
            }
            if (items["driver_version_info"] != null)
                items["gpu_info"] += $" ({items["driver_version_info"]})";

            if (items["vulkan_compatible_device_name"] is string vulkanDevices)
            {
                var devices = vulkanDevices.Split(Environment.NewLine)
                    .Distinct()
                    .Select(n => new {name = n.StripMarks(), driverVersion = GetVulkanDriverVersion(n, items["vulkan_found_device"])})
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
            if (items["lib_loader"] is string libLoader)
            {
                var auto = libLoader.Contains("auto", StringComparison.InvariantCultureIgnoreCase);
                var manual = libLoader.Contains("manual", StringComparison.InvariantCultureIgnoreCase);
                if (auto && manual)
                    items["lib_loader"] = "Auto & manual select";
                else if (auto)
                    items["lib_loader"] = "Auto";
                else if (manual)
                    items["lib_loader"] = "Manual selection";
            }
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
                var fields = new EmbedPager().BreakInFieldContent(notesContent.Split(Environment.NewLine), 100).ToList();
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
            return commitA.Substring(0, len) == commitB.Substring(0, len);
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

        private static string GetVulkanDriverVersion(string gpu, string foundDevices)
        {
            if (string.IsNullOrEmpty(gpu) || string.IsNullOrEmpty(foundDevices))
                return null;

            var info = (from line in foundDevices.Split(Environment.NewLine)
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

            if (result.EndsWith(".0.0"))
                result = result.Substring(0, result.Length - 4);
            if (result.Length > 3 && result[result.Length - 2] == '.')
                result = result.Substring(0, result.Length - 1) + "0" + result[result.Length - 1];
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

            switch (driverVer.Major)
            {
                case 6: //XDDM
                    return "XP";
                case 7:
                    return "Vista";
                case 8:
                    return "7";
                case 9:
                    return "8";
                case 10:
                    return "8.1";
                case int v when v >= 20 && v < 30:
                    var wddmMinor = v % 10;
                    switch (wddmMinor)
                    {
                        // see https://en.wikipedia.org/wiki/Windows_Display_Driver_Model#WDDM_2.0
                        case 0:
                            return "10";
                        case 1:
                            return "10 1607";
                        case 2:
                            return "10 1703";
                        case 3:
                            return "10 1709";
                        case 4:
                            return "10 1803";
                        case 5:
                            return "10 1809";
                        case 6:
                            return "10 1903";
                        default:
                            Config.Log.Warn($"Invalid WDDM version 2.{wddmMinor} in driver version {driverVersionString}");
                            return null;
                    }
                default:
                    Config.Log.Warn("Invalid video driver version " + driverVersionString);
                    return null;
            }
        }

        private static string GetWindowsVersion(Version windowsVersion)
        {
            switch (windowsVersion.Major)
            {
                case 5:
                {
                    switch (windowsVersion.Minor)
                    {
                        case 0:
                            return "2000";
                        case 1:
                            return "XP";
                        case 2:
                            return "XP x64";
                        default:
                            Config.Log.Warn("Invalid Windows version " + windowsVersion);
                            return null;
                    }
                }
                case 6:
                {
                    switch (windowsVersion.Minor)
                    {
                        case 0:
                            return "Vista";
                        case 1:
                            return "7";
                        case 2:
                            return "8";
                        case 3:
                            return "8.1";
                        default:
                            Config.Log.Warn("Invalid Windows version " + windowsVersion);
                            return null;
                    }
                }
                case 10:
                {
                    switch (windowsVersion.Build)
                    {
                        case int v when v < 10240:
                            return "10 TH1 Build " + v;
                        case 10240:
                            return "10 1507";
                        case int v when v < 10586:
                            return "10 TH2 Build " + v;
                        case 10586:
                            return "10 1511";
                        case int v when v < 14393:
                            return "10 RS1 Build " + v;
                        case 14393:
                            return "10 1607";
                        case int v when v < 15063:
                            return "10 RS2 Build " + v;
                        case 15063:
                            return "10 1703";
                        case int v when v < 16299:
                            return "10 RS3 Build " + v;
                        case 16299:
                            return "10 1709";
                        case int v when v < 17134:
                            return "10 RS4 Build " + v;
                        case 17134:
                            return "10 1803";
                        case int v when v < 17763:
                            return "10 RS5 Build " + v;
                        case 17763:
                            return "10 1809";
                        case int v when v < 18362:
                            return "10 19H1 Build " + v;
                        case 18362:
                            return "10 1903";
                        case int v when v < 18836:
                            return "10 19H2 Build " + v;
                        case int v when v < 20000:
                            return "10 20H1 Build " + v;
                        default:
                            Config.Log.Warn("Invalid Windows version " + windowsVersion);
                            return "10 ??? Build " + windowsVersion.Build;
                    }
                }
                default:
                    Config.Log.Warn("Invalid Windows version " + windowsVersion);
                    return null;
            }
        }

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
                var ver = release.Split('.').FirstOrDefault(p => p.StartsWith("fc"))?.Substring(2);
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
                   gpuInfo.Contains("Quadro", StringComparison.InvariantCultureIgnoreCase);
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

        private static Dictionary<string, int> GetPatches(string hashList, string patchesList)
        {
            if (string.IsNullOrEmpty(hashList) || string.IsNullOrEmpty(patchesList))
                return new Dictionary<string, int>(0);

            var hashes = hashList.Split(Environment.NewLine);
            var patches = patchesList.Split(Environment.NewLine);
            if (hashes.Length != patches.Length)
            {
                Config.Log.Warn($"Hashes count: {hashes.Length}, Patches count: {patches.Length}");
                return new Dictionary<string, int>(0);
            }

            var result = new Dictionary<string, int>();
            for (var i = 0; i < hashes.Length; i++)
            {
                int.TryParse(patches[i], out var pCount);
                result[hashes[i]] = pCount;
            }
            return result;
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
    }
}
