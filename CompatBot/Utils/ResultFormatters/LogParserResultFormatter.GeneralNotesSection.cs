using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient.Utils;
using CompatBot.Database;
using CompatBot.EventHandlers;
using CompatBot.EventHandlers.LogParsing.POCOs;
using DSharpPlus;
using DSharpPlus.Entities;
using IrdLibraryClient.IrdFormat;

namespace CompatBot.Utils.ResultFormatters
{
    internal static partial class LogParserResult
    {
        private static readonly Version DecompilerIssueStartVersion = new(0, 0, 9, 10307);
        private static readonly Version DecompilerIssueEndVersion = new(0, 0, 10, 10346);

        private static async Task BuildNotesSectionAsync(DiscordEmbedBuilder builder, LogParseState state, DiscordClient discordClient)
        {
            var items = state.CompletedCollection!;
            var multiItems = state.CompleteMultiValueCollection!;
            var notes = new List<string>();
            var (_, brokenDump, longestPath) = await HasBrokenFilesAsync(state).ConfigureAwait(false);
            brokenDump |= multiItems["edat_block_offset"].Any();
            var supportedGpu = string.IsNullOrEmpty(items["rsx_unsupported_gpu"]);
            var unsupportedGpuDriver = false;
            var elfBootPath = items["elf_boot_path"] ?? "";
            var isEboot = !string.IsNullOrEmpty(elfBootPath) && elfBootPath.EndsWith("EBOOT.BIN", StringComparison.InvariantCultureIgnoreCase);
            var isElf = !string.IsNullOrEmpty(elfBootPath) && !elfBootPath.EndsWith("EBOOT.BIN", StringComparison.InvariantCultureIgnoreCase);
            var serial = items["serial"] ?? "";
            if (multiItems["fatal_error"] is UniqueList<string> fatalErrors && fatalErrors.Any())
            {
                var contexts = multiItems["fatal_error_context"];
                var reducedFatalErrors = GroupSimilar(fatalErrors);
                foreach (var (fatalError, count, similarity) in reducedFatalErrors)
                {
                    var knownFatal = false;
                    if (fatalError.Contains("psf.cpp", StringComparison.InvariantCultureIgnoreCase)
                        || fatalError.Contains("invalid map<K, T>", StringComparison.InvariantCultureIgnoreCase)
                        || contexts.Any(c => c.Contains("SaveData", StringComparison.InvariantCultureIgnoreCase)))
                    {
                        knownFatal = true;
                        notes.Add("❌ Game save data is corrupted");
                    }
                    else if (fatalError.Contains("Could not bind OpenGL context"))
                    {
                        knownFatal = true;
                        notes.Add("❌ GPU or installed GPU drivers do not support OpenGL 4.3");
                    }
                    else if (fatalError.Contains("file is null"))
                    {
                        if (contexts.Any(c => c.StartsWith("RSX")))
                        {
                            knownFatal = true;
                            notes.Add("❌ Shader cache might be corrupted; right-click on the game, then `Remove` → `Shader Cache`");
                        }
                        if (contexts.Any(c => c.StartsWith("SPU")))
                        {
                            knownFatal = true;
                            notes.Add("❌ SPU cache might be corrupted; right-click on the game, then `Remove` → `SPU Cache`");
                        }
                        if (contexts.Any(c => c.StartsWith("PPU")))
                        {
                            knownFatal = true;
                            notes.Add("❌ PPU cache might be corrupted; right-click on the game, then `Remove` → `PPU Cache`");
                        }
                    }
                    else if (fatalError.Contains("Null function") && fatalError.Contains("JIT"))
                    {
                        if (contexts.Any(c => c.StartsWith("PPU")))
                        {
                            knownFatal = true;
                            notes.Add("❌ PPU cache has issues; right-click on the game, then `Remove` → `PPU Cache`");
                        }
                        if (contexts.Any(c => c.StartsWith("SPU")))
                        {
                            knownFatal = true;
                            notes.Add("❌ SPU cache has issues; right-click on the game, then `Remove` → `SPU Cache`");
                        }
                    }
                    else if (fatalError.Contains("no matching overloaded function found"))
                    {
                        if (fatalError.Contains("'mov'"))
                        {
                            knownFatal = true;
                            unsupportedGpuDriver = true;
                        }
                    }
                    else if (fatalError.Contains("RSX Decompiler Thread"))
                    {
                        if (items["build_branch"]?.ToLowerInvariant() == "head"
                            && Version.TryParse(items["build_full_version"], out var v)
                            && v >= DecompilerIssueStartVersion
                            && v < DecompilerIssueEndVersion)
                        {
                            knownFatal = true;
                            notes.Add("❌ This RPCS3 build has a known regression, please update to the latest version");
                        }
                    }
                    else if (fatalError.Contains("graphics-hook64.dll"))
                    {
                        knownFatal = true;
                        notes.Add("❌ Please update or uninstall OBS to prevent crashes");
                    }
                    else if (fatalError.Contains("bdcamvk64.dll"))
                    {
                        knownFatal = true;
                        notes.Add("❌ Please update or uninstall Bandicam to prevent crashes");
                    }
                    else if (fatalError.Contains("(e=0x17): file::read"))
                    {
                        // on windows this is ERROR_CRC
                        notes.Add("❌ Storage device communication error; check your cables");
                    }
                    else if (fatalError.Contains("Unknown primitive type"))
                    {
                        notes.Add("⚠ RSX desync detected, it's probably random");
                    }
                    if (!knownFatal)
                    {
                        var sectionName = count == 1
                            ? "Fatal Error"
#if DEBUG
                            : $"Fatal Error (x{count}) [{similarity*100:0.00}%+]";
#else
                            : $"Fatal Error (x{count})";
#endif
                        builder.AddField(sectionName, $"```\n{fatalError.Trim(EmbedPager.MaxFieldLength - 8)}\n```");
                    }
                }
            }
            else if (items["unimplemented_syscall"] is string unimplementedSyscall)
            {
                if (unimplementedSyscall.Contains("syscall_988"))
                {
                    var fatalError = "Unimplemented syscall " + unimplementedSyscall;
                    builder.AddField("Fatal Error", $"```\n{fatalError.Trim(EmbedPager.MaxFieldLength - 8)}\n```");
                    if (items["ppu_decoder"] is string ppuDecoder && ppuDecoder.Contains("Recompiler") && !Config.Colors.CompatStatusPlayable.Equals(builder.Color.Value))
                        notes.Add("⚠ PPU desync detected; check your save data for corruption and/or try PPU Interpreter");
                    else
                        notes.Add("⚠ PPU desync detected, most likely cause is corrupted save data");
                }
            }

            if (Config.Colors.CompatStatusNothing.Equals(builder.Color.Value) || Config.Colors.CompatStatusLoadable.Equals(builder.Color.Value))
                notes.Add("❌ This game doesn't work on the emulator yet");
            if (items["failed_to_decrypt"] != null)
                notes.Add("❌ Failed to decrypt game content, license file might be corrupted");
            if (items["failed_to_boot"] != null)
                notes.Add("❌ Failed to boot the game, the dump might be encrypted or corrupted");
            if (multiItems["failed_to_verify"].Contains("sce"))
                notes.Add("❌ Failed to decrypt executables, PPU recompiler may crash or fail");
            if (items["disc_to_psn_serial"] is string badSerial)
                notes.Add("❌ This version of the game does not work on the emulator at this time");
            else if (items["game_status"] is string gameStatus
                     && Enum.TryParse(gameStatus, true, out CompatStatus status)
                     && status < CompatStatus.Ingame)
                notes.Add("❌ This game title does not work on the emulator at this time");
            if (brokenDump)
                notes.Add("❌ Some game files are missing or corrupted, please re-dump and validate.");
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

            if (items["os_type"] == "Windows")
            {
                var knownPaths = new[]
                {
                    items["win_path"],
                    items["ldr_game_full"],
                    items["ldr_disc_full"],
                    items["ldr_path_full"],
                    items["ldr_boot_path_full"],
                    items["elf_boot_path_full"],
                }.Where(s => !string.IsNullOrEmpty(s));
                const int maxPath = 260;
                const int maxFolderPath = 260 - 1 - 8 - 3;
                foreach (var p in knownPaths)
                {
                    if (p!.Length > maxPath)
                    {
                        notes.Add($"⚠ Some file paths are longer than {maxPath} characters");
                        break;
                    }
                    else
                    {
                        var baseDir = Path.GetDirectoryName(p) ?? p;
                        if (baseDir.Length > maxFolderPath)
                        {
                            notes.Add($"⚠ Some folder paths are longer than {maxFolderPath} characters");
                            break;
                        }
                        else if (baseDir.Length + longestPath > maxPath)
                        {
                            notes.Add($"⚠ Some file paths are potentially longer than {maxPath} characters");
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(items["host_root_in_boot"]) && isEboot)
                notes.Add("❌ Retail game booted as an ELF through the `/root_host/`, probably due to passing path as an argument; please boot through the game library list for now");
            var path = items["ldr_game"] ?? items["ldr_path"] ?? items["ldr_boot_path"] ?? items["elf_boot_path"];
            if (!string.IsNullOrEmpty(path)
                && serial.StartsWith("NP")
                && items["ldr_game_serial"] != serial
                && items["ldr_path_serial"] != serial
                && items["ldr_boot_path_serial"] != serial
                && items["elf_boot_path_serial"] != serial)
                notes.Add("❌ Digital version of the game outside of `/dev_hdd0/game/` directory");
            // LDR: Path: before settings is unreliable, because you can boot through installed patch or game data
            if (!string.IsNullOrEmpty(items["ldr_disc"])
                && serial.StartsWith("BL")
                && !string.IsNullOrEmpty(items["ldr_disc_serial"]))
                notes.Add("❌ Disc version of the game inside the `/dev_hdd0/game/` directory");
            if (!string.IsNullOrEmpty(serial) && isElf)
                notes.Add($"⚠ Retail game booted directly through `{Path.GetFileName(elfBootPath)}`, which is not recommended");

            if (items["log_from_ui"] is string _)
                notes.Add("ℹ The log is a copy from UI, please upload the full file created by RPCS3");
            else if (string.IsNullOrEmpty(items["ppu_decoder"]) || string.IsNullOrEmpty(items["renderer"]))
            {
                notes.Add("ℹ The log is empty");
                notes.Add("ℹ Please boot the game and upload a new log");
            }
            else if (string.IsNullOrEmpty(serial + items["game_title"])
                     && !string.IsNullOrEmpty(items["fw_installed_message"])
                     && items["fw_version_installed"] is string fwVersion)
            {
                notes.Add($"ℹ The log contains only installation of firmware {fwVersion}");
                notes.Add("ℹ Please boot the game and upload a new log");
            }

            var category = items["game_category"];
            if (category == "PE"
                || category == "PP"
                || serial.StartsWith('U') && ProductCodeLookup.ProductCode.IsMatch(serial))
            {
                builder.Color = Config.Colors.CompatStatusNothing;
                notes.Add("❌ PSP software is not supported");
            }
            else if (category == "MN")
            {
                builder.Color = Config.Colors.CompatStatusNothing;
                notes.Add("❌ Minis are not supported");
            }
            if (category == "2G" || category == "2P" || category == "2D")
            {
                builder.Color = Config.Colors.CompatStatusNothing;
                notes.Add("❌ PS2 software is not supported");
            }

            if (items["compat_database_path"] is string compatDbPath)
            {
                if (InstallPath.Match(compatDbPath.Replace('\\', '/').Replace("//", "/").Trim()) is Match installPathMatch
                    && installPathMatch.Success)
                {
                    var rpcs3FolderMissing = string.IsNullOrEmpty(installPathMatch.Groups["rpcs3_folder"].Value);
                    var desktop = !string.IsNullOrEmpty(installPathMatch.Groups["desktop"].Value);
                    var programFiles = !string.IsNullOrEmpty(installPathMatch.Groups["program_files"].Value);
                    if (rpcs3FolderMissing)
                    {
                        if (desktop)
                            notes.Add("ℹ RPCS3 installed directly on desktop, without folder");
                        else if (programFiles)
                            notes.Add("⚠ RPCS3 installed directly inside Program Files, without folder");
                        else
                            notes.Add("⚠ RPCS3 installed in the drive root, please create a folder and move all files inside");
                    }

                    if (programFiles)
                        notes.Add("⚠ Program Files have special permissions, please move RPCS3 to another location");
                }

                var pathSegments = PathUtils.GetSegments(compatDbPath);
                var syncFolder = pathSegments.FirstOrDefault(s => KnownSyncFolders.Contains(s) || s.EndsWith("sync", StringComparison.InvariantCultureIgnoreCase));
                if (!string.IsNullOrEmpty(syncFolder))
                    notes.Add($"⚠ RPCS3 is installed in a file sync service folder `{syncFolder}`; may result in data loss or inconsistent state");
                var rar = pathSegments.FirstOrDefault(s => s.StartsWith("Rar$"));
                if (!string.IsNullOrEmpty(rar))
                    notes.Add("❌ RPCS3 is launched from WinRAR; please extract all files instead");
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
                        if (cpu.EndsWith('U')
                            || cpu.EndsWith('H')
                            || cpu.EndsWith("HS"))
                            notes.Add("⚠ Mobile Ryzen CPUs are only recommended for lighter games.");
                    }
                    else
                        notes.Add("⚠ AMD CPUs before Ryzen are too weak for PS3 emulation");
                }

                if (cpu.StartsWith("Intel") || cpu.StartsWith("Pentium"))
                {
                    if (items["cpu_extensions"]?.Contains("TSX") is not true
                        && (cpu.Contains("Core2")
                            || cpu.Contains("Celeron")
                            || cpu.Contains("Atom")
                            || cpu.Contains("Pentium")
                            || cpu.EndsWith('U')
                            || cpu.EndsWith('M')
                            || cpu.Contains('Y')
                            || cpu[^2] == 'G'
                            || threadCount < 6))
                        notes.Add("⚠ This CPU is too weak and/or too old for PS3 emulation");
                }
            }

            if (items["memory_amount"] is string ramSizeStr
                && double.TryParse(ramSizeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var ramSize)
                && ramSize < 6)
                notes.Add("⚠ 8 GB RAM or more is recommended for PS3 emulation");

            Version? oglVersion = null;
            if (items["opengl_version"] is string oglVersionString)
                _ = Version.TryParse(oglVersionString, out oglVersion);
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
                if (IntelGpuModel.Match(gpuInfo) is Match intelMatch && intelMatch.Success)
                {
                    var family = intelMatch.Groups["gpu_family"].Value.TrimEnd();
                    var modelNumber = intelMatch.Groups["gpu_model_number"].Value;
                    if (!string.IsNullOrEmpty(modelNumber) && modelNumber.StartsWith('P'))
                        modelNumber = modelNumber[1..];
                    _ = int.TryParse(modelNumber, out var modelNumberInt);
                    if (family == "UHD" || family == "Iris Plus" || modelNumberInt > 500 && modelNumberInt < 1000)
                        notes.Add("⚠ Intel iGPUs are not officially supported; visual glitches are to be expected");
                    else
                    {
                        notes.Add("⚠ Intel iGPUs before Skylake do not fully comply with OpenGL 4.3");
                        supportedGpu = false;
                    }
                }

                if (items["driver_version_info"] is string driverVersionString)
                {
                    if (driverVersionString.Contains('-'))
                        driverVersionString = driverVersionString.Split(new[] {' ', '-'}, StringSplitOptions.RemoveEmptyEntries).Last();
                    if (Version.TryParse(driverVersionString, out var driverVersion)
                        && Version.TryParse(items["build_full_version"], out var buildVersion))
                    {
                        if (IsNvidia(gpuInfo))
                        {
                            var isWindows = items["os_type"] is string os && os != "Linux";
                            var minVersion = isWindows ? NvidiaRecommendedWindowsVersion : NvidiaRecommendedLinuxVersion;
                            if (driverVersion < minVersion)
                                notes.Add($"❗ Please update your nVidia GPU driver to at least version {minVersion}");
                            if (isWindows
                                && buildVersion < NvidiaFullscreenBugFixed
                                && items["build_branch"] == "HEAD")
                            {
                                if (driverVersion >= NvidiaFullscreenBugMinVersion
                                    && driverVersion < NvidiaFullscreenBugMaxVersion
                                    && items["renderer"] == "Vulkan")
                                    notes.Add("ℹ 400 series nVidia drivers can cause screen freezes, please update RPCS3");
                            }
                        }
                        else if (IsAmd(gpuInfo) && items["os_type"] == "Windows")
                        {
                            if (driverVersion < AmdRecommendedOldWindowsVersion)
                                notes.Add($"❗ Please update your AMD GPU driver to at least version {AmdRecommendedOldWindowsVersion}");
                        }
                    }
                    else if (driverVersionString.Contains("older than", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (IsAmd(gpuInfo))
                            notes.Add($"❗ Please update your AMD GPU driver to version {AmdLastGoodOpenGLWindowsVersion} or newer");
                    }
                }
            }
            items["supported_gpu"] = supportedGpu ? EnabledMark : DisabledMark;

            if (!string.IsNullOrEmpty(items["shader_compile_error"]))
            {
                if (unsupportedGpuDriver)
                    notes.Add("❌ Shader compilation error on unsupported GPU driver");
                else if (supportedGpu)
                    notes.Add("❌ Shader compilation error might indicate shader cache corruption");
                else
                    notes.Add("❌ Shader compilation error on unsupported GPU");
            }

            if (!string.IsNullOrEmpty(items["enqueue_buffer_error"])
                && state.ValueHitStats.TryGetValue("enqueue_buffer_error", out var enqueueBufferErrorCount)
                && enqueueBufferErrorCount > 100)
            {
                if (items["os_type"] == "Windows")
                    notes.Add("⚠ Audio backend issues detected; it could be caused by a bad driver or 3rd party software");
                else
                    notes.Add("⚠ Audio backend issues detected; check for high audio driver/sink latency");
            }

            if (!string.IsNullOrEmpty(items["patch_error_file"])) 
                notes.Add($"⚠ Failed to load `patch.yml`, check syntax around line {items["patch_error_line"]} column {items["patch_error_column"]}");

            var ppuPatches = GetPatches(multiItems["ppu_patch"], true);
            var ovlPatches = GetPatches(multiItems["ovl_patch"], true);
            var allSpuPatches = GetPatches(multiItems["spu_patch"], false);
            var spuPatches = new Dictionary<string, int>(allSpuPatches.Where(kvp => kvp.Value != 0));
            if (ppuPatches.Any() || spuPatches.Any() || ovlPatches.Any())
            {
                var patchCount = "";
                if (ppuPatches.Count != 0)
                    patchCount += "PPU: " + string.Join('/', ppuPatches.Values) + ", ";
                if (ovlPatches.Count != 0)
                    patchCount += "OVL: " + string.Join('/', ovlPatches.Values) + ", ";
                if (spuPatches.Count != 0)
                    patchCount += "SPU: " + string.Join('/', spuPatches.Values);
                notes.Add($"ℹ Game-specific patches were applied ({patchCount.TrimEnd(',', ' ')})");
            }
            var mlaaHashes = KnownMlaaSpuHashes.Intersect(allSpuPatches.Keys).ToList();
            if (mlaaHashes.Count != 0)
            {
                if (mlaaHashes.Any(h => allSpuPatches[h] != 0))
                    notes.Add("ℹ MLAA patch was applied");
                else
                    notes.Add("ℹ This game has MLAA disable patch, see [Game Patches](https://wiki.rpcs3.net/index.php?title=Help:Game_Patches#Disable_SPU_MLAA)");
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
            if (category == "DG" || category == "GD") // only disc games should install game data
            {
                discInsideGame |= !string.IsNullOrEmpty(items["ldr_disc"]) && !(items["serial"]?.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase) ?? false);
                discAsPkg |= items["serial"]?.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase) ?? false;
                discAsPkg |= items["ldr_game_serial"] is string ldrGameSerial && ldrGameSerial.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase);
            }

