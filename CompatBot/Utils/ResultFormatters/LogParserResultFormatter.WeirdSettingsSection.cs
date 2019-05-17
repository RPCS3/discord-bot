using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using CompatApiClient.Utils;
using DSharpPlus.Entities;

namespace CompatBot.Utils.ResultFormatters
{
    internal static partial class LogParserResult
    {
        private static void BuildWeirdSettingsSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            var notes = new List<string>();
            var serial = items["serial"] ?? "";
            if (!string.IsNullOrWhiteSpace(items["log_disabled_channels"]) || !string.IsNullOrWhiteSpace(items["log_disabled_channels_multiline"]))
                notes.Add("❗ Some logging priorities were modified, please reset and upload a new log");
            if (items["enable_tsx"] == "Disabled")
                notes.Add("⚠ TSX support is disabled; performance may suffer");
            if (items["spu_lower_thread_priority"] == EnabledMark
                && int.TryParse(items["thread_count"], out var threadCount)
                && threadCount > 4)
                notes.Add("❔ `Lower SPU thread priority` is enabled on a CPU with enough threads");
            if (items["llvm_arch"] is string llvmArch)
                notes.Add($"❔ LLVM target CPU architecture override is set to `{llvmArch.Sanitize()}`");

            if (items["renderer"] == "D3D12")
                notes.Add("💢 Do **not** use DX12 renderer");
            if (!string.IsNullOrEmpty(items["resolution"]) && items["resolution"] != "1280x720")
            {
                if (items["resolution"] != "1920x1080" || !Known1080pIds.Contains(serial))
                    notes.Add("⚠ `Resolution` was changed from the recommended `1280x720`");
                var dimensions = items["resolution"].Split("x");
                if (dimensions.Length > 1 && int.TryParse(dimensions[1], out var height) && height < 720)
                    notes.Add("⚠ `Resolution` below 720p will not improve performance");
            }

            if (items["ppu_decoder"] is string ppuDecoder)
            {
                if (KnownGamesThatRequireInterpreter.Contains(serial))
                {
                    if (ppuDecoder.Contains("Recompiler", StringComparison.InvariantCultureIgnoreCase))
                        notes.Add("⚠ This game requires `PPU Decoder` to use `Interpreter (fast)`");
                }
                else
                {
                    if (ppuDecoder.Contains("Interpreter", StringComparison.InvariantCultureIgnoreCase))
                        notes.Add("⚠ Please set `PPU Decoder` to use recompiler for better performance");
                }
            }
            if (items["spu_decoder"] is string spuDecoder && spuDecoder.Contains("Interpreter", StringComparison.InvariantCultureIgnoreCase))
                notes.Add("⚠ Please set `SPU Decoder` to use recompiler for better performance");

            if (items["approximate_xfloat"] is string approximateXfloat && approximateXfloat == DisabledMark)
                notes.Add("⚠ `Approximate xfloat` is disabled, please enable");

            if (!string.IsNullOrEmpty(serial))
            {
                CheckP5Settings(serial, items, notes);
                CheckAsurasWrathSettings(serial, items, notes);
                CheckJojoSettings(serial, items, notes);
                CheckSimpsonsSettings(serial, notes);
                CheckNierSettings(serial, items, notes);
                CheckScottPilgrimSettings(serial, items, notes);
            }
            else if (items["game_title"] == "vsh.self")
                CheckVshSettings(items, notes);

            if (items["hook_static_functions"] is string hookStaticFunctions && hookStaticFunctions == EnabledMark)
                notes.Add("⚠ `Hook Static Functions` is enabled, please disable");
            if (items["host_root"] is string hostRoot && hostRoot == EnabledMark)
                notes.Add("❔ `/host_root/` is enabled");
            if (items["gpu_texture_scaling"] is string gpuTextureScaling && gpuTextureScaling == EnabledMark)
                notes.Add("⚠ `GPU Texture Scaling` is enabled, please disable");
            if (items["af_override"] is string af)
            {
                if (af == "Disabled")
                    notes.Add("❌ `Anisotropic Filter` is `Disabled`, please use `Auto` instead");
                else if (af.ToLowerInvariant() != "auto" && af != "16")
                    notes.Add($"❔ `Anisotropic Filter` is set to `{af}x`, which makes little sense over `16x` or `Auto`");
            }

