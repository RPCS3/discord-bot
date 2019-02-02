using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.EventHandlers.LogParsing.POCOs;
using DSharpPlus;
using DSharpPlus.Entities;
using IrdLibraryClient;
using IrdLibraryClient.IrdFormat;

namespace CompatBot.Utils.ResultFormatters
{
    internal static class LogParserResult
    {
        private static readonly Client compatClient = new Client();
        private static readonly IrdClient irdClient = new IrdClient();

        // RPCS3 v0.0.3-3-3499d08 Alpha | HEAD
        // RPCS3 v0.0.4-6422-95c6ac699 Alpha | HEAD
        // RPCS3 v0.0.5-7104-a19113025 Alpha | HEAD
        // RPCS3 v0.0.5-42b4ce13a Alpha | minor
        private static readonly Regex BuildInfoInLog = new Regex(@"RPCS3 v(?<version>(\d|\.)+)(-(?<build>\d+))?-(?<commit>[0-9a-f]+) (?<stage>\w+) \| (?<branch>.*?)\r?$",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // rpcs3-v0.0.5-7105-064d0619_win64.7z
        // rpcs3-v0.0.5-7105-064d0619_linux64.AppImage
        private static readonly Regex BuildInfoInUpdate = new Regex(@"rpcs3-v(?<version>(\d|\.)+)(-(?<build>\d+))?-(?<commit>[0-9a-f]+)_",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Regex VulkanDeviceInfo = new Regex(@"'(?<device_name>.+)' running on driver (?<version>.+)\r?$",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Singleline);

        private static readonly Version MinimumOpenGLVersion = new Version(4, 3);
        private static readonly Version RecommendedOpenGLVersion = new Version(4, 5);

        private static readonly Dictionary<string, string> KnownDiscOnPsnIds = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            {"BLES00932", "NPEB01202"},
            {"BLUS30443", "NPUB30910"},
            //{"BCJS30022", "NPJA00102"},
            {"BCJS70013", "NPJA00102"},
        };

        private static readonly TimeSpan OldBuild = TimeSpan.FromDays(30);
        private static readonly TimeSpan VeryOldBuild = TimeSpan.FromDays(60);
        //private static readonly TimeSpan VeryVeryOldBuild = TimeSpan.FromDays(90);
        private static readonly TimeSpan AncientBuild = TimeSpan.FromDays(180);
        private static readonly TimeSpan PrehistoricBuild = TimeSpan.FromDays(365);

        private static readonly char[] PrioritySeparator = {' '};
        private static readonly string[] EmojiPriority = { "😱", "💢", "‼", "❗",  "❌", "⁉", "⚠", "❔", "✅", "ℹ" };
        private const string TrueMark = "[x]";
        private const string FalseMark = "[ ]";

        public static async Task<DiscordEmbed> AsEmbedAsync(this LogParseState state, DiscordClient client, DiscordMessage message)
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
            return builder.Build();
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
                items["driver_version_info"] = GetOpenglDriverVersion(items["gpu_info"], (items["driver_version_new"] ?? items["driver_version"])) ??
                                               GetVulkanDriverVersion(items["vulkan_initialized_device"], items["vulkan_found_device"]) ??
                                               GetVulkanDriverVersionRaw(items["gpu_info"], items["vulkan_driver_version_raw"]);
            if (items["driver_version_info"] != null)
                items["gpu_info"] += $" ({items["driver_version_info"]})";

            if (items["vulkan_compatible_device_name"] is string vulkanDevices)
            {
                var deviceNames = vulkanDevices.Split(Environment.NewLine)
                    .Distinct()
                    .Select(n => $"{n} ({GetVulkanDriverVersion(n, items["vulkan_found_device"])})");
                items["gpu_available_info"] = string.Join(Environment.NewLine, deviceNames);
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
                items["os_path"] = "Windows";
            else if (items["lin_path"] != null)
                items["os_path"] = "Linux";
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
                    value = TrueMark;
                else if ("false".Equals(value, StringComparison.CurrentCultureIgnoreCase))
                    value = FalseMark;
                items[key] = value.Sanitize(false);
            }
        }