            discAsPkg |= category == "HG" && !(items["serial"]?.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase) ?? false);
            if (discInsideGame)
                notes.Add($"❌ Disc game inside `{items["ldr_disc"]}`");
            if (discAsPkg)
                notes.Add($"{pirateEmoji} Disc game installed as a PKG ");

            if (!string.IsNullOrEmpty(items["native_ui_input"]))
                notes.Add("⚠ Pad initialization problem detected; try disabling `Native UI`");
            if (!string.IsNullOrEmpty(items["xaudio_init_error"]))
                notes.Add("❌ XAudio initialization failed; make sure you have a working audio output device");
            else if (items["audio_backend_init_error"] is string audioBackend) 
                notes.Add($"⚠ {audioBackend} initialization failed; make sure you have a working audio output device");

            if (!string.IsNullOrEmpty(items["fw_missing_msg"])
                || !string.IsNullOrEmpty(items["fw_missing_something"]))
                notes.Add("❌ PS3 firmware is missing or corrupted");

            if (items["game_mod"] is string mod)
                notes.Add($"ℹ Game files modification present: `{mod.Trim(10)}`");

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
                    notes.Add($"😱 `{buildBranch}` build is obsolete, current master build offers at least the same level of performance and includes many additional improvements");
            }

            if (DesIds.Contains(serial))
                notes.Add("ℹ If you experience infinite load screen, clear game cache via `File` → `All games` → `Remove Disk Cache`");

            if (items["game_version"] is string gameVer)
            {
                var msg = "ℹ Game version: v" + gameVer;
                if (items["game_update_version"] is string gameUpVer
                    && Version.TryParse(gameVer, out var gv)
                    && Version.TryParse(gameUpVer, out var guv)
                    && guv > gv)
                    msg += $" (update available: v{gameUpVer})";
                notes.Add(msg);
            }
            if (multiItems["ppu_patch"].FirstOrDefault() is string firstPpuPatch
                && ProgramHashPatch.Match(firstPpuPatch) is Match m
                && m.Success
                && m.Groups["hash"].Value is string firstPpuHash)
            {
                var exe = Path.GetFileName(items["elf_boot_path"] ?? "");
                if (string.IsNullOrEmpty(exe) || exe.Equals("EBOOT.BIN", StringComparison.InvariantCultureIgnoreCase))
                    exe = "Main";
                else
                    exe = $"`{exe}`";
                notes.Add($"ℹ {exe} hash: `PPU-{firstPpuHash}`");
            }

            if (state.Error == LogParseState.ErrorCode.SizeLimit)
                notes.Add("ℹ The log was too large, so only the last processed run is shown");
            if (state.Error == LogParseState.ErrorCode.UnknownError)
                notes.Add("ℹ There was an error during log processing");

            BuildWeirdSettingsSection(builder, state, notes);
            BuildMissingLicensesSection(builder, serial, multiItems, notes);
            BuildAppliedPatchesSection(builder, multiItems);

            var notesContent = new StringBuilder();
            foreach (var line in SortLines(notes, pirateEmoji).Distinct())
                notesContent.AppendLine(line);
            PageSection(builder, notesContent.ToString().Trim(), "Notes");
        }

        private static void BuildAppliedPatchesSection(DiscordEmbedBuilder builder, NameUniqueObjectCollection<string> items)
        {
            var patchNames = items["patch_desc"];
            if (patchNames.Any())
                builder.AddField("Applied Game Patch" + (patchNames.Length == 1 ? "" : "es"), string.Join(", ", patchNames));
        }
        private static void BuildMissingLicensesSection(DiscordEmbedBuilder builder, string serial, NameUniqueObjectCollection<string> items, List<string> generalNotes)
        {
            if (items["rap_file"] is UniqueList<string> raps && raps.Any())
            {
                var limitTo = 5;
                List<string> licenseNames = raps
                    .Select(Path.GetFileName)
                    .Where(l => !string.IsNullOrEmpty(l))
                    .Distinct()
                    .Except(KnownBogusLicenses)
                    .OrderBy(l => l)
                    .ToList()!;
                var formattedLicenseNames = licenseNames
                    .Select(p => $"{StringUtils.InvisibleSpacer}`{p}`")
                    .ToList();
                if (formattedLicenseNames.Count == 0)
                    return;

                string content;
                if (formattedLicenseNames.Count > limitTo)
                {
                    content = string.Join(Environment.NewLine, formattedLicenseNames.Take(limitTo - 1));
                    var other = formattedLicenseNames.Count - limitTo + 1;
                    content += $"{Environment.NewLine}and {other} other license{StringUtils.GetSuffix(other)}";
                }
                else
                    content = string.Join(Environment.NewLine, formattedLicenseNames);

                builder.AddField("Missing Licenses", content);

                var gameRegion = serial.Length > 3 ? new[] {serial[2]} : Enumerable.Empty<char>();
                var dlcRegions = licenseNames
                    .Where(l => l.Length > 9)
                    .Select(n => n[9])
                    .Concat(gameRegion)
                    .Distinct()
                    .ToArray();
                if (dlcRegions.Length > 1)
                    generalNotes.Add($"🤔 That is a very interesting DLC collection from {dlcRegions.Length} different regions");
                if (KnownCustomLicenses.Overlaps(licenseNames))
                    generalNotes.Add("🤔 That is a very interesting license you're missing");
                generalNotes.Add("⚠ DLC without a license is useless and may lead to game crash in some cases");
            }
        }

        private static async Task<(bool irdChecked, bool broken, int longestPath)> HasBrokenFilesAsync(LogParseState state)
        {
            var items = state.CompletedCollection!;
            var multiItems = state.CompleteMultiValueCollection!;
            var defaultLongestPath = "/PS3_GAME/USRDIR/".Length + (1+8+3)*2; // usually there's at least one more level for data files
            if (items["serial"] is not string productCode)
                return (false, false, defaultLongestPath);

            if (!productCode.StartsWith("B") && !productCode.StartsWith("M"))
            {
                if (P5Ids.Contains(productCode)
                    && multiItems["broken_digital_filename"] is UniqueList<string> brokenDigitalFiles
                    && brokenDigitalFiles.Any())
                {
                    if (brokenDigitalFiles.Contains("USRDIR/ps3.cpk") || brokenDigitalFiles.Contains("USRDIR/data.cpk"))
                        return (false, true, defaultLongestPath);
                }
                return (false, false, defaultLongestPath);
            }

            HashSet<string> knownFiles;
            try
            {
                var irdFiles = await IrdClient.DownloadAsync(productCode, Config.IrdCachePath, Config.Cts.Token).ConfigureAwait(false);
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
                return (false, false, defaultLongestPath);
            }
            if (knownFiles.Count == 0)
                return (false, false, defaultLongestPath);

            var longestPath = knownFiles.Max(p => p.TrimEnd('.').Length);
            if (!multiItems["broken_directory"].Any()
                && !multiItems["broken_filename"].Any())
                return (false, false, longestPath);

            var missingDirs = multiItems["broken_directory"];
            var missingFiles = multiItems["broken_filename"];

            var broken = missingFiles.Where(knownFiles.Contains).ToList();
            if (broken.Count > 0)
            {
                Config.Log.Debug("List of broken files according to IRD:");
                foreach (var f in broken)
                    Config.Log.Debug(f);
                return (true, true, longestPath);
            }

            var knownDirs = new HashSet<string>(
                knownFiles
                    .Select(f => Path.GetDirectoryName(f)?.Replace('\\', '/'))
                    .Where(p => !string.IsNullOrEmpty(p))!,
                StringComparer.InvariantCultureIgnoreCase);
            var brokenDirs = missingDirs.Where(knownDirs.Contains).ToList();
            if (brokenDirs.Count > 0)
            {
                Config.Log.Debug("List of broken directories according to IRD:");
                foreach (var d in broken)
                    Config.Log.Debug(d);
                return (true, true, longestPath);
            }
            return (true, false, longestPath);
        }
    }
}