            if (items["resolution_scale"] is string resScale && int.TryParse(resScale, out var resScaleFactor) &&
                resScaleFactor < 100)
                notes.Add($"❔ `Resolution Scale` is `{resScale}%`; this will not increase performance");
            if (items["async_shaders"] == EnabledMark)
                notes.Add("❔ `Async Shader Compiler` is disabled");
            if (items["write_color_buffers"] == DisabledMark
                && !string.IsNullOrEmpty(serial)
                && KnownWriteColorBuffersIds.Contains(serial))
                notes.Add("⚠ `Write Color Buffers` is disabled, please enable");
            if (items["vertex_cache"] == EnabledMark
                && !string.IsNullOrEmpty(serial)
                && !KnownDisableVertexCacheIds.Contains(serial))
                notes.Add("⚠ `Vertex Cache` is disabled, please re-enable");
            if (items["cpu_blit"] is string cpuBlit && cpuBlit == EnabledMark &&
                items["write_color_buffers"] is string wcb && wcb == DisabledMark)
                notes.Add("❔ `Force CPU Blit` is enabled, but `Write Color Buffers` is disabled");
            if (items["zcull"] is string zcull && zcull == EnabledMark)
                notes.Add("⚠ `ZCull Occlusion Queries` are disabled, can result in visual artifacts");
            if (items["driver_recovery_timeout"] is string driverRecoveryTimeout &&
                int.TryParse(driverRecoveryTimeout, out var drtValue) && drtValue != 1000000)
            {
                if (drtValue == 0)
                    notes.Add("⚠ `Driver Recovery Timeout` is set to 0 (infinite), please use default value of 1000000");
                else if (drtValue < 10_000)
                    notes.Add($"⚠ `Driver Recovery Timeout` is set too low: {GetTimeFormat(drtValue)} (1 frame @ {(1_000_000.0 / drtValue):0.##} fps)");
                else if (drtValue > 10_000_000)
                    notes.Add($"⚠ `Driver Recovery Timeout` is set too high: {GetTimeFormat(drtValue)}");
            }

            if (!string.IsNullOrEmpty(serial)
                && KnownMotionControlsIds.Contains(serial)
                && items["pad_handler"] is string padHandler
                && !padHandler.StartsWith("DualShock"))
                notes.Add("❗ This game requires motion controls, please use DS3 or DS4 gamepad");

            if (items["audio_backend"] is string audioBackend && !string.IsNullOrEmpty(audioBackend))
            {
                if (items["os_type"] == "Windows" && !audioBackend.Equals("XAudio2", StringComparison.InvariantCultureIgnoreCase))
                    notes.Add("⚠ Please use `XAudio2` as the audio backend on Windows");
                else if (items["os_type"] == "Linux" && !audioBackend.Equals("OpenAL", StringComparison.InvariantCultureIgnoreCase))
                    notes.Add("ℹ `OpenAL` is the recommended audio backend on Linux");
                if (audioBackend.Equals("null", StringComparison.InvariantCultureIgnoreCase))
                    notes.Add("⚠ `Audio backend` is set to `null`");
            }

            if (int.TryParse(items["audio_volume"], out var audioVolume))
            {
                if (audioVolume < 10)
                    notes.Add($"⚠ Audio volume is set to {audioVolume}%");
                else if (audioVolume > 100)
                    notes.Add($"⚠ Audio volume is set to {audioVolume}%; audio clipping is to be expected");
            }

