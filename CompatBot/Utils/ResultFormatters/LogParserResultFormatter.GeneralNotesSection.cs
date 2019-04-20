using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.EventHandlers.LogParsing.POCOs;
using DSharpPlus;
using DSharpPlus.Entities;
using IrdLibraryClient.IrdFormat;

namespace CompatBot.Utils.ResultFormatters
{
    internal static partial class LogParserResult
    {
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
            var serial = items["serial"] ?? "";
            if (items["fatal_error"] is string fatalError)
            {
                var context = items["fatal_error_context"] ?? "";
                builder.AddField("Fatal Error", $"```{fatalError.Trim(1022)}```");
                if (fatalError.Contains("psf.cpp") || fatalError.Contains("invalid map<K, T>") || context.Contains("SaveData"))
                    notes.Add("⚠ Game save data might be corrupted");
                else if (fatalError.Contains("Could not bind OpenGL context"))
                    notes.Add("❌ GPU or installed GPU drivers do not support OpenGL 4.3");
                else if (fatalError.Contains("file is null"))
                {
                    if (context.StartsWith("RSX", StringComparison.InvariantCultureIgnoreCase) || fatalError.StartsWith("RSX:"))
                        notes.Add("❌ Shader cache might be corrupted; right-click on the game, then `Remove` → `Shader Cache`");
                    if (context.StartsWith("SPU", StringComparison.InvariantCultureIgnoreCase))
                        notes.Add("❌ SPU cache might be corrupted; right-click on the game, then `Remove` → `SPU Cache`");
                    if (context.StartsWith("PPU", StringComparison.InvariantCultureIgnoreCase))
                        notes.Add("❌ PPU cache might be corrupted; right-click on the game, then `Remove` → `PPU Cache`");
                }
            }

            if (items["failed_to_decrypt"] is string _)
                notes.Add("❌ Failed to decrypt game content, license file might be corrupted");
            if (items["failed_to_boot"] is string _)
                notes.Add("❌ Failed to boot the game, the dump might be encrypted or corrupted");
            if (items["failed_to_verify"] is string verifyFails)
            {
                var types = verifyFails.Split(Environment.NewLine).Distinct().ToList();
                if (types.Contains("sce"))
                    notes.Add("❌ Failed to decrypt executables from DLC, PPU recompilers may fail");
            }
            if (brokenDump)
                notes.Add("❌ Some game files are missing or corrupted, please re-dump and validate.");
            else if (irdChecked)
                notes.Add("✅ Checked missing files against IRD");
            if (items["fw_version_installed"] is string fw && !string.IsNullOrEmpty(fw))
            {
                if (Version.TryParse(fw, out var fwv))
                {
                    if (fwv < MinimumFirmwareVersion)
                        notes.Add($"⚠ Firmware version {MinimumFirmwareVersion} or later is recommended");
                }
                else
                    notes.Add("⚠ Custom firmware is not supported, please use the latest official one");
            }

            if (!string.IsNullOrEmpty(items["host_root_in_boot"]) && isEboot)
                notes.Add("❌ Retail game booted as an ELF through the `/root_host/`, probably due to passing path as an argument; please boot through the game library list for now");
            if (serial.StartsWith("NP") && items["ldr_game_serial"] != serial && items["ldr_path_serial"] != serial)
                notes.Add("❌ Digital version of the game outside of `/dev_hdd0/game/` directory");
            if (!string.IsNullOrEmpty(items["serial"]) && isElf)
                notes.Add($"⚠ Retail game booted directly through `{Path.GetFileName(elfBootPath)}`, which is not recommended");
            if (items["log_from_ui"] is string _)
                notes.Add("ℹ The log is a copy from UI, please upload the full file created by RPCS3");
            else if (string.IsNullOrEmpty(items["ppu_decoder"]) || string.IsNullOrEmpty(items["renderer"]))
            {
                notes.Add("ℹ The log is empty");
                notes.Add("ℹ Please boot the game and upload a new log");
            }
            else if (string.IsNullOrEmpty(items["serial"] + items["game_title"])
                     && !string.IsNullOrEmpty(items["fw_installed_message"])
                     && items["fw_version_installed"] is string fwVersion)
            {
                notes.Add($"ℹ The log contains only installation of firmware {fwVersion}");
                notes.Add("ℹ Please boot the game and upload a new log");
            }

            if (int.TryParse(items["thread_count"], out var threadCount) && threadCount < 4)
                notes.Add($"⚠ This CPU only has {threadCount} hardware thread{(threadCount == 1 ? "" : "s")} enabled");