        private static void BuildInfoSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            var systemInfo = $"{items["build_and_specs"]}";
            if (!string.IsNullOrEmpty(items["os_path"]) && !string.IsNullOrEmpty(systemInfo))
            {
                var sysInfoParts = systemInfo.Split(new[] {'\r', '\n'}, 2, StringSplitOptions.RemoveEmptyEntries);
                systemInfo = $"{sysInfoParts[0]} | {items["os_path"]}";
                if (sysInfoParts.Length > 1)
                    systemInfo += Environment.NewLine + sysInfoParts[1];
            }
            if (items["gpu_info"] is string gpu)
                systemInfo += $"{Environment.NewLine}GPU: {gpu}";
            else if (items["gpu_available_info"] is string availableGpus)
            {
                var multiple = availableGpus.Contains(Environment.NewLine);
                systemInfo +=$"{Environment.NewLine}GPU{(multiple ? "s" : "")}:{(multiple ? Environment.NewLine : " ")}{availableGpus}";
            }
            builder.AddField("Build Info", systemInfo.Trim(EmbedPager.MaxFieldLength));
        }

        private static (string name, List<string> lines) BuildCpuSection(NameValueCollection items)
        {
            if (string.IsNullOrEmpty(items["ppu_decoder"]))
                return (null, null);

            var lines = new List<string>
            {
                $"PPU Decoder: {items["ppu_decoder"],21}",
                $"SPU Decoder: {items["spu_decoder"],21}",
                $"SPU Lower Thread Priority: {items["spu_lower_thread_priority"],7}",
                $"SPU Loop Detection: {items["spu_loop_detection"],14}",
                $"Thread Scheduler: {items["thread_scheduler"],16}",
                $"SPU Threads: {items["spu_threads"],21}",
                $"SPU Block Size: {items["spu_block_size"] ?? "N/A",18}",
                $"Accurate xfloat: {items["accurate_xfloat"] ?? "N/A",17}",
                $"Force CPU Blit: {items["cpu_blit"] ?? "N/A",18}",
                $"Lib Loader: {items["lib_loader"],22}",
            };
            return ("CPU Settings", lines);
        }

        private static (string name, List<string> lines) BuildGpuSection(NameValueCollection items)
        {
            if (string.IsNullOrEmpty(items["renderer"]))
                return (null, null);

            var lines = new List<string>
            {

                $"Renderer: {items["renderer"], 24}",
                $"Aspect ratio: {items["aspect_ratio"], 20}",
                $"Resolution: {items["resolution"], 22}",
                $"Resolution Scale: {items["resolution_scale"] ?? "N/A", 16}",
                $"Resolution Scale Threshold: {items["texture_scale_threshold"] ?? "N/A", 6}",
                $"Write Color Buffers: {items["write_color_buffers"], 13}",
                $"Anisotropic Filter: {items["af_override"] ?? "N/A", 14}",
                $"Frame Limit: {items["frame_limit"], 21}",
                $"Disable Async Shaders: {items["async_shaders"] ?? "N/A", 11}",
                $"Disable Vertex Cache: {items["vertex_cache"], 12}",
            };
            return ("GPU Settings", lines);
        }

        private static void BuildSettingsSections(DiscordEmbedBuilder builder, NameValueCollection items, (string name, List<string> lines) colA, (string name, List<string> lines) colB)
        {
            if (colA.lines?.Count > 0 && colB.lines?.Count > 0)
            {
                var isCustomSettings = items["custom_config"] != null;
                var colAToRemove = colA.lines.Count(l => l.EndsWith("N/A"));
                var colBToRemove = colB.lines.Count(l => l.EndsWith("N/A"));
                var linesToRemove = Math.Min(colAToRemove, colBToRemove);
                if (linesToRemove > 0)
                {
                    var linesToSkip = colAToRemove - linesToRemove;
                    var tmp = colA.lines;
                    colA.lines = new List<string>(tmp.Count - linesToRemove);
                    for (var i = 0; i < tmp.Count; i++)
                        if (!tmp[i].EndsWith("N/A") || (linesToSkip--) > 0)
                            colA.lines.Add(tmp[i]);

                    linesToSkip = colBToRemove - linesToRemove;
                    tmp = colB.lines;
                    colB.lines = new List<string>(tmp.Count - linesToRemove);
                    for (var i = 0; i < tmp.Count; i++)
                        if (!tmp[i].EndsWith("N/A") || (linesToSkip--) > 0)
                            colB.lines.Add(tmp[i]);
                }
                AddSettingsSection(builder, colA.name, colA.lines, isCustomSettings);
                AddSettingsSection(builder, colB.name, colB.lines, isCustomSettings);
            }
        }