            if (items["hle_lwmutex"] is string hleLwmutex && hleLwmutex == EnabledMark)
                notes.Add("⚠ `HLE lwmutex` is enabled, might affect compatibility");
            if (items["spu_block_size"] is string spuBlockSize)
            {
                if (spuBlockSize == "Giga")
                    notes.Add($"⚠ Please change `SPU Block Size`, `{spuBlockSize}` is currently unstable.");
            }

            if (items["lib_loader"] is string libLoader
                && libLoader.Contains("Auto", StringComparison.InvariantCultureIgnoreCase)
                && (libLoader == "Auto"
                    || (libLoader.Contains("manual", StringComparison.InvariantCultureIgnoreCase) &&
                        string.IsNullOrEmpty(items["library_list"]))))
            {
                notes.Add("⚠ Please use `Load liblv2.sprx only` as a `Library loader`");
            }

            var notesContent = new StringBuilder();
            foreach (var line in SortLines(notes))
                notesContent.AppendLine(line);
            PageSection(builder, notesContent.ToString().Trim(), "Important Settings to Review");
        }

        private static readonly HashSet<string> P5Ids = new HashSet<string>
        {
            "BLES02247", "BLUS31604", "BLJM61346",
            "NPEB02436", "NPUB31848", "NPJB00769",
        };

        private static void CheckP5Settings(string serial, NameValueCollection items, List<string> notes)
        {
            if (P5Ids.Contains(serial))
            {
                if (items["ppu_decoder"] is string ppuDecoder && !ppuDecoder.Contains("LLVM"))
                    notes.Add("⚠ Please set `PPU Decoder` to `Recompiler (LLVM)`");
                if (items["spu_decoder"] is string spuDecoder)
                {
                    if (spuDecoder.Contains("Interpreter"))
                        notes.Add("⚠ Please set `SPU Decoder` to `Recompiler (LLVM)`");
                    else if (spuDecoder.Contains("ASMJIT"))
                        notes.Add("ℹ Please consider setting `SPU Decoder` to `Recompiler (LLVM)`");
                }

                if (items["spu_threads"] is string spuThreads && spuThreads != "2")
                {
                    if (int.TryParse(items["thread_count"], out var threadCount))
                    {
                        if (threadCount > 4)
                            notes.Add("ℹ `SPU Thread Count` is best to set to `2`");
                        else if (spuThreads != "1")
                            notes.Add("ℹ `SPU Thread Count` is best to set to `2` or `1`");
                    }
                    else
                        notes.Add("ℹ `SPU Thread Count` is best to set to `2`");
                }
                if (items["accurate_xfloat"] is string accurateXfloat && accurateXfloat == EnabledMark)
                    notes.Add("ℹ `Accurate xFloat` is not required, please disable");
                if (items["frame_limit"] is string frameLimit && frameLimit != "Off")
                    notes.Add("⚠ `Frame Limiter` is not required, please disable");
                if (items["write_color_buffers"] is string wcb && wcb == EnabledMark)
                    notes.Add("⚠ `Write Color Buffers` is not required, please disable");
                if (items["cpu_blit"] is string cpuBlit && cpuBlit == EnabledMark)
                    notes.Add("⚠ `Force CPU Blit` is not required, please disable");
                if (items["strict_rendering_mode"] is string srm && srm == EnabledMark)
                    notes.Add("⚠ `Strict Rendering Mode` is not required, please disable");
                if (string.IsNullOrEmpty(items["ppu_hash_patch"])
                    && items["resolution_scale"] is string resScale
                    && int.TryParse(resScale, out var scale)
                    && scale > 100)
                    notes.Add("⚠ `Resolution Scale` over 100% requires portrait sprites mod");
            }
        }