            if (items["cpu_model"] is string cpu)
            {
                if (cpu.StartsWith("AMD"))
                {
                    if (cpu.Contains("Ryzen"))
                    {
                        if (threadCount < 12)
                            notes.Add("⚠ Six cores or more is recommended for Ryzen CPUs");
                        if (items["os_path"] != "Linux"
                            && items["thread_scheduler"] == DisabledMark)
                            notes.Add("⚠ Please enable `Thread scheduler` option in the CPU Settings");
                    }
                    else
                        notes.Add("⚠ AMD CPUs before Ryzen are too weak for PS3 emulation");
                }

                if (cpu.StartsWith("Intel"))
                {
                    if (!items["cpu_extensions"].Contains("TSX")
                        && (cpu.Contains("Core2")
                            || cpu.Contains("Celeron")
                            || cpu.Contains("Atom")
                            || cpu.Contains("Pentium")
                            || cpu.EndsWith('U')
                            || cpu.EndsWith('M')
                            || cpu.Contains('Y')
                            || ((cpu.EndsWith("HQ") || cpu.EndsWith('H'))
                                && threadCount < 8)))
                        notes.Add("⚠ This CPU is too old and/or too weak for PS3 emulation");
                }
            }

            var supportedGpu = true;
            Version oglVersion = null;
            if (items["opengl_version"] is string oglVersionString)
                Version.TryParse(oglVersionString, out oglVersion);
            if (items["glsl_version"] is string glslVersionString &&
                Version.TryParse(glslVersionString, out var glslVersion))
            {
                glslVersion = new Version(glslVersion.Major, glslVersion.Minor / 10);
                if (oglVersion == null || glslVersion > oglVersion)
                    oglVersion = glslVersion;
            }

            if (oglVersion != null)
            {
                if (oglVersion < MinimumOpenGLVersion)
                {
                    notes.Add($"❌ GPU only supports OpenGL {oglVersion.Major}.{oglVersion.Minor}, which is below the minimum requirement of {MinimumOpenGLVersion}");
                    supportedGpu = false;
                }
            }

            var gpuInfo = items["gpu_info"] ?? items["discrete_gpu_info"];
            if (supportedGpu && !string.IsNullOrEmpty(gpuInfo))
            {
                if (IntelGpuModel.Match(gpuInfo) is Match intelMatch
                    && intelMatch.Success)
                {
                    var modelNumber = intelMatch.Groups["gpu_model_number"].Value;
                    if (!string.IsNullOrEmpty(modelNumber) && modelNumber.StartsWith('P'))
                        modelNumber = modelNumber.Substring(1);
                    int.TryParse(modelNumber, out var modelNumberInt);
                    if (modelNumberInt < 500 || modelNumberInt > 1000)
                    {
                        notes.Add("⚠ Intel iGPUs before Skylake do not fully comply with OpenGL 4.3");
                        supportedGpu = false;
                    }
                    else
                        notes.Add("⚠ Intel iGPUs are not officially supported; visual glitches are to be expected");
                }

                if (items["os_path"] is string os
                    && os != "Linux"
                    && IsNvidia(gpuInfo)
                    && items["driver_version_info"] is string driverVersionString
                    && Version.TryParse(driverVersionString, out var driverVersion))
                {
                    if (driverVersion < NvidiaRecommendedOldWindowsVersion)
                        notes.Add($"❗ Please update your nVidia driver to at least version {NvidiaRecommendedOldWindowsVersion}");
                    if (driverVersion >= NvidiaFullscreenBugMinVersion
                        && driverVersion < NvidiaFullscreenBugMaxVersion
                        && items["renderer"] == "Vulkan")
                        notes.Add("ℹ **400 series** nVidia drivers can cause random screen freezes when playing in **fullscreen** using **Vulkan** renderer on the **first monitor**");
                }
            }

            if (!string.IsNullOrEmpty(items["shader_compile_error"]))
            {
                if (supportedGpu)
                    notes.Add("❌ Shader compilation error might indicate shader cache corruption");
                else
                    notes.Add("❌ Shader compilation error on unsupported GPU");
            }

            var ppuPatches = GetPatches(items["ppu_hash"], items["ppu_hash_patch"]);
            var spuPatches = GetPatches(items["spu_hash"], items["spu_hash_patch"]);
            if (ppuPatches.Any() || spuPatches.Any())
                notes.Add($"ℹ Game-specific patches were applied (PPU: {ppuPatches.Count}, SPU: {spuPatches.Count})");
            if (P5Ids.Contains(serial))
            {
                /*
                 * mod support = 27
                 * log access  = 39
                 * intro skip  = 1
                 * 60 fps v1   = 12
                 * 60 fps v2   = 268
                 * disable hud = 10
                 * random music= 19
                 * disable blur= 8
                 * distortion  = 8
                 * 100% dist   = 8
                 */
                if (ppuPatches.Values.Any(n => n > 260 || n == 27+12 || n == 12))
                    notes.Add("ℹ 60 fps patch is enabled; please disable if you have any strange issues");
                if (ppuPatches.Values.Any(n => n == 12 || n == 12+27))
                    notes.Add("⚠ An old version of the 60 fps patch is used");
            }