        private static void AddSettingsSection(DiscordEmbedBuilder builder, string name, List<string> lines, bool isCustomSettings)
        {
            var result = new StringBuilder();
            foreach (var line in lines)
                result.Append("`").Append(line).AppendLine("`");
            if (isCustomSettings)
                name = "Per-game " + name;
            builder.AddField(name, result.ToString().FixSpaces(), true);
        }

        private static void BuildLibsSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            if (items["lib_loader"] is string libs && libs.Contains("manual", StringComparison.InvariantCultureIgnoreCase))
                builder.AddField("Selected Libraries", items["library_list"]?.Trim(1024));
        }

        private static void BuildWeirdSettingsSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            var notes = new List<string>();
            if (!string.IsNullOrEmpty(items["resolution"]) && items["resolution"] != "1280x720")
                notes.Add("⚠ `Resolution` was changed from the recommended `1280x720`");
            if (items["hook_static_functions"] is string hookStaticFunctions && hookStaticFunctions == TrueMark)
                notes.Add("⚠ `Hook Static Functions` is enabled, please disable");
            if (items["host_root"] is string hostRoot && hostRoot == TrueMark)
                notes.Add("❔ `/host_root/` is enabled");
            if (items["gpu_texture_scaling"] is string gpuTextureScaling && gpuTextureScaling == TrueMark)
                notes.Add("⚠ `GPU Texture Scaling` is enabled, please disable");
            if (items["af_override"] is string af)
            {
                if (af == "Disabled")
                    notes.Add("❌ `Anisotropic Filter` is `Disabled`, please use `Auto` instead");
                else if (af.ToLowerInvariant() != "auto" && af != "16")
                    notes.Add($"⁉ `Anisotropic Filter` is set to `{af}x`, which makes little sense over `16x` or `Auto`");
            }
            if (items["resolution_scale"] is string resScale && int.TryParse(resScale, out var resScaleFactor) && resScaleFactor < 100)
                notes.Add($"⁉ `Resolution Scale` is `{resScale}%`.");
            if (items["cpu_blit"] is string cpuBlit && cpuBlit == TrueMark && items["write_color_buffers"] is string wcb && wcb == FalseMark)
                notes.Add("⚠ `Force CPU Blit` is enabled, but `Write Color Buffers` is disabled");
            if (items["zcull"] is string zcull && zcull == TrueMark)
                notes.Add("⚠ `ZCull Occlusion Queries` are disabled, can result in visual artifacts");
            if (items["driver_recovery_timeout"] is string driverRecoveryTimeout && int.TryParse(driverRecoveryTimeout, out var drtValue) && drtValue != 1000000)
            {
                if (drtValue == 0)
                    notes.Add("⚠ `Driver Recovery Timeout` is set to 0 (infinite), please use default value of 1000000");
                else if (drtValue < 10_000)
                    notes.Add($"⚠ `Driver Recovery Timeout` is set too low: {GetTimeFormat(drtValue)} (1 frame @ {(1_000_000.0 / drtValue):0.##} fps)");
                else if (drtValue > 10_000_000)
                    notes.Add($"⚠ `Driver Recovery Timeout` is set too high: {GetTimeFormat(drtValue)}");
            }
            if (items["hle_lwmutex"] is string hleLwmutex && hleLwmutex == TrueMark)
                notes.Add("⚠ `HLE lwmutex` is enabled, might affect compatibility");
            if (items["spu_block_size"] is string spuBlockSize)
            {
/*
                if (spuBlockSize == "Giga")
                    notes.AppendLine("`Giga` mode for `SPU Block Size` is strongly not recommended to use");
*/
                if (spuBlockSize != "Safe")
                    notes.Add($"⚠ Please use `Safe` mode for `SPU Block Size`. `{spuBlockSize}` is currently unstable.");
            }

            if (items["lib_loader"] is string libLoader
                && libLoader.Contains("Auto", StringComparison.InvariantCultureIgnoreCase)
                && (libLoader == "Auto"
                    || (libLoader.Contains("manual", StringComparison.InvariantCultureIgnoreCase) && string.IsNullOrEmpty(items["library_list"]))))
            {
                notes.Add("⚠ Please use `Load liblv2.sprx only` as a `Library loader`");
            }

            var notesContent = new StringBuilder();
            foreach (var line in SortLines(notes))
                notesContent.AppendLine(line);
            PageSection(builder, notesContent.ToString().Trim(), "Important Settings to Review");
        }

        private static void BuildMissingLicensesSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            if (items["rap_file"] is string rap)
            {
                var limitTo = 5;
                var licenseNames = rap.Split(Environment.NewLine).Distinct().Select(p => $"`{Path.GetFileName(p)}`").ToList();
                string content;
                if (licenseNames.Count > limitTo)
                {
                    content = string.Join(Environment.NewLine, licenseNames.Take(limitTo - 1));
                    var other = licenseNames.Count - limitTo + 1;
                    content += $"{Environment.NewLine}and {other} other license{StringUtils.GetSuffix(other)}";
                }
                else
                    content = string.Join(Environment.NewLine, licenseNames);
                builder.AddField("Missing Licenses", content);
            }
        }

        private static async Task<(bool irdChecked, bool broken)> HasBrokenFilesAsync(NameValueCollection items)
        {
            if (!(items["serial"] is string productCode))
                return (false, false);

            if (!productCode.StartsWith("B") && !productCode.StartsWith("M"))
                return (false, false);

            if (string.IsNullOrEmpty(items["broken_directory"])
                && string.IsNullOrEmpty(items["broken_filename"]))
                return (false, false);

            var getIrdTask = irdClient.DownloadAsync(productCode, Config.IrdCachePath, Config.Cts.Token);
            var missingDirs = items["broken_directory"]?.Split(Environment.NewLine).Distinct().ToList() ?? new List<string>(0);
            var missingFiles = items["broken_filename"]?.Split(Environment.NewLine).Distinct().ToList() ?? new List<string>(0);
            HashSet<string> knownFiles;
            try
            {
                var irdFiles = await getIrdTask.ConfigureAwait(false);
                knownFiles = new HashSet<string>(
                    from ird in irdFiles
                    from name in ird.GetFilenames()
                    select name,
                    StringComparer.InvariantCultureIgnoreCase
                );
            }
            catch (Exception e)
            {
                Config.Log.Warn(e, "Failed to get IRD files for " + productCode);
                return (false, false);
            }
            if (knownFiles.Count == 0)
                return (false, false);

            var broken = missingFiles.Any(knownFiles.Contains);
            if (broken)
                return (true, true);

            var knownDirs = new HashSet<string>(knownFiles.Select(f => Path.GetDirectoryName(f).Replace('\\', '/')), StringComparer.InvariantCultureIgnoreCase);
            return (true, missingDirs.Any(knownDirs.Contains));
        }

        private static async Task BuildNotesSectionAsync(DiscordEmbedBuilder builder, LogParseState state, NameValueCollection items, DiscordClient discordClient)
        {
            BuildWeirdSettingsSection(builder, items);
            BuildMissingLicensesSection(builder, items);
            var (irdChecked, brokenDump) = await HasBrokenFilesAsync(items).ConfigureAwait(false);
            brokenDump |= !string.IsNullOrEmpty(items["edat_block_offset"]);
            var elfBootPath = items["elf_boot_path"] ?? "";
            var isEboot = !string.IsNullOrEmpty(elfBootPath) && elfBootPath.EndsWith("EBOOT.BIN", StringComparison.InvariantCultureIgnoreCase);
            var isElf = !string.IsNullOrEmpty(elfBootPath) && !elfBootPath.EndsWith("EBOOT.BIN", StringComparison.InvariantCultureIgnoreCase);
            var notes = new List<string>();
            if (items["fatal_error"] is string fatalError)
            {
                builder.AddField("Fatal Error", $"```{fatalError.Trim(1022)}```");
                if (fatalError.Contains("psf.cpp") || fatalError.Contains("invalid map<K, T>"))
                    notes.Add("⚠ Game save data might be corrupted");
            }
            if (items["failed_to_decrypt"] is string _)
                notes.Add("❌ Failed to decrypt game content, license file might be corrupted");
            if (items["failed_to_boot"] is string _)
                notes.Add("❌ Failed to boot the game, the dump might be encrypted or corrupted");
            if (brokenDump)
                notes.Add("❌ Some game files are missing or corrupted, please re-dump and validate.");
            else if (irdChecked)
                notes.Add("✅ Checked against IRD file and found no issues");
            if (!string.IsNullOrEmpty(items["host_root_in_boot"]) && isEboot)
                notes.Add("❌ Retail game booted as an ELF through the `/root_host/`, probably due to passing path as an argument; please boot through the game library list for now");
            if (!string.IsNullOrEmpty(items["serial"]) && isElf)
                notes.Add($"⚠ Retail game booted directly through `{Path.GetFileName(elfBootPath)}`, which is not recommended");
            if (string.IsNullOrEmpty(items["serial"] + items["game_title"]) && items["fw_version_installed"] is string fwVersion)
            {
                notes.Add($"ℹ The log contains only installation of firmware {fwVersion}");
                notes.Add("ℹ Please boot the game and upload a new log");
            }
            if (string.IsNullOrEmpty(items["ppu_decoder"]) || string.IsNullOrEmpty(items["renderer"]))
            {
                notes.Add("ℹ The log is empty");
                notes.Add("ℹ Please boot the game and upload a new log");
            }

            Version oglVersion = null;
            if (items["opengl_version"] is string oglVersionString)
                Version.TryParse(oglVersionString, out oglVersion);
            if (items["glsl_version"] is string glslVersionString && Version.TryParse(glslVersionString, out var glslVersion))
            {
                glslVersion = new Version(glslVersion.Major, glslVersion.Minor/10);
                if (oglVersion == null || glslVersion > oglVersion)
                    oglVersion = glslVersion;
            }
            if (oglVersion != null)
            {
                if (oglVersion < MinimumOpenGLVersion)
                    notes.Add($"❌ GPU only supports OpenGL {oglVersion.Major}.{oglVersion.Minor}, which is below the minimum requirement of {MinimumOpenGLVersion}");
            }
            if (!string.IsNullOrEmpty(items["ppu_hash_patch"]) || !string.IsNullOrEmpty(items["spu_hash_patch"]))
                notes.Add("ℹ Game-specific patches were applied");

            bool discInsideGame = false;
            bool discAsPkg = false;
            if (items["game_category"] == "DG")
            {
                discInsideGame |= !string.IsNullOrEmpty(items["ldr_disc"]) && !(items["serial"]?.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase) ?? false);
                discAsPkg |= items["serial"]?.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase) ?? false;
                discAsPkg |= items["ldr_game_serial"] is string ldrGameSerial
                             && ldrGameSerial.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase);
            }
            discAsPkg |= items["game_category"] == "HG" && !(items["serial"]?.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase) ?? false);
            if (discInsideGame)
                notes.Add($"❌ Disc game inside `{items["ldr_disc"]}`");
            if (discAsPkg)
            {
                var emoji = discordClient.GetEmoji(":piratethink:", DiscordEmoji.FromUnicode("🔨"));
                notes.Add($"{emoji} Disc game installed as a PKG ");
            }

            if (!string.IsNullOrEmpty(items["native_ui_input"]))
                notes.Add("⚠ Pad initialization problem detected; try disabling `Native UI`");
            if (!string.IsNullOrEmpty(items["xaudio_init_error"]))
                notes.Add("❌ XAudio initialization failed; make sure you have audio output device working");

            if (!string.IsNullOrEmpty(items["fw_missing_msg"])
                || !string.IsNullOrEmpty(items["fw_missing_something"]))
                notes.Add("❌ PS3 firmware is missing or corrupted");

            var updateInfo = await CheckForUpdateAsync(items).ConfigureAwait(false);
            if (updateInfo != null)
            {
                string prefix = "⚠";
                string timeDeltaStr;
                if (updateInfo.GetUpdateDelta() is TimeSpan timeDelta)
                {
                    timeDeltaStr = timeDelta.AsTimeDeltaDescription() + " old";
                    if (timeDelta > PrehistoricBuild)
                        prefix = "😱";
                    else if (timeDelta > AncientBuild)
                        prefix = "💢";
                    //else if (timeDelta > VeryVeryOldBuild)
                    //    prefix = "💢";
                    else if (timeDelta > VeryOldBuild)
                        prefix = "‼";
                    else if (timeDelta > OldBuild)
                        prefix = "❗";
                }
                else
                    timeDeltaStr = "outdated";
                notes.Add($"{prefix} This RPCS3 build is {timeDeltaStr}, please consider updating it");
            }

            if (state.Error == LogParseState.ErrorCode.SizeLimit)
                notes.Add("ℹ The log was too large, so only the last processed run is shown");

            var notesContent = new StringBuilder();
            foreach (var line in SortLines(notes))
                notesContent.AppendLine(line);
            PageSection(builder, notesContent.ToString().Trim(), "Notes");
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
            if (!(items["build_and_specs"] is string buildAndSpecs))
                return null;

            var buildInfo = BuildInfoInLog.Match(buildAndSpecs.ToLowerInvariant());
            if (!buildInfo.Success || buildInfo.Groups["branch"].Value != "head")
                return null;

            var currentBuildCommit = buildInfo.Groups["commit"].Value;
            if (string.IsNullOrEmpty(currentBuildCommit))
                currentBuildCommit = null;
            var updateInfo = await compatClient.GetUpdateAsync(Config.Cts.Token, currentBuildCommit).ConfigureAwait(false);
            if (updateInfo?.ReturnCode != 1 && currentBuildCommit != null)
                updateInfo = await compatClient.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
            var link = updateInfo?.LatestBuild?.Windows?.Download ?? updateInfo?.LatestBuild?.Linux?.Download;
            if (string.IsNullOrEmpty(link))
                return null;

            var latestBuildInfo = BuildInfoInUpdate.Match(link.ToLowerInvariant());
            if (latestBuildInfo.Success && VersionIsTooOld(buildInfo, latestBuildInfo, updateInfo))
                return updateInfo;

            return null;
        }

        private static bool VersionIsTooOld(Match log, Match update, UpdateInfo updateInfo)
        {
            if ((updateInfo.GetUpdateDelta() is TimeSpan updateTimeDelta) && (updateTimeDelta < Config.BuildTimeDifferenceForOutdatedBuilds))
                return false;

            if (Version.TryParse(log.Groups["version"].Value, out var logVersion) && Version.TryParse(update.Groups["version"].Value, out var updateVersion))
            {
                if (logVersion < updateVersion)
                    return true;

                if (int.TryParse(log.Groups["build"].Value, out var logBuild) && int.TryParse(update.Groups["build"].Value, out var updateBuild))
                {
                    if (logBuild + Config.BuildNumberDifferenceForOutdatedBuilds < updateBuild)
                        return true;
                }
                return false;
            }
            return !SameCommits(log.Groups["commit"].Value, update.Groups["commit"].Value);
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
                return AmdDriverVersionProvider.GetFromOpenglAsync(version).GetAwaiter().GetResult();

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

                return AmdDriverVersionProvider.GetFromVulkanAsync(result).GetAwaiter().GetResult();
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
            if (gpuInfo.Contains("Radeon", StringComparison.InvariantCultureIgnoreCase) ||
                gpuInfo.Contains("AMD", StringComparison.InvariantCultureIgnoreCase) ||
                gpuInfo.Contains("ATI ", StringComparison.InvariantCultureIgnoreCase))
            {
                var major = (ver >> 22) & 0x3ff;
                var minor = (ver >> 12) & 0x3ff;
                var patch = ver & 0xfff;
                var result = $"{major}.{minor}.{patch}";
                if (gpuInfo.Contains("RADV", StringComparison.InvariantCultureIgnoreCase))
                    return result;

                return AmdDriverVersionProvider.GetFromVulkanAsync(result).GetAwaiter().GetResult();
            }
            else
            {
                var major = (ver >> 22) & 0x3ff;
                var minor = (ver >> 14) & 0xff;
                var patch = ver & 0x3fff;
                if (major == 0 && gpuInfo.Contains("Intel", StringComparison.InvariantCultureIgnoreCase))
                    return $"{minor}.{patch}";

                if (gpuInfo.Contains("GeForce", StringComparison.InvariantCultureIgnoreCase) ||
                    gpuInfo.Contains("nVidia", StringComparison.InvariantCultureIgnoreCase) ||
                    gpuInfo.Contains("Quadro", StringComparison.InvariantCultureIgnoreCase))
                {
                    if (patch == 0)
                        return $"{major}.{minor}";
                    return $"{major}.{minor:00}.{(patch >> 6) & 0xff}.{patch & 0x3f}";
                }

                return $"{major}.{minor}.{patch}";
            }
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
                priorityList.Add(piracyEmoji.ToString());
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
    }
}