        private static void CheckAsurasWrathSettings(string serial, NameValueCollection items, List<string> notes)
        {
            if (serial == "BLES01227" || serial == "BLUS30721")
            {
                if (items["resolution_scale"] is string resScale
                    && int.TryParse(resScale, out var scale)
                    && scale > 100
                    && items["texture_scale_threshold"] is string thresholdStr
                    && int.TryParse(thresholdStr, out var threshold)
                    && threshold < 500)
                    notes.Add("⚠ `Resolution Scale` over 100% requires `Resolution Scale Threshold` set to `512x512`");

                if (items["af_override"] is string af && af != "Auto")
                    notes.Add("⚠ Please use `Auto` for `Anisotropic Filter Override`");
            }
        }

        private static readonly HashSet<string> AllStarBattleIds = new HashSet<string>
        {
            "BLES01986", "BLUS31405", "BLJS10217",
            "NPEB01922", "NPUB31391", "NPJB00331",
        };

        private static void CheckJojoSettings(string serial, NameValueCollection items, List<string> notes)
        {
            if (AllStarBattleIds.Contains(serial))
                notes.Add("ℹ Missing health bars are random");

            if ((AllStarBattleIds.Contains(serial) || serial == "BLJS10318" || serial == "NPJB00753")
                && items["audio_buffering"] == EnabledMark)
                notes.Add("ℹ If you experience audio issues, disable `Audio Buffering` or Pause/Unpause emulation");
        }

        private static void CheckSimpsonsSettings(string serial, List<string> notes)
        {
            if (serial == "BLES00142" || serial == "BLUS30065")
            {
                notes.Add("ℹ This game has a controller initialization bug. Simply unplug and replug it until it works.");
            }
        }

        private static void CheckNierSettings(string serial, NameValueCollection items, List<string> notes)
        {
            if (serial == "BLUS30481" || serial == "BLES00826" || serial == "BLJM60223")
            {
/*                if (items["spu_decoder"] is string spuDecoder
                    && spuDecoder.Contains("LLVM")
                    && items["spu_block_size"] is string spuBlockSize
                    && spuBlockSize != "Mega")
                {
                    notes.Add("⚠ This game needs `SPU Block Size` set to `Mega` when using `SPU Decoder` `Recompiler (LLVM)`");
                }
*/

                if (items["frame_limit"] is string frameLimit
                    && frameLimit != "30")
                    notes.Add("⚠ Please set `Framerate Limiter` to 30 fps");

                if (serial == "BLJM60223" && items["native_ui"] == EnabledMark)
                    notes.Add("ℹ To enter the character name, disable `Native UI` and use Japanese text");
            }
        }

        private static void CheckScottPilgrimSettings(string serial, NameValueCollection items, List<string> notes)
        {
            if (serial == "NPEB00258" || serial == "NPUB30162" || serial == "NPJB00068")
            {
                if (items["resolution"] is string res && res != "1920x1080")
                    notes.Add("⚠ For perfect sprite scaling without borders set `Resolution` to `1920x1080`");
            }
        }

        private static void CheckVshSettings(NameValueCollection items, List<string> notes)
        {
            if (items["build_branch"] is string branch
                && !branch.Contains("vsh", StringComparison.InvariantCultureIgnoreCase))
                notes.Add("ℹ Booting `vsh.self` currently requires a special build");
            if (items["lib_loader"] is string libLoader
                && libLoader != "Manual selection")
                notes.Add("⚠ `Library Loader` must be set to `Manual`");
            if (items["library_list"] is string libList
                && libList != "None")
                notes.Add("⚠ Every library module must be deselected");
            if (items["debug_console_mode"] is string decrMode && decrMode != EnabledMark)
                notes.Add("⚠ `Debug Console Mode` must be enabled");
            if (items["write_color_buffers"] is string wcb && wcb != EnabledMark)
                notes.Add("ℹ `Write Color Buffers` should be enabled for proper visuals");
            if (items["cpu_blit"] is string cpuBlit && cpuBlit != EnabledMark)
                notes.Add("ℹ `Force CPU Blit` should be enabled for proper visuals");
        }
    }
}