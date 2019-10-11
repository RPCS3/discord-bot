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
            var hasTsx = items["cpu_extensions"]?.Contains("TSX") ?? false;
            var hasTsxFa = items["cpu_extensions"]?.Contains("TSX-FA") ?? false;
            items["has_tsx"] = hasTsx ? EnabledMark : DisabledMark;
            items["has_tsx_fa"] = hasTsxFa ? EnabledMark : DisabledMark;
            if (items["enable_tsx"] == "Disabled" && hasTsx && !hasTsxFa)
                notes.Add("⚠ TSX support is disabled; performance may suffer");
            else if (items["enable_tsx"] == "Enabled" && hasTsxFa)
                notes.Add("⚠ Disable TSX support if you experience performance issues");
            if (items["spu_lower_thread_priority"] == EnabledMark
                && int.TryParse(items["thread_count"], out var threadCount)
                && threadCount > 4)
                notes.Add("❔ `Lower SPU thread priority` is enabled on a CPU with enough threads");
            if (items["llvm_arch"] is string llvmArch)
                notes.Add($"❔ LLVM target CPU architecture override is set to `{llvmArch.Sanitize(replaceBackTicks: true)}`");

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
            if (items["stretch_to_display"] == EnabledMark)
                notes.Add("🤢 `Stretch to Display Area` is enabled");
            if (KnownDisableVertexCacheIds.Contains(serial))
            {
                if (items["vertex_cache"] == DisabledMark)
                    notes.Add("⚠ This game requires disabling `Vertex Cache` in the GPU tab of the Settings");
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
            {
                if (KnownNoApproximateXFloatIds.Contains(serial))
                    notes.Add("ℹ `Approximate xfloat` is disabled");
                else
                    notes.Add("⚠ `Approximate xfloat` is disabled, please enable");
            }

            var ppuPatches = GetPatches(items["ppu_hash"], items["ppu_hash_patch"]);
            var ppuHashes = GetHashes(items["ppu_hash"]);
            if (!string.IsNullOrEmpty(serial))
            {
                CheckP5Settings(serial, items, notes);
                CheckAsurasWrathSettings(serial, items, notes);
                CheckJojoSettings(serial, items, notes, ppuPatches, ppuHashes);
                CheckSimpsonsSettings(serial, notes);
                CheckNierSettings(serial, items, notes, ppuPatches, ppuHashes);
                CheckDod3Settings(serial, items, notes, ppuPatches, ppuHashes);
                CheckScottPilgrimSettings(serial, items, notes);
                CheckGoWSettings(serial, items, notes);
                CheckDesSettings(serial, items, notes, ppuPatches, ppuHashes);
                CheckTlouSettings(serial, items, notes);
                CheckMgs4Settings(serial, items, notes);
            }
            else if (items["game_title"] == "vsh.self")
                CheckVshSettings(items, notes);
            if (items["game_category"] == "1P")
                CheckPs1ClassicsSettings(items, notes);

            if (items["hook_static_functions"] is string hookStaticFunctions && hookStaticFunctions == EnabledMark)
                notes.Add("⚠ `Hook Static Functions` is enabled, please disable");
            if (items["host_root"] is string hostRoot && hostRoot == EnabledMark)
                notes.Add("❔ `/host_root/` is enabled");
            if (items["spurs_threads"] is string spursSetting
                && int.TryParse(spursSetting, out var spursThreads)
                && spursThreads != 6)
            {
                if (spursThreads > 6 || spursThreads < 1)
                    notes.Add($"⚠ `Max SPURS Threads` is set to `{spursThreads}`; please change it back to `6`");
                else
                    notes.Add($"ℹ `Max SPURS Threads` is set to `{spursThreads}`; may result in game crash");
            }

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
            {
                if (DesIds.Contains(serial) && ppuPatches.Any())
                    notes.Add("ℹ `Write Color Buffers` is disabled");
                else
                    notes.Add("⚠ `Write Color Buffers` is disabled, please enable");
            }
            if (items["vertex_cache"] == EnabledMark
                && !string.IsNullOrEmpty(serial)
                && !KnownDisableVertexCacheIds.Contains(serial))
                notes.Add("⚠ `Vertex Cache` is disabled, please re-enable");
            if (items["frame_skip"] == EnabledMark)
                notes.Add("⚠ `Frame Skip` is enabled, please disable");
            if (items["cpu_blit"] is string cpuBlit
                && cpuBlit == EnabledMark
                && items["write_color_buffers"] is string wcb
                && wcb == DisabledMark)
                notes.Add("❔ `Force CPU Blit` is enabled, but `Write Color Buffers` is disabled");
            if (items["zcull"] is string zcull && zcull == EnabledMark)
                notes.Add("⚠ `ZCull Occlusion Queries` are disabled, can result in visual artifacts");
            if (items["driver_recovery_timeout"] is string driverRecoveryTimeout
                && int.TryParse(driverRecoveryTimeout, out var drtValue) && drtValue != 1000000)
            {
                if (drtValue == 0)
                    notes.Add("⚠ `Driver Recovery Timeout` is set to 0 (infinite), please use default value of 1000000");
                else if (drtValue < 10_000)
                    notes.Add($"⚠ `Driver Recovery Timeout` is set too low: {GetTimeFormat(drtValue)} (1 frame @ {(1_000_000.0 / drtValue):0.##} fps)");
                else if (drtValue > 10_000_000)
                    notes.Add($"⚠ `Driver Recovery Timeout` is set too high: {GetTimeFormat(drtValue)}");
            }

            if (!KnownFpsUnlockPatchIds.Contains(serial) || !ppuPatches.Any())
            {
                if (items["vblank_rate"] is string vblank
                    && int.TryParse(vblank, out var vblankRate)
                    && vblankRate != 60)
                    notes.Add($"ℹ `VBlank Rate` is set to {vblankRate} Hz ({vblankRate/60.0*100:0}%)");

                if (items["clock_scale"] is string clockScaleStr
                    && int.TryParse(clockScaleStr, out var clockScale)
                    && clockScale != 100)
                    notes.Add($"ℹ `Clock Scale` is set to {clockScale}%");
            }

            if (items["mtrsx"] is string mtrsx && mtrsx == EnabledMark)
                notes.Add("ℹ `Multithreaded RSX` is enabled");

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
                && (libLoader == "Auto"
                    || (libLoader.Contains("manual", StringComparison.InvariantCultureIgnoreCase) &&
                        (string.IsNullOrEmpty(items["library_list"]) || items["library_list"] == "None"))))
            {
                if (items["game_title"] != "vsh.self")
                    notes.Add("⚠ Please use `Load liblv2.sprx only` as a `Library loader`");
            }

            if (notes.Any())
                items["weird_settings_notes"] = "true";
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

                if (items["spu_threads"] is string spuThreads)
                {
                    if (items["has_tsx"] == EnabledMark)
                    {
                        if (spuThreads != "Auto")
                            notes.Add("ℹ `SPU Thread Count` is best to set to `Auto`");
                    }
                    else if (spuThreads != "2")
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
                }
                if (items["spu_loop_detection"] == EnabledMark)
                    notes.Add("ℹ If you have distorted audio, try disabling `SPU Loop Detection`");
                if (items["accurate_xfloat"] is string accurateXfloat && accurateXfloat == EnabledMark)
                    notes.Add("ℹ `Accurate xfloat` is not required, please disable");
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

        private static readonly HashSet<string> KnownJojoPatches = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "6875682ab309df32307c5305c43bb132c4e261fa",
            "18cf9a4e8196684ed9ee816f82649561fd1bf182",
        };

        private static void CheckJojoSettings(string serial, NameValueCollection items, List<string> notes, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes)
        {
            if (AllStarBattleIds.Contains(serial) || serial == "BLJS10318" || serial == "NPJB00753")
            {
                if (items["audio_buffering"] == EnabledMark && items["audio_buffer_duration"] != "20")
                    notes.Add("ℹ If you experience audio issues, set `Audio Buffer Duration` to `20ms`");
                else if (items["audio_buffering"] == DisabledMark)
                    notes.Add("ℹ If you experience audio issues, check `Enable Buffering` and set `Audio Buffer Duration` to `20ms`");

                if ((serial == "BLUS31405" || serial == "BLJS10318")
                    && items["vblank_rate"] is string vbrStr
                    && int.TryParse(vbrStr, out var vbr))
                {
                    if (ppuPatches.Any())
                    {
                        if (vbr == 60)
                            notes.Add("ℹ `VBlank Rate` is not set; FPS is limited to 30");
                        else if (vbr == 120)
                            notes.Add("✅ Settings are set for the 60 FPS patch");
                        else
                            notes.Add($"⚠ Settings are configured for the {vbr / 2} FPS patch, which is unsupported");
                    }
                    else
                    {
                        if (vbr > 60)
                            notes.Add("ℹ Unlocking FPS requires game patch");
                        if (ppuHashes.Overlaps(KnownJojoPatches))
                            notes.Add("ℹ This game has an FPS unlock patch, see [Game Patches](https://github.com/RPCS3/rpcs3/wiki/Game-Patches)");
                    }
                }
            }
        }

        private static void CheckSimpsonsSettings(string serial, List<string> notes)
        {
            if (serial == "BLES00142" || serial == "BLUS30065")
            {
                notes.Add("ℹ This game has a controller initialization bug. Simply unplug and replug it until it works.");
            }
        }

        private static readonly HashSet<string> KnownNierPatches = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "13950b2e29e05a115fe317815d3da9d2b2baee65",
            "f098ee8410599c81c89f90d698340a078dc69a90",
        };

        private static void CheckNierSettings(string serial, NameValueCollection items, List<string> notes, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes)
        {
            if (serial == "BLUS30481" || serial == "BLES00826" || serial == "BLJM60223")
            {
                var frameLimit = items["frame_limit"];
                var vsync = items["vsync"] == EnabledMark;
                if (ppuPatches.Any())
                {
                    if (frameLimit == "Off")
                    {
                        if (!vsync)
                            notes.Add("⚠ Please set `Framerate Limiter` to `Auto` or enable V-Sync");
                    }
                    else if (frameLimit != "59.95"
                             && frameLimit != "60"
                             && frameLimit != "Auto")
                    {
                        if (vsync)
                            notes.Add("⚠ Please set `Framerate Limiter` to `Off`");
                        else
                            notes.Add("⚠ Please set `Framerate Limiter` to `Auto` or enable V-Sync");
                    }
                    else
                    {
                        if (vsync)
                            notes.Add("⚠ Please set `Framerate Limiter` to `Off`");
                    }
                }
                else
                {
                    if (frameLimit != "30")
                        notes.Add("⚠ Please set `Framerate Limiter` to 30 fps");
                    if (ppuHashes.Overlaps(KnownNierPatches))
                        notes.Add("ℹ This game has an FPS unlock patch, see [Game Patches](https://github.com/RPCS3/rpcs3/wiki/Game-Patches#nier)");
                }

                if (serial == "BLJM60223" && items["native_ui"] == EnabledMark)
                    notes.Add("ℹ To enter the character name, disable `Native UI` and use Japanese text");

                if (items["sleep_timer"] is string sleepTimer
                    && sleepTimer != "Usleep Only"
                    && sleepTimer != "Usleep")
                    notes.Add("⚠ Please set `Sleep Timers Accuracy` setting to `Usleep Only`");
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

        private static readonly HashSet<string> Gow3Ids = new HashSet<string>
        {
            "BCAS25003", "BCES00510", "BCES00516", "BCES00799", "BCJS37001", "BCUS98111", "BCKS15003",
        };

        private static readonly HashSet<string> GowHDIds = new HashSet<string>
        {
            "BCAS20102", "BCES00791", "BCES00800", "BLJM60200", "BCUS98229", // collection except volume II
            "NPUA80491", "NPUA80490", "NPEA00255", "NPEA00256", "NPJA00062", "NPJA00061", "NPJA00066",
        };

        private static readonly HashSet<string> GowAscIds = new HashSet<string>
        {
            "BCAS25016", "BCES01741", "BCES01742", "BCUS98232",
            "NPEA00445", "NPEA90123", "NPUA70216", "NPUA70269", "NPUA80918",
            "NPHA80258",
        };

        private static void CheckGoWSettings(string serial, NameValueCollection items, List<string> notes)
        {
            if (serial == "NPUA70080") // GoW3 Demo
                return;

            if (GowHDIds.Contains(serial))
            {
                if (items["renderer"] is string renderer && renderer != "OpenGL")
                    notes.Add("⚠ `OpenGL` is recommended for classic God of War games");
            }
            else if (Gow3Ids.Contains(serial))
            {
                notes.Add("ℹ Black screen after Santa Monica logo is fine for up to 5 minutes");
                if (items["spu_decoder"] is string spuDecoder
                    && spuDecoder.Contains("LLVM")
                    && items["spu_block_size"] is string blockSize
                    && blockSize != "Mega")
                    notes.Add("⚠ Please change `SPU Block Size` to `Mega` for this game");
            }
            else if (GowAscIds.Contains(serial))
                notes.Add("ℹ This game is known to be very unstable");
        }

        private static readonly HashSet<string> DesIds = new HashSet<string>
        {
            "BLES00932", "BLUS30443", "BCJS30022", "BCJS70013",
            "NPEB01202", "NPUB30910", "NPJA00102",
        };

        private static readonly HashSet<string> KnownDesPatches = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "83681f6110d33442329073b72b8dc88a2f677172",
            "5446a2645880eefa75f7e374abd6b7818511e2ef",
        };

        private static void CheckDesSettings(string serial, NameValueCollection items, List<string> notes, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes)
        {
            if (!DesIds.Contains(serial))
                return;

            if (items["frame_limit"] is string frameLimit && frameLimit != "Off")
                notes.Add("⚠ `Frame Limiter` should be `Off`");

            if (items["spu_loop_detection"] == EnabledMark)
                notes.Add("⚠ `SPU Loop Detection` is `Enabled`, and can cause visual artifacts");

            if (items["spu_threads"] is string spuThreads && spuThreads != "Auto")
                notes.Add("⚠ Please set `SPU Thread Count` to `Auto` for best performance");

            if (serial != "BLES00932" && serial != "BLUS30443")
                return;

            if (items["vblank_rate"] is string vbrStr
                && items["clock_scale"] is string clkStr
                && int.TryParse(vbrStr, out var vblankRate)
                && int.TryParse(clkStr, out var clockScale))
            {
                if (ppuPatches.Any())
                {
                    if (vblankRate == 60)
                        notes.Add("ℹ `VBlank Rate` is not set; FPS is limited to 30");
                    var vbrRatio = vblankRate / 60.0;
                    var clkRatio = clockScale / 100.0;
                    if (Math.Abs(vbrRatio - clkRatio) > 0.05)
                        notes.Add($"⚠ `VBlank Rate` is set to {vblankRate} Hz ({vbrRatio * 100:0}%), but `Clock Scale` is set to {clockScale}%");
                    else if (vblankRate == 60)
                        notes.Add("ℹ Settings are not set for the FPS patch");
                    else
                        notes.Add($"✅ Settings are set for the {vblankRate / 2} FPS patch");
                }
                else
                {
                    if (vblankRate > 60)
                        notes.Add("ℹ Unlocking FPS requires game patch");
                    if (ppuHashes.Overlaps(KnownDesPatches))
                        notes.Add("ℹ This game has an FPS unlock patch, see [Game Patches](https://github.com/RPCS3/rpcs3/wiki/Game-Patches#demons-souls)");
                }
            }
            else if (ppuPatches.Any())
            {
                notes.Add("ℹ `VBlank Rate` or `Clock Scale` is not set");
            }
        }

        private static readonly HashSet<string> Dod3Ids = new HashSet<string>
        {
            "BLUS31197", "NPUB31251",
            "NPEB01407",
            "BLJM61043", "BCAS20311",
        };

        private static readonly HashSet<string> KnownDod3Patches = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
        {
            "f2f7f7ea0444353884bb715152147c3a29f4e790",
            "2b393f064786e5895d5a576621deb4c9107a8f0b",
            "b18834a8f21cd29a091b287a66656a279ccba507",
            "9c04f427625a0064282432e4edfefe9e0956c303",
            "e1a44e5d3fb03a37f0445e92ed13abce8d6efdd4",
            "a017576369165f3746730724c8ae762ed9bc64d8",
            "c09c496514f6dc591434575b04eb7c003826c11d",
            "5eb979631fbbe531db5d20f0622dca5a8b64090e",
        };

        private static void CheckDod3Settings(string serial, NameValueCollection items, List<string> notes, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes)
        {
            if (!Dod3Ids.Contains(serial))
                return;

            if (items["vblank_rate"] is string vbrStr
                && int.TryParse(vbrStr, out var vbr))
            {
                if (ppuPatches.Any())
                {
                    if (vbr == 60)
                        notes.Add("ℹ `VBlank Rate` is not set; FPS is limited to 30");
                    else if (vbr == 120 || vbr == 240)
                        notes.Add($"✅ Settings are set for the {vbr / 2} FPS patch");
                    else if (vbr > 240)
                        notes.Add($"⚠ Settings are configured for the {vbr/2} FPS patch, which is too high; issues are expected");
                    else
                        notes.Add($"ℹ Settings are set for the {vbr/2} FPS patch");
                }
                else
                {
                    if (vbr > 60)
                        notes.Add("ℹ Unlocking FPS requires game patch");
                    if (ppuHashes.Overlaps(KnownDod3Patches))
                        notes.Add("ℹ This game has an FPS unlock patch, see [Game Patches](https://github.com/RPCS3/rpcs3/wiki/Game-Patches#drakengard-3)");
                    else if (ppuHashes.Any())
                        notes.Add("🤔 Very interesting version of the game you got there");
                }
            }
        }



        private static readonly HashSet<string> TlouIds = new HashSet<string>
        {
            "BCAS20270", "BCES01584", "BCES01585", "BCJS37010", "BCUS98174",
            "NPEA00435", "NPEA00521", "NPJA00096", "NPJA00129",
            "NPUA30134", "NPUA70257", "NPUA80960", "NPUA81175",
            "NPHA80206", "NPHA80279",
        };

        private static void CheckTlouSettings(string serial, NameValueCollection items, List<string> notes)
        {
            if (!TlouIds.Contains(serial))
                return;

            if (items["write_color_buffers"] == DisabledMark)
                notes.Add("⚠ Please enable `Write Color Buffers`");

            if (items["read_color_buffers"] == DisabledMark)
                notes.Add("⚠ Please enable `Read Color Buffers`");

            if (items["read_depth_buffer"] == DisabledMark)
                notes.Add("⚠ Please enable `Read Depth Buffer`");

            if (items["cpu_blit"] == EnabledMark)
                notes.Add("⚠ Please disable `Force CPU Blit`");

            if (items["resolution_scale"] is string resFactor
                && int.TryParse(resFactor, out var resolutionScale)
                && resolutionScale > 100
                && items["strict_rendering_mode"] != EnabledMark)
                notes.Add("⚠ Please set `Resolution Scale` to 100%");
        }

        private static readonly HashSet<string> Mgs4Ids = new HashSet<string>
        {
            "BLAS55005", "BLES00246", "BLJM57001", "BLJM67001", "BLKS25001", "BLUS30109", "BLUS30148",
            "NPEB00027", "NPEB02182", "NPEB90116", "NPJB00698", "NPJB90149", "NPUB31633",
            "NPHB00065", "NPHB00067",
        };

        private static void CheckMgs4Settings(string serial, NameValueCollection items, List<string> notes)
        {
            if (!Mgs4Ids.Contains(serial))
                return;

            notes.Add("ℹ Metal Gear Solid 4 just got ingame, and is still very unstable");
            notes.Add("ℹ There is no universal set of settings and game updates that works for everyone");
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

        private static void CheckPs1ClassicsSettings(NameValueCollection items, List<string> notes)
        {
            if (items["lib_loader"] is string libLoader
                && libLoader != "Auto")
                notes.Add("⚠ `Library Loader` must be set to `Auto` for PS1 Classics");
            if (items["spu_decoder"] is string spuDecoder
                && !spuDecoder.Contains("LLVM"))
                notes.Add("ℹ Please set `SPU Decoder` to use `Recompiler (LLVM)`");
        }
    }
}