            if (KnownDisableVertexCacheIds.Contains(serial))
            {
                if (items["vertex_cache"] == DisabledMark)
                    notes.Add("⚠ This game requires disabling `Vertex Cache` in the GPU tab of the Settings");
            }

            bool discInsideGame = false;
            bool discAsPkg = false;
            var pirateEmoji = discordClient.GetEmoji(":piratethink:", DiscordEmoji.FromUnicode("🔨"));
            //var thonkEmoji = discordClient.GetEmoji(":thonkang:", DiscordEmoji.FromUnicode("🤔"));
			// this is a common scenario now that Mega did the version merge from param.sfo
/*
            if (items["game_category"] == "GD")
                notes.Add($"❔ Game was booted through the Game Data");
*/
            if (items["game_category"] == "DG" || items["game_category"] == "GD") // only disc games should install game data
            {
                discInsideGame |= !string.IsNullOrEmpty(items["ldr_disc"]) && !(items["serial"]?.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase) ?? false);
                discAsPkg |= items["serial"]?.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase) ?? false;
                discAsPkg |= items["ldr_game_serial"] is string ldrGameSerial && ldrGameSerial.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase);
            }

            discAsPkg |= items["game_category"] == "HG" && !(items["serial"]?.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase) ?? false);
            if (discInsideGame)
                notes.Add($"❌ Disc game inside `{items["ldr_disc"]}`");
            if (discAsPkg)
                notes.Add($"{pirateEmoji} Disc game installed as a PKG ");

            if (!string.IsNullOrEmpty(items["native_ui_input"]))
                notes.Add("⚠ Pad initialization problem detected; try disabling `Native UI`");
            if (!string.IsNullOrEmpty(items["xaudio_init_error"]))
                notes.Add("❌ XAudio initialization failed; make sure you have audio output device working");

            if (!string.IsNullOrEmpty(items["fw_missing_msg"])
                || !string.IsNullOrEmpty(items["fw_missing_something"]))
                notes.Add("❌ PS3 firmware is missing or corrupted");

            var updateInfo = await CheckForUpdateAsync(items).ConfigureAwait(false);
            var buildBranch = items["build_branch"]?.ToLowerInvariant();
            if (updateInfo != null
                && (buildBranch == "head"
                    || buildBranch == "spu_perf"
                    || string.IsNullOrEmpty(buildBranch) && updateInfo.CurrentBuild != null))
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
                if (buildBranch == "spu_perf")
                    notes.Add($"ℹ `{buildBranch}` build is obsolete, current master build offers at least the same level of performance and includes many additional improvements");
            }

            if (state.Error == LogParseState.ErrorCode.SizeLimit)
                notes.Add("ℹ The log was too large, so only the last processed run is shown");

            var notesContent = new StringBuilder();
            foreach (var line in SortLines(notes, pirateEmoji))
                notesContent.AppendLine(line);
            PageSection(builder, notesContent.ToString().Trim(), "Notes");
        }

        private static void BuildMissingLicensesSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            if (items["rap_file"] is string rap)
            {
                var limitTo = 5;
                var licenseNames = rap.Split(Environment.NewLine)
                    .Distinct()
                    .Select(Path.GetFileName)
                    .Distinct()
                    .Except(KnownBogusLicenses)
                    .Select(p => $"`{p}`")
                    .ToList();
                if (licenseNames.Count == 0)
                    return;

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
            var missingDirs = items["broken_directory"]?.Split(Environment.NewLine).Distinct().ToList() ??
                              new List<string>(0);
            var missingFiles = items["broken_filename"]?.Split(Environment.NewLine).Distinct().ToList() ??
                               new List<string>(0);
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

            var broken = missingFiles.Where(knownFiles.Contains).ToList();
            if (broken.Count > 0)
            {
                Config.Log.Debug("List of broken files according to IRD:");
                foreach (var f in broken)
                    Config.Log.Debug(f);
                return (true, true);
            }

            var knownDirs = new HashSet<string>(knownFiles.Select(f => Path.GetDirectoryName(f).Replace('\\', '/')),
                StringComparer.InvariantCultureIgnoreCase);
            var brokenDirs = missingDirs.Where(knownDirs.Contains).ToList();
            if (brokenDirs.Count > 0)
            {
                Config.Log.Debug("List of broken directories according to IRD:");
                foreach (var d in broken)
                    Config.Log.Debug(d);
                return (true, true);
            }
            return (true, false);
        }
    }
}