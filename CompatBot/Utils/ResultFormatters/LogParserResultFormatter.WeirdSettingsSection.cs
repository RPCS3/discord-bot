using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CompatApiClient.Utils;
using CompatBot.EventHandlers.LogParsing.POCOs;
using DSharpPlus.Entities;

namespace CompatBot.Utils.ResultFormatters;

internal static partial class LogParserResult
{
    private static void BuildWeirdSettingsSection(DiscordEmbedBuilder builder, LogParseState state, List<string> generalNotes)
    {
        var items = state.CompletedCollection!;
        var multiItems = state.CompleteMultiValueCollection!;
        var notes = new List<string>();
        var serial = items["serial"] ?? "";
        _ = int.TryParse(items["thread_count"], out var threadCount);
        if (items["disable_logs"] == EnabledMark)
            notes.Add("❗ `Silence All Logs` is enabled, please disable and upload a new log");
        else if (!string.IsNullOrWhiteSpace(items["log_disabled_channels"])
                 || !string.IsNullOrWhiteSpace(items["log_disabled_channels_multiline"]))
            notes.Add("❗ Some logging priorities were modified, please reset and upload a new log");
        var hasTsx = items["cpu_extensions"]?.Contains("TSX") ?? false;
        var hasTsxFa = items["cpu_extensions"]?.Contains("TSX-FA") ?? false;
        items["has_tsx"] = hasTsx ? EnabledMark : DisabledMark;
        items["has_tsx_fa"] = hasTsxFa ? EnabledMark : DisabledMark;
        Version? buildVersion = null;
        if (items["build_branch"] is "HEAD" or "master"
            && Version.TryParse(items["build_full_version"], out buildVersion)
            && buildVersion < TsxFaFixedVersion)
        {
            if (items["enable_tsx"] == "Disabled" && hasTsx && !hasTsxFa)
                notes.Add("ℹ️ TSX support is disabled");
            else if (items["enable_tsx"] == "Enabled" && hasTsxFa)
                notes.Add("⚠️ Disable TSX support if you experience performance issues");
        }
        else
        {
            if (items["enable_tsx"] == "Disabled" && hasTsx)
                notes.Add("ℹ️ TSX support is disabled");
        }
        /*
        if (items["spu_lower_thread_priority"] == EnabledMark && threadCount > 4)
            notes.Add("❔ `Lower SPU thread priority` is enabled on a CPU with enough threads");
        */
        if (items["cpu_model"] is string cpu)
        {
            if (cpu.StartsWith("AMD")
                && cpu.Contains("Ryzen")
                && items["os_type"] != "Linux")
            {
                if (Version.TryParse(items["os_version"], out var winVer)
                    && winVer is { Major: <10 } or { Major: 10, Build: <18362 }) // everything before win 10 1903
                {
                    if (items["thread_scheduler"] == "OS")
                    {
                        if (buildVersion >= IntelThreadSchedulerBuildVersion)
                            notes.Add("⚠️ Please enable RPCS3 `Thread Scheduler` option in the CPU Settings");
                        else
                            notes.Add("⚠️ Please enable `Thread Scheduler` in the CPU Settings");
                    }
                    else
                        notes.Add("ℹ️ Changing `Thread Scheduler` option may or may not increase performance");
                }
                else
                    notes.Add("ℹ️ Changing `Thread Scheduler` option may or may not increase performance");
            }
            else if (cpu.StartsWith("Intel")
                     && threadCount > 11
                     && buildVersion >= IntelThreadSchedulerBuildVersion)
                notes.Add("ℹ️ Changing `Thread Scheduler` option may or may not increase performance");
        }
        if (items["llvm_arch"] is string llvmArch)
            notes.Add($"❔ LLVM target CPU architecture override is set to `{llvmArch.Sanitize(replaceBackTicks: true)}`");

        if (items["renderer"] == "D3D12")
            notes.Add("💢 Do **not** use DX12 renderer");
        if (items["renderer"] == "OpenGL"
            && items["supported_gpu"] == EnabledMark
            && !GowHDIds.Contains(serial))
            notes.Add("⚠️ `Vulkan` is the recommended `Renderer`");
        if (items["renderer"] == "Vulkan"
            && items["supported_gpu"] == DisabledMark)
            notes.Add("❌ Selected `Vulkan` device is not supported, please use `OpenGL` instead");
        var selectedRes = items["resolution"];
        var selectedRatio = items["aspect_ratio"];
        if (!string.IsNullOrEmpty(selectedRes))
        {
            if (selectedRes == "1280x720" && items["game_category"] == "1P")
            {
                if (serial.Length > 3)
                {
                    if (serial[2] == 'E')
                    {
                        if (selectedRes != "720x576")
                            notes.Add("⚠️ PAL PS1 Classics should use `Resolution` of `720x576`");
                    }
                    else
                    {
                        if (selectedRes != "720x480")
                            notes.Add("⚠️ NTSC PS1 Classics should use `Resolution` of `720x480`");
                    }
                }
            }
            else if (selectedRes != "1280x720")
            {
                var supported = false;
                var known = false;
                if (items["game_supported_resolution_list"] is string supportedRes)
                {
                    var supportedList = PsnMetaExtensions.GetSupportedResolutions(supportedRes);
                    supported = selectedRatio == "Auto"
                        ? supportedList.Any(i => i.resolution == selectedRes)
                        : supportedList.Any(i => i.resolution == selectedRes && i.aspectRatio == selectedRatio);
                    known = true;
                }
                if (selectedRes == "1920x1080" && Known1080pIds.Contains(serial))
                {
                    supported = true;
                    known = true;
                }

                if (known)
                {
                    if (!supported)
                        notes.Add("❌ Selected `Resolution` is not supported, please set to recommended `1280x720`");
                }
                else if (items["game_category"] != "1P")
                    notes.Add("⚠️ `Resolution` was changed from the recommended `1280x720`");
                var dimensions = selectedRes.Split("x");
                if (dimensions.Length > 1
                    && int.TryParse(dimensions[0], out var width)
                    && int.TryParse(dimensions[1], out var height))
                {
                    var ratio = Reduce(width, height);
                    var canBeWideOrSquare = width is 720 && height is 480 or 576;
                    if (ratio == (8, 5))
                        ratio = (16, 10);
                    if (selectedRatio is not null and not "Auto")
                    {
                        var arParts = selectedRatio.Split(':');
                        if (arParts.Length > 1
                            && int.TryParse(arParts[0], out var arWidth)
                            && int.TryParse(arParts[1], out var arHeight))
                        {
                            var arRatio = Reduce(arWidth, arHeight);
                            if (arRatio == (8, 5))
                                arRatio = (16, 10);
                            /*
                            if (items["game_category"] == "1P")
                            {
                                if (arRatio != (4, 3))
                                    notes.Add("⚠️ PS1 Classics should use `Aspect Ratio` of 4:3");
                            }
                            else
                            */
                            if (arRatio != ratio && !canBeWideOrSquare)
                                notes.Add($"⚠️ Selected `Resolution` has aspect ratio of {ratio.numerator}:{ratio.denominator}, but `Aspect Ratio` is set to {selectedRatio}");
                        }
                    }
                    else
                    {
                        if (canBeWideOrSquare)
                            notes.Add("ℹ️ Setting `Aspect Ratio` to `16:9` or `4:3` instead of `Auto` may improve compatibility");
                        else
                            notes.Add($"ℹ️ Setting `Aspect Ratio` to `{ratio.numerator}:{ratio.denominator}` instead of `Auto` may improve compatibility");
                    }
                    if (height < 720 && items["game_category"] != "1P")
                        notes.Add("⚠️ `Resolution` below 720p will not improve performance");
                }
            }

        }
        if (items["stretch_to_display"] == EnabledMark)
            notes.Add("🤢 `Stretch to Display Area` is enabled");
        var vertexCacheDisabled = items["vertex_cache"] == EnabledMark || items["mtrsx"] == EnabledMark;
        if (KnownDisableVertexCacheIds.Contains(serial) && !vertexCacheDisabled)
            notes.Add("⚠️ This game requires disabling `Vertex Cache` option");

        if (multiItems["rsx_not_supported_feature"].Contains("alpha-to-one for multisampling"))
        {
            if (items["msaa"] is not null and not "Disabled")
                generalNotes.Add("ℹ️ The driver or GPU does not support all required features for proper MSAA implementation, which may result in minor visual artifacts");
        }
        var isWireframeBugPossible = items["gpu_info"] is string gpuInfo
                                     && buildVersion < RdnaMsaaFixedVersion
                                     && Regex.IsMatch(gpuInfo, @"Radeon RX 5\d{3}", RegexOptions.IgnoreCase) // RX 590 is a thing 😔
                                     && !gpuInfo.Contains("RADV");
        if (items["msaa"] == "Disabled")
        {
            if (!isWireframeBugPossible)
                notes.Add("ℹ️ `Anti-aliasing` is disabled, which may result in visual artifacts");
        }
        else if (items["msaa"] is not null and not "Disabled")
        {
            if (isWireframeBugPossible)
                notes.Add("⚠️ Please disable `Anti-aliasing` if you experience wireframe-like visual artifacts");
        }

        var vsync = items["vsync"] == EnabledMark;
        string? vkPm;
        if (items["rsx_present_mode"] is string pm)
            RsxPresentModeMap.TryGetValue(pm, out vkPm);
        else
            vkPm = null;
        if (items["force_fifo_present"] == EnabledMark)
        {
            notes.Add("⚠️ Double-buffered VSync is forced");
            vsync = true;
        }
        if (items["rsx_swapchain_mode"] is "2")
            vsync = true;
        if (vsync && items["frame_limit"] is string frameLimitStr)
        {
            if (frameLimitStr == "Auto")
                notes.Add("ℹ️ Frame rate might be limited to 30 fps due to enabled VSync");
            else if (double.TryParse(frameLimitStr, NumberStyles.Float, NumberFormatInfo.InvariantInfo, out var frameLimit))
            {
                if (frameLimit is >30 and <60)
                    notes.Add("ℹ️ Frame rate might be limited to 30 fps due to enabled VSync");
                else if (frameLimit < 30)
                    notes.Add("ℹ️ Frame rate might be limited to 15 fps due to enabled VSync");
                else
                    notes.Add("ℹ️ Frame pacing might be affected due to VSync and Frame Limiter enabled at the same time");
            }
        }
        if (!vsync && vkPm != "VK_PRESENT_MODE_IMMEDIATE_KHR")
        {
            var pmDesc = vkPm switch
            {
                "VK_PRESENT_MODE_MAILBOX_KHR" => "Fast Sync",
                "VK_PRESENT_MODE_FIFO_KHR" => "Double-buffered VSync",
                "VK_PRESENT_MODE_FIFO_RELAXED_KHR" => "Adaptive VSync",
                _ => null,
            };
            if (pmDesc != null)
                notes.Add($"ℹ️ `VSync` is disabled, but the drivers provided `{pmDesc}`");
        }
        if (items["async_texture_streaming"] == EnabledMark)
        {
            if (items["async_queue_scheduler"] == "Device")
                notes.Add("⚠️ If you experience visual artifacts, try setting `Async Queue Scheduler` to use `Host`");
            notes.Add("⚠️ If you experience visual artifacts, try disabling `Async Texture Streaming`");
        }
            
        if (items["ppu_decoder"] is string ppuDecoder)
        {
            if (KnownGamesThatRequireInterpreter.Contains(serial))
            {
                if (ppuDecoder.Contains("Recompiler", StringComparison.InvariantCultureIgnoreCase))
                    notes.Add("⚠️ This game requires `PPU Decoder` to use `Interpreter (fast)`");
            }
            else
            {
                if (ppuDecoder.Contains("Interpreter", StringComparison.InvariantCultureIgnoreCase))
                    notes.Add("⚠️ Please set `PPU Decoder` to use recompiler for better performance");
            }
        }
        if (items["spu_decoder"] is string spuDecoder && spuDecoder.Contains("Interpreter", StringComparison.InvariantCultureIgnoreCase))
            notes.Add("⚠️ Please set `SPU Decoder` to use recompiler for better performance");

        if (items["accurate_getllar"] == EnabledMark)
            notes.Add("ℹ️ `Accurate GETLLAR` is enabled");
        if (items["accurate_putlluc"] == EnabledMark)
            notes.Add("ℹ️ `Accurate PUTLLUC` is enabled");
        if (items["accurate_rsx_reservation"] == EnabledMark)
            notes.Add("ℹ️ `Accurate RSX Reservation Access` is enabled");

        if (items["accurate_xfloat"] is string accurateXfloat)
        {
            if (accurateXfloat == EnabledMark)
            {
                if (!KnownGamesThatRequireAccurateXfloat.Contains(serial))
                    notes.Add("⚠️ `Accurate xfloat` is not required, and significantly impacts performance");
            }
            else
            {
                if (KnownGamesThatRequireAccurateXfloat.Contains(serial))
                    notes.Add("⚠️ `Accurate xfloat` is required for this game, but it will significantly impact performance");
            }
        }
        if (items["relaxed_xfloat"] is DisabledMark)
        {
            if (KnownNoRelaxedXFloatIds.Contains(serial))
                notes.Add("ℹ️ `Relaxed xfloat` is disabled");
            else
                notes.Add("⚠️ `Relaxed xfloat` is disabled, please enable");
        } 
        if (items["approximate_xfloat"] is DisabledMark)
        {
            if (KnownNoApproximateXFloatIds.Contains(serial))
                notes.Add("ℹ️ `Approximate xfloat` is disabled");
            else
                notes.Add("⚠️ `Approximate xfloat` is disabled, please enable");
        }
        if (items["resolution_scale"] is string resScale
            && int.TryParse(resScale, out var resScaleFactor))
        {
            if (resScaleFactor < 100)
                notes.Add($"❔ `Resolution Scale` is `{resScale}%`; this will not increase performance");
            if (resScaleFactor != 100
                && items["texture_scale_threshold"] is string thresholdStr
                && int.TryParse(thresholdStr, out var threshold)
                && threshold < 16
                && !KnownResScaleThresholdIds.Contains(serial))
            {
                notes.Add("⚠️ `Resolution Scale Threshold` below `16x16` may result in corrupted visuals and game crash");
            }
            if (resScaleFactor > 100
                && items["msaa"] is string msaa
                && msaa != "Disabled")
            {
                var level = "ℹ️";
                if (resScaleFactor > 200)
                    level = "⚠️";
                notes.Add($"{level} If you have missing UI elements or experience performance issues, decrease `Resolution Scale` or disable `Anti-aliasing`");
            }

            if (resScaleFactor > 300)
                notes.Add("⚠️ Excessive `Resolution Scale` may impact performance");
        }
        var allPpuHashes = GetPatches(multiItems["ppu_patch"], false);
        var ppuPatches = allPpuHashes.Where(kvp => kvp.Value > 0).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        var ppuHashes = new HashSet<string>(allPpuHashes.Keys, StringComparer.InvariantCultureIgnoreCase);
        var patchNames = multiItems["patch_desc"];
        if (items["write_color_buffers"] == DisabledMark
            && !string.IsNullOrEmpty(serial)
            && KnownWriteColorBuffersIds.Contains(serial))
        {
            if (DesIds.Contains(serial) && ppuPatches.Count != 0)
                notes.Add("ℹ️ `Write Color Buffers` is disabled");
            else
                notes.Add("⚠️ `Write Color Buffers` is disabled, please enable");
        }
        if (items["vertex_cache"] == EnabledMark
            && items["mtrsx"] == DisabledMark
            && !string.IsNullOrEmpty(serial)
            && !KnownDisableVertexCacheIds.Contains(serial))
            notes.Add("ℹ️ `Vertex Cache` is disabled, and may impact performance");
        if (items["frame_skip"] == EnabledMark)
            notes.Add("⚠️ `Frame Skip` is enabled, please disable");
        if (items["cpu_blit"] is EnabledMark 
            && items["write_color_buffers"] is DisabledMark)
            notes.Add("❔ `Force CPU Blit` is enabled, but `Write Color Buffers` is disabled");
        if (items["zcull"] is EnabledMark)
            notes.Add("⚠️ `ZCull Occlusion Queries` is disabled, which can result in visual artifacts");
        else if (items["relaxed_zcull"] is string relaxedZcull)
        {
            if (relaxedZcull == EnabledMark && !KnownGamesThatWorkWithRelaxedZcull.Contains(serial))
                notes.Add("ℹ️ `Relaxed ZCull Sync` is enabled and can cause performance and visual issues");
            else if (relaxedZcull == DisabledMark && KnownGamesThatWorkWithRelaxedZcull.Contains(serial))
                notes.Add("ℹ️ Enabling `Relaxed ZCull Sync` for this game may improve performance");
        }
        if (!KnownFpsUnlockPatchIds.Contains(serial) || ppuPatches.Count == 0)
        {
            if (items["vblank_rate"] is string vblank
                && int.TryParse(vblank, out var vblankRate)
                && vblankRate != 60)
                notes.Add($"ℹ️ `VBlank Rate` is set to {vblankRate} Hz ({vblankRate / 60.0 * 100:0}%)");

            if (items["clock_scale"] is string clockScaleStr
                && int.TryParse(clockScaleStr, out var clockScale)
                && clockScale != 100)
                notes.Add($"ℹ️ `Clock Scale` is set to {clockScale}%");
        }
        if (items["lib_loader"] is string libLoader
            && (libLoader == "Auto"
                || ((libLoader.Contains("manual", StringComparison.InvariantCultureIgnoreCase)
                     || libLoader.Contains("strict", StringComparison.InvariantCultureIgnoreCase))
                    && (string.IsNullOrEmpty(items["library_list"]) || items["library_list"] == "None"))))
        {
            if (items["game_title"] != "vsh.self")
                notes.Add("⚠️ Please use `Load liblv2.sprx only` as a `Library loader`");
        }
        bool warnLibraryOverrides = items["library_list_hle"] is not null and not "None";
        if (items["library_list_lle"] is string lleLibList and not "None")
        {
            if (lleLibList.Contains("sysutil"))
                notes.Add("❗ Never override `sysutil` firmware modules");

            if (lleLibList.Contains("libvdec"))
            {
                var weirdModules = lleLibList.Split(',', StringSplitOptions.TrimEntries).Except(new[] {"libvdec.sprx"}).ToArray();
                if (weirdModules.Length > 0)
                {
                    notes.Add("⚠️ Please do not override Firmware Libraries that you weren't asked to");
                    warnLibraryOverrides = false;
                }
            }
            else
                warnLibraryOverrides = true;
        }
        if (warnLibraryOverrides)
            notes.Add("⚠️ Please disable any Firmware Libraries overrides");
            
        if (!string.IsNullOrEmpty(serial))
        {
            CheckP5Settings(serial, items, notes, generalNotes, ppuHashes, ppuPatches, patchNames);
            CheckAsurasWrathSettings(serial, items, notes);
            CheckJojoSettings(serial, state, notes, ppuPatches, ppuHashes, generalNotes);
            CheckSimpsonsSettings(serial, generalNotes);
            CheckNierSettings(serial, items, notes, ppuPatches, ppuHashes, generalNotes);
            CheckDod3Settings(serial, items, notes, ppuPatches, ppuHashes, generalNotes);
            CheckScottPilgrimSettings(serial, items, notes, generalNotes);
            CheckGoWSettings(serial, items, notes, generalNotes);
            CheckDesSettings(serial, items, notes, ppuPatches, ppuHashes, generalNotes);
            CheckTlouSettings(serial, items, notes, ppuPatches, patchNames);
            CheckRdrSettings(serial, items, notes);
            CheckMgs4Settings(serial, items, generalNotes);
            CheckProjectDivaSettings(serial, items, notes, ppuPatches, ppuHashes, generalNotes);
            CheckGt5Settings(serial, items, generalNotes);
            CheckGt6Settings(serial, items, notes, generalNotes);
            //CheckRatchetSettings(serial, items, notes, generalNotes);
            CheckSly4Settings(serial, items, notes);
            CheckDragonsCrownSettings(serial, items, notes);
            CheckLbpSettings(serial, items, generalNotes);
            CheckKillzone3Settings(serial, items, notes, patchNames);
        }
        else if (items["game_title"] == "vsh.self")
            CheckVshSettings(items, notes, generalNotes);
        if (items["game_category"] == "1P")
            CheckPs1ClassicsSettings(items, notes, generalNotes);

        if (items["game_title"] != "vsh.self" && items["debug_console_mode"] == EnabledMark)
            notes.Add("⚠️ `Debug Console Mode` is enabled, and may cause game crashes");
        if (items["hook_static_functions"] is EnabledMark)
            notes.Add("⚠️ `Hook Static Functions` is enabled, please disable");
        if (items["host_root"] is EnabledMark)
            notes.Add("❔ `/host_root/` is enabled");
        if (items["ppu_threads"] is string ppuThreads
            && ppuThreads != "2")
            notes.Add($"⚠️ `PPU Threads` is set to `{ppuThreads.Sanitize()}`; please change it back to `2`");
        if (items["spurs_threads"] is string spursSetting
            && int.TryParse(spursSetting, out var spursThreads)
            && spursThreads != 6)
        {
            if (spursThreads is <1 or >6)
                notes.Add($"⚠️ `Max SPURS Threads` is set to `{spursThreads}`; please change it back to `6`");
            else
                notes.Add($"ℹ️ `Max SPURS Threads` is set to `{spursThreads}`; may result in game crash");
        }

        if (items["gpu_texture_scaling"] is EnabledMark)
            notes.Add("⚠️ `GPU Texture Scaling` is enabled, please disable");
        if (items["af_override"] is string af)
        {
            if (af == "Disabled")
                notes.Add("❌ `Anisotropic Filter` is `Disabled`, please use `Auto` instead");
            else if (af.ToLowerInvariant() != "auto" && af != "16")
                notes.Add($"❔ `Anisotropic Filter` is set to `{af}x`, which makes little sense over `16x` or `Auto`");
        }

        if (items["shader_mode"] == "Interpreter only")
            notes.Add("⚠️ `Shader Interpreter Only` mode is not accurate and very demanding");
        else if (items["shader_mode"]?.StartsWith("Async") is false)
            notes.Add("❔ Async shader compilation is disabled");
        if (items["driver_recovery_timeout"] is string driverRecoveryTimeout
            && int.TryParse(driverRecoveryTimeout, out var drtValue)
            && drtValue != 1000000)
        {
            if (drtValue == 0)
                notes.Add("⚠️ `Driver Recovery Timeout` is set to 0 (infinite), please use default value of 1000000");
            else if (drtValue < 10_000)
                notes.Add($"⚠️ `Driver Recovery Timeout` is set too low: {GetTimeFormat(drtValue)} (1 frame @ {(1_000_000.0 / drtValue):0.##} fps)");
            else if (drtValue > 10_000_000)
                notes.Add($"⚠️ `Driver Recovery Timeout` is set too high: {GetTimeFormat(drtValue)}");
        }
        if (items["driver_wakeup_delay"] is string strDriverWakeup
            && int.TryParse(strDriverWakeup, out var driverWakeupDelay)
            && driverWakeupDelay > 1)
        {
            if (driverWakeupDelay > 1000)
                notes.Add($"⚠️ `Driver Wake-up Delay` is set to {GetTimeFormat(driverWakeupDelay)}, and will impact performance");
            else
                notes.Add($"ℹ️ `Driver Wake-up Delay` is set to {GetTimeFormat(driverWakeupDelay)}");
        }
        if (items["audio_buffering"] == EnabledMark
            && int.TryParse(items["audio_buffer_duration"], out var duration)
            && duration > 100)
            notes.Add($"ℹ️ `Audio Buffer Duration` is set to {duration}ms, which may cause audio lag");
        if (items["audio_stretching"] == EnabledMark)
            notes.Add("ℹ️ `Audio Time Stretching` is `Enabled`");

        if (items["mtrsx"] is EnabledMark)
        {
            if (multiItems["fatal_error"].Any(f => f.Contains("VK_ERROR_OUT_OF_POOL_MEMORY_KHR")))
                notes.Add("⚠️ `Multithreaded RSX` is enabled, please disable for this game");
            else if (threadCount < 6)
                notes.Add("⚠️ `Multithreaded RSX` is enabled on a CPU with few threads");
            else
                notes.Add("ℹ️ `Multithreaded RSX` is enabled");
        }

        if (items["failed_pad"] is string failedPad)
            notes.Add($"⚠️ Binding `{failedPad.Sanitize(replaceBackTicks: true)}` failed, check if device is connected.");

        if (!string.IsNullOrEmpty(serial)
            && KnownMotionControlsIds.Contains(serial)
            && !multiItems["pad_handler"].Any(h => h.StartsWith("DualS"))
            && !multiItems["pad_has_gyro"].Any(g => g is "1" or "true"))
            notes.Add("❗ This game requires motion controls, please use native handler for DualShock 3, 4, DualSense, or SDL handler with compatible controller");

        if (items["audio_backend"] is string audioBackend && !string.IsNullOrEmpty(audioBackend))
        {
            if (buildVersion is not null && buildVersion < CubebBuildVersion)
            {
                if (items["os_type"] is "Windows" && !audioBackend.Equals("XAudio2", StringComparison.InvariantCultureIgnoreCase))
                    notes.Add("⚠️ Please use `XAudio2` as the audio backend for this build");
                else if (items["os_type"] == "Linux"
                         && !audioBackend.Equals("OpenAL", StringComparison.InvariantCultureIgnoreCase)
                         && !audioBackend.Equals("FAudio", StringComparison.InvariantCultureIgnoreCase))
                    notes.Add("ℹ️ `FAudio` and `OpenAL` are the recommended audio backends for this build");
            }
            else
            {
                if (items["os_type"] is "Windows" or "Linux" && !audioBackend.Equals("Cubeb", StringComparison.InvariantCultureIgnoreCase))
                    notes.Add("⚠️ Please use `Cubeb` as the audio backend");
            }
            if (audioBackend.Equals("null", StringComparison.InvariantCultureIgnoreCase))
                notes.Add("⚠️ `Audio backend` is set to `null`");
        }

        if (int.TryParse(items["audio_volume"], out var audioVolume))
        {
            if (audioVolume < 10)
                notes.Add($"⚠️ Audio volume is set to {audioVolume}%");
            else if (audioVolume > 100)
                notes.Add($"⚠️ Audio volume is set to {audioVolume}%; audio clipping is to be expected");
        }

        if (items["hle_lwmutex"] is EnabledMark)
            notes.Add("⚠️ `HLE lwmutex` is enabled, might affect compatibility");
        if (items["spu_block_size"] is string spuBlockSize)
        {
            if (spuBlockSize != "Safe" && spuBlockSize != "Mega")
                notes.Add($"⚠️ Please change `SPU Block Size` to `Safe/Mega`, currently `{spuBlockSize}` is unstable.");
        }

        if (items["auto_start_on_boot"] == DisabledMark)
            notes.Add("❔ `Automatically start games after boot` is disabled");
        else if (items["always_start_on_boot"] == DisabledMark)
            notes.Add("❔ `Always start after boot` is disabled");

        if (items["custom_config"] != null && notes.Any())
            generalNotes.Add("⚠️ To change custom configuration, **Right-click on the game**, then `Configure`");

        var notesContent = new StringBuilder();
        foreach (var line in SortLines(notes))
            notesContent.AppendLine(line);
        PageSection(builder, notesContent.ToString().Trim(), "Important Settings to Review");
    }

    private static readonly HashSet<string> P5Ids = new()
    {
        "BLES02247", "BLUS31604", "BLJM61346",
        "NPEB02436", "NPUB31848", "NPJB00769",
    };

        
    private static readonly HashSet<string> KnownP5Patches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "e72e715d646a94770d1902364bc66fe33b1b6606",
        "b8c34f774adb367761706a7f685d4f8d9d355426",
        "3b394da7912181d308bf08505009b3578521c756",
        "9da9b988693598fbe1e2d316d1e927c37ad666bc",
    };

    private static void CheckP5Settings(string serial, NameValueCollection items, List<string> notes, List<string> generalNotes, HashSet<string> ppuHashes, Dictionary<string, int> ppuPatches, UniqueList<string> patchNames)
    {
        if (!P5Ids.Contains(serial))
            return;

        if (items["ppu_decoder"] is string ppuDecoder && !ppuDecoder.Contains("LLVM"))
            notes.Add("⚠️ Please set `PPU Decoder` to `Recompiler (LLVM)`");
        if (items["spu_decoder"] is string spuDecoder)
        {
            if (spuDecoder.Contains("Interpreter"))
                notes.Add("⚠️ Please set `SPU Decoder` to `Recompiler (LLVM)`");
            else if (spuDecoder.Contains("ASMJIT"))
                notes.Add("ℹ️ Please consider setting `SPU Decoder` to `Recompiler (LLVM)`");
        }

        if (items["spu_threads"] is string spuThreads)
        {
            if (items["has_tsx"] == EnabledMark)
            {
                if (spuThreads != "Auto")
                    notes.Add("ℹ️ `SPU Thread Count` is best to set to `Auto`");
            }
            else if (spuThreads != "2")
            {
                if (int.TryParse(items["thread_count"], out var threadCount))
                {
                    if (threadCount > 4)
                        notes.Add("ℹ️ `SPU Thread Count` is best to set to `2`");
                    else if (spuThreads != "1")
                        notes.Add("ℹ️ `SPU Thread Count` is best to set to `2` or `1`");
                }
                else
                    notes.Add("ℹ️ `SPU Thread Count` is best to set to `2`");
            }
        }
        if (items["spu_loop_detection"] == EnabledMark)
            notes.Add("ℹ️ If you have distorted audio, try disabling `SPU Loop Detection`");
        if (items["frame_limit"] is not null and not "Off")
            notes.Add("⚠️ `Frame Limiter` is not required, please disable");
        if (items["write_color_buffers"] is EnabledMark)
            notes.Add("⚠️ `Write Color Buffers` is not required, please disable");
        if (items["cpu_blit"] is EnabledMark)
            notes.Add("⚠️ `Force CPU Blit` is not required, please disable");
        if (items["strict_rendering_mode"] is EnabledMark)
            notes.Add("⚠️ `Strict Rendering Mode` is not required, please disable");
        if (ppuPatches.Count == 0
            && items["resolution_scale"] is string resScale
            && int.TryParse(resScale, out var scale)
            && scale > 100)
            notes.Add("⚠️ `Resolution Scale` over 100% requires portrait sprites mod");
        /*
         * 60 fps v1   = 12
         * 60 fps v2   = 268
         */
        if (patchNames.Any(n => n.Contains("60")) || ppuPatches.Values.Any(n => n > 260))
            notes.Add("ℹ️ 60 fps patch is enabled; please disable if you have any strange issues");
            
        if (!KnownP5Patches.Overlaps(ppuHashes))
            generalNotes.Add("🤔 Very interesting version of the game you got there");
    }

    private static void CheckAsurasWrathSettings(string serial, NameValueCollection items, List<string> notes)
    {
        if (serial is "BLES01227" or "BLUS30721")
        {
            if (items["resolution_scale"] is string resScale
                && int.TryParse(resScale, out var scale)
                && scale > 100
                && items["texture_scale_threshold"] is string thresholdStr
                && int.TryParse(thresholdStr, out var threshold)
                && threshold < 500)
                notes.Add("⚠️ `Resolution Scale` over 100% requires `Resolution Scale Threshold` set to `512x512`");

            if (items["af_override"] is not null and not "Auto")
                notes.Add("⚠️ Please use `Auto` for `Anisotropic Filter Override`");
        }
    }

    private static readonly HashSet<string> AllStarBattleIds = new()
    {
        "BLES01986", "BLUS31405", "BLJS10217",
        "NPEB01922", "NPUB31391", "NPJB00331",
    };

    private static readonly HashSet<string> KnownJojoPatches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "6875682ab309df32307c5305c43bb132c4e261fa",
        "18cf9a4e8196684ed9ee816f82649561fd1bf182",
    };

    private static void CheckJojoSettings(string serial, LogParseState state, List<string> notes, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes, List<string> generalNotes)
    {
        var items = state.CompletedCollection!;
        if (AllStarBattleIds.Contains(serial) || serial is "BLJS10318" or "NPJB00753")
        {
            if (items["audio_buffering"] == EnabledMark && items["audio_buffer_duration"] != "20")
                notes.Add("ℹ️ If you experience audio issues, set `Audio Buffer Duration` to `20ms`");
            else if (items["audio_buffering"] == DisabledMark)
                notes.Add("ℹ️ If you experience audio issues, check `Enable Buffering` and set `Audio Buffer Duration` to `20ms`");

            if (serial is "BLUS31405" or "BLJS10318"
                && items["vblank_rate"] is string vbrStr
                && int.TryParse(vbrStr, out var vbr))
            {
                if (ppuPatches.Any())
                {
                    if (vbr == 60)
                        notes.Add("ℹ️ `VBlank Rate` is not set; FPS is limited to 30");
                    else if (vbr == 120)
                        notes.Add("✅ Settings are set for the 60 FPS patch");
                    else
                        notes.Add($"⚠️ Settings are configured for the {vbr / 2} FPS patch, which is unsupported");
                }
                else
                {
                    if (vbr > 60)
                        notes.Add("ℹ️ Unlocking FPS requires game patch");
                    if (ppuHashes.Overlaps(KnownJojoPatches))
                        generalNotes.Add($"ℹ️ This game has an FPS unlock patch");
                }
            }

            if (serial == "BLUS31405"
                && items["compat_database_path"] is string compatDbPath
                && compatDbPath.Contains("JoJo ASB Emulator v.04")
                && state.CompleteMultiValueCollection!["rap_file"].Any())
                generalNotes.Add("🤔 Very interesting version of the game you got there");
        }
    }

    private static void CheckSimpsonsSettings(string serial, List<string> generalNotes)
    {
        if (serial is "BLES00142" or "BLUS30065")
            generalNotes.Add("ℹ️ This game has a controller initialization bug. Please use [the patch](https://wiki.rpcs3.net/index.php?title=The_Simpsons_Game#Patches).");
    }

    private static readonly HashSet<string> KnownNierPatches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "13950b2e29e05a115fe317815d3da9d2b2baee65",
        "f098ee8410599c81c89f90d698340a078dc69a90",
    };

    private static void CheckNierSettings(string serial, NameValueCollection items, List<string> notes, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes, List<string> generalNotes)
    {
        if (serial is "BLUS30481" or "BLES00826" or "BLJM60223")
        {
            var frameLimit = items["frame_limit"];
            var vsync = items["vsync"] == EnabledMark;
            if (ppuPatches.Any() && ppuPatches.Values.Max() > 1)
                notes.Add("✅ Using the variable rate FPS patch");
            else if (ppuPatches.Any())
            {
                if (frameLimit == "Off")
                {
                    if (!vsync)
                        notes.Add("⚠️ Please set `Framerate Limiter` to `Auto` or enable V-Sync");
                }
                else if (frameLimit != "59.95"
                         && frameLimit != "60"
                         && frameLimit != "Auto")
                {
                    if (vsync)
                        notes.Add("⚠️ Please set `Framerate Limiter` to `Off`");
                    else
                        notes.Add("⚠️ Please set `Framerate Limiter` to `Auto` or enable V-Sync");
                }
                else
                {
                    if (vsync)
                        notes.Add("⚠️ Please set `Framerate Limiter` to `Off`");
                }
                notes.Add("⚠️ There is a new variable frame rate FPS patch available");
            }
            else
            {
                if (frameLimit != "30")
                    notes.Add("⚠️ Please set `Framerate Limiter` to 30 fps");
                if (ppuHashes.Overlaps(KnownNierPatches))
                    generalNotes.Add("ℹ️ This game has an FPS unlock patch");
            }

            if (serial == "BLJM60223" && items["native_ui"] == EnabledMark)
                notes.Add("ℹ️ To enter the character name, disable `Native UI` and use Japanese text");

            if (items["sleep_timer"] is string sleepTimer
                && sleepTimer != "Usleep Only"
                && sleepTimer != "Usleep")
                notes.Add("⚠️ Please set `Sleep Timers Accuracy` setting to `Usleep Only`");
        }
    }

    private static void CheckScottPilgrimSettings(string serial, NameValueCollection items, List<string> notes, List<string> generalNotes)
    {
        if (serial is "NPEB00258" or "NPUB30162" or "NPJB00068")
        {
            if (items["resolution"] is not null and not "1920x1080")
                notes.Add("⚠️ For perfect sprite scaling without borders set `Resolution` to `1920x1080`");
            if (items["game_version"] is string gameVer
                && Version.TryParse(gameVer, out var v)
                && v < new Version(1, 03))
                generalNotes.Add("⚠️ Please update game to v1.03 if you experience visual issues");
        }
    }

    private static readonly HashSet<string> Gow3Ids = new()
    {
        "BCAS25003", "BCES00510", "BCES00516", "BCES00799", "BCJS37001", "BCUS98111", "BCKS15003",
    };

    private static readonly HashSet<string> GowHDIds = new()
    {
        "BCAS20102", "BCES00791", "BCES00800", "BLJM60200", "BCUS98229", // collection except volume II
        "NPUA80491", "NPUA80490", "NPEA00255", "NPEA00256", "NPJA00062", "NPJA00061", "NPJA00066",
    };

    private static readonly HashSet<string> GowAscIds = new()
    {
        "BCAS25016", "BCES01741", "BCES01742", "BCUS98232",
        "NPEA00445", "NPEA90123", "NPUA70216", "NPUA70269", "NPUA80918",
        "NPHA80258",
    };

    private static void CheckGoWSettings(string serial, NameValueCollection items, List<string> notes, List<string> generalNotes)
    {
        if (serial == "NPUA70080") // GoW3 Demo
            return;

        if (Gow3Ids.Contains(serial))
            generalNotes.Add("ℹ️ Black screen after Santa Monica logo is fine for up to 5 minutes");
        else if (GowAscIds.Contains(serial))
            generalNotes.Add("ℹ️ This game is known to be very unstable");
    }

    private static readonly HashSet<string> DesIds = new()
    {
        "BLES00932", "BLUS30443", "BCJS30022", "BCJS70013",
        "NPEB01202", "NPUB30910", "NPJA00102",
    };

    private static readonly HashSet<string> KnownDesPatches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "83681f6110d33442329073b72b8dc88a2f677172",
        "5446a2645880eefa75f7e374abd6b7818511e2ef",
    };

    private static void CheckDesSettings(string serial, NameValueCollection items, List<string> notes, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes, List<string> generalNotes)
    {
        if (!DesIds.Contains(serial))
            return;

        if (items["spu_block_size"] is not null and not "Safe")
            notes.Add("ℹ️ Please set `SPU Block Size` to `Safe` to reduce crash rate");

        if (items["frame_limit"] is not null and not "Off")
            notes.Add("⚠️ `Frame Limiter` should be `Off`");

        if (items["spu_loop_detection"] == EnabledMark)
            notes.Add("⚠️ `SPU Loop Detection` is `Enabled`, and can cause visual artifacts");

        if (items["spu_threads"] is not null and not "Auto")
            notes.Add("⚠️ Please set `SPU Thread Count` to `Auto` for best performance");

        if (serial != "BLES00932" && serial != "BLUS30443")
            return;

        if (items["vblank_rate"] is string vbrStr
            && items["clock_scale"] is string clkStr
            && int.TryParse(vbrStr, out var vblankRate)
            && int.TryParse(clkStr, out var clockScale))
        {
            var vbrRatio = vblankRate / 60.0;
            var clkRatio = clockScale / 100.0;
            if (ppuPatches.Values.Any(v => v >= 25))
            {
                if (vblankRate != 60)
                    notes.Add($"ℹ️ `VBlank Rate` is set to {vblankRate} Hz ({vbrRatio * 100:0}%)");
                if (clockScale != 100)
                    notes.Add($"⚠️ `Clock Scale` is set to {clockScale}%, please set it back to 100%");
                else
                    notes.Add("✅ Settings are set for the variable rate FPS patch");
            }
            else if (ppuPatches.Any())
            {
                if (vblankRate == 60)
                    notes.Add("ℹ️ `VBlank Rate` is not set; FPS is limited to 30");
                if (Math.Abs(vbrRatio - clkRatio) > 0.05)
                    notes.Add($"⚠️ `VBlank Rate` is set to {vblankRate} Hz ({vbrRatio * 100:0}%), but `Clock Scale` is set to {clockScale}%");
                else if (vblankRate == 60)
                    notes.Add("ℹ️ Settings are not set for the fixed rate FPS patch");
                else
                    notes.Add($"✅ Settings are set for the fixed rate {vblankRate / 2} FPS patch");
                notes.Add("⚠️ There is a new variable frame rate FPS patch available");
            }
            else
            {
                if (ppuHashes.Overlaps(KnownDesPatches))
                    generalNotes.Add("ℹ️ This game has an FPS unlock patch");
            }
        }
        else if (ppuPatches.Any())
        {
            notes.Add("ℹ️ `VBlank Rate` or `Clock Scale` is not set");
        }
    }

    private static readonly HashSet<string> Dod3Ids = new()
    {
        "BLUS31197", "NPUB31251",
        "NPEB01407",
        "BLJM61043", "BCAS20311",
    };

    private static readonly HashSet<string> KnownDod3Patches = new(StringComparer.InvariantCultureIgnoreCase)
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

    private static void CheckDod3Settings(string serial, NameValueCollection items, List<string> notes, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes, List<string> generalNotes)
    {
        if (!Dod3Ids.Contains(serial))
            return;

        if (items["vblank_rate"] is string vbrStr
            && int.TryParse(vbrStr, out var vbr))
        {
            if (ppuPatches.Any())
            {
                if (vbr == 60)
                    notes.Add("ℹ️ `VBlank Rate` is not set; FPS is limited to 30");
                else if (vbr is 120 or 240)
                    notes.Add($"✅ Settings are set for the {vbr / 2} FPS patch");
                else if (vbr > 240)
                    notes.Add($"⚠️ Settings are configured for the {vbr/2} FPS patch, which is too high; issues are expected");
                else
                    notes.Add($"ℹ️ Settings are set for the {vbr/2} FPS patch");
            }
            else
            {
                if (vbr > 60)
                    notes.Add("ℹ️ Unlocking FPS requires game patch");
                if (ppuHashes.Overlaps(KnownDod3Patches))
                    generalNotes.Add("ℹ️ This game has an FPS unlock patch");
                else if (ppuHashes.Any())
                    generalNotes.Add("🤔 Very interesting version of the game you got there");
            }
        }
    }

    private static readonly HashSet<string> TlouIds = new()
    {
        "BCAS20270", "BCES01584", "BCES01585", "BCJS37010", "BCUS98174",
        "NPEA00435", "NPEA90122", "NPHA80243", "NPHA80279", "NPJA00096", "NPJA00129", "NPUA70257", "NPUA80960", "NPUA81175", 
    };

    private static void CheckTlouSettings(string serial, NameValueCollection items, List<string> notes, Dictionary<string, int> ppuPatches, UniqueList<string> patchNames)
    {
        if (!TlouIds.Contains(serial))
            return;

        if (items["spu_block_size"] is not null and not "Safe")
            notes.Add("ℹ️ Please set `SPU Block Size` to `Safe` to reduce crash rate");
        if (items["cpu_blit"] == EnabledMark)
            notes.Add("⚠️ Please disable `Force CPU Blit`");
        if (items["read_color_buffers"] == DisabledMark)
            notes.Add("⚠️ Please enable `Read Color Buffers`");
        var depthBufferPatchesAreApplied = ppuPatches.Any() && patchNames.Count(n => n.Contains("depth buffer", StringComparison.OrdinalIgnoreCase)) > 1;

        if (items["build_branch"] is "HEAD" or "master"
            && Version.TryParse(items["build_full_version"], out var buildVersion)
            && buildVersion < FixedTlouRcbBuild)
        {
            if (items["read_depth_buffer"] == EnabledMark)
            {
                if (depthBufferPatchesAreApplied)
                    notes.Add("⚠️ `Read Depth Buffer` is not required with applied patches");
            }
            else
            {
                if (!depthBufferPatchesAreApplied)
                    notes.Add("⚠️ Please enable `Read Depth Buffer` or appropriate patches");
            }
        }
        else
        {
            if (items["read_depth_buffer"] == EnabledMark)
                notes.Add("⚠️ `Read Depth Buffer` is not required");
        }
        if (ppuPatches.Any() && patchNames.Any(n => n.Contains("MLAA", StringComparison.OrdinalIgnoreCase))) // when MLAA patch is applied
        {
            if (items["write_color_buffers"] == EnabledMark)
                notes.Add("⚠️ `Write Color Buffers` is not required with applied MLAA patch");
        }
        else
        {
            if (items["write_color_buffers"] == DisabledMark)
                notes.Add("⚠️ Please enable MLAA patch (Recommended) or `Write Color Buffers`");
        }
        if (items["resolution_scale"] is string resFactor
            && int.TryParse(resFactor, out var resolutionScale))
        {
            if (resolutionScale > 100 && items["strict_rendering_mode"] != EnabledMark)
            {
                if (!patchNames.Any(n => n.Contains("MLAA")))
                    notes.Add("⚠️ Please set `Resolution Scale` to 100% or enable MLAA patch");
                if (items["texture_scale_threshold"] is string tst
                    && int.TryParse(tst, out var scaleThreshold)
                    && scaleThreshold > 1)
                {
                    notes.Add("⚠️ Please set `Resolution Scale Threshold` to 1x1");
                }
            }
        }
    }

    private static readonly HashSet<string> Killzone3Ids = new()
    {
        "BCAS20157", "BCAS25008", "BCES01007", "BCJS30066", "BCJS37003", "BCJS70016", "BCJS75002", "BCUS98234",
        "NPEA00321", "NPEA90084", "NPEA90085", "NPEA90086", "NPHA80140", "NPJA90178", "NPUA70133",
    };

    private static void CheckKillzone3Settings(string serial, NameValueCollection items, List<string> notes, UniqueList<string> patchNames)
    {
        if (!Killzone3Ids.Contains(serial))
            return;

        if (patchNames.Any(n => n.Contains("MLAA", StringComparison.OrdinalIgnoreCase)))
        {
            if (items["write_color_buffers"] == EnabledMark)
                notes.Add("⚠️ `Write Color Buffers` is not required with applied MLAA patch");
        }
        else
        {
            if (items["write_color_buffers"] == DisabledMark)
                notes.Add("⚠️ Please enable MLAA patch (recommended) or `Write Color Buffers`");
        }
    }
    private static readonly HashSet<string> RdrIds = new()
    {
        "BLAS50296", "BLES00680", "BLES01179", "BLES01294", "BLUS30418", "BLUS30711", "BLUS30758",
        "BLJM60314", "BLJM60403", "BLJM61181", "BLKS20315",
        "NPEB00833", "NPHB00465", "NPHB00466", "NPUB30638", "NPUB30639",
        "NPUB50139", // max payne 3 / rdr bundle???
    };

    private static void CheckRdrSettings(string serial, NameValueCollection items, List<string> notes)
    {
        if (!RdrIds.Contains(serial))
            return;

        if (items["write_color_buffers"] == DisabledMark)
            notes.Add("ℹ️ `Write Color Buffers` is required for proper visuals at night");
    }

    private static readonly HashSet<string> Mgs4Ids = new()
    {
        "BLAS55005", "BLES00246", "BLJM57001", "BLJM67001", "BLKS25001", "BLUS30109", "BLUS30148",
        "NPEB00027", "NPEB02182", "NPEB90116", "NPJB00698", "NPJB90149", "NPUB31633",
        "NPHB00065", "NPHB00067",
    };

    private static void CheckMgs4Settings(string serial, NameValueCollection items, List<string> generalNotes)
    {
        if (!Mgs4Ids.Contains(serial))
            return;

        if (items["build_branch"] == "mgs4")
        {
            //notes.Clear();
            generalNotes.Add("⚠️ Custom RPCS3 builds are not officially supported");
            generalNotes.Add("⚠️ This custom build comes with pre-configured settings, don't change anything");
        }
    }

    private static readonly HashSet<string> PdfIds = new()
    {
        "BLJM60527", "BLUS31319", "BLAS50576",
        "NPEB01393", "NPUB31241", "NPHB00559", "NPJB00287"
    };


    private static readonly HashSet<string> KnownPdfPatches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "f3227f57ec001582b253035fd90de77f05ead470",
        "c02e3b52e3d75f52f76fb8f0fb5be7ca4d921949",
        "1105af0a4d6a4a1481930c6f3090c476cde06c4c",
    };

    private static readonly HashSet<string> Pdf2ndIds = new()
    {
        "BCAS50693", "BLAS50693", "BLES02029", "BLJM61079",
        "NPUB31488", "NPHB00671", "NPHB00662", "NPEB02013", "NPJB00435",
    };

    private static readonly HashSet<string> KnownPdf2ndPatches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "092c43e2bcacccfe3cdc22b0ab8062b91d4e1cf9",
        "67e0e7c9b2a7a340c914a0d078e25aac1047e4d4",
        "51d336edfa3774f2db83ed030611f462c097c40b",
        "c70b15d3f6694af74fa329dd4fc25fe28a59e9cc",
        "c3291f5919ca147ac854de10f7436f4ad494233f",
        "058cf39c07fd13f100c1f6dc40a0ead9bf3ad51b",
        "8fc9f26ed77cc9237db0e6348dcf9d6c451b6220",
        "311fcd98af6adc5e64e6a833eb959f43b0976193",
    };

    private static void CheckProjectDivaSettings(string serial, NameValueCollection items, List<string> notes, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes, List<string> generalNotes)
    {
        if (!PdfIds.Contains(serial) && !Pdf2ndIds.Contains(serial))
            return;

        if (!ppuPatches.Any())
        {
            if (ppuHashes.Overlaps(KnownPdfPatches))
                generalNotes.Add("ℹ️ This game has an FPS unlock patch");
            else if (ppuHashes.Overlaps(KnownPdf2ndPatches))
                generalNotes.Add("ℹ️ This game has an FPS unlock patch");
        }
        if (items["frame_limit"] is not null and not "Off")
            notes.Add("⚠️ `Frame Limiter` should be `Off`");
            
        if (!ppuHashes.Overlaps(KnownPdf2ndPatches))
            generalNotes.Add("🤔 Very interesting version of the game you got there");
    }

    private static readonly HashSet<string> Gt5Ids = new()
    {
        "BCAS20108", "BCAS20151", "BCAS20154", "BCAS20164", "BCAS20229", "BCAS20267",
        "BCES00569",
        "BCJS30001", "BCJS30050", "BCJS30100",
        "BCUS98114", "BCUS98272", "BCUS98394",
    };

    private static void CheckGt5Settings(string serial, NameValueCollection items, List<string> generalNotes)
    {
        if (!Gt5Ids.Contains(serial))
            return;

        if (items["game_version"] is string gameVer
            && Version.TryParse(gameVer, out var v)
            && v > new Version(1, 05)
            && v < new Version(1, 10))
            generalNotes.Add("ℹ️ Game versions between 1.05 and 1.10 can fail to boot with HDD space error");
    }

    private static readonly HashSet<string> Gt6Ids = new()
    {
        "BCAS20519", "BCAS20520", "BCAS20521", "BCAS25018", "BCAS25019",
        "BCES01893", "BCES01905", "BCJS37016", "BCUS98296", "BCUS99247",
        "NPEA00502", "NPJA00113", "NPUA81049",
    };

    private static void CheckGt6Settings(string serial, NameValueCollection items, List<string> notes, List<string> generalNotes)
    {
        if (!Gt6Ids.Contains(serial))
            return;

        if (items["spu_loop_detection"] == EnabledMark)
            notes.Add("⚠️ Please disable `SPU Loop Detection` for this game");

        if (items["game_version"] is string gameVer
            && Version.TryParse(gameVer, out var v))
        {
            if (v > new Version(1, 05))
            {
                var needChanges = false;
                if (items["write_color_buffers"] == EnabledMark)
                {
                    notes.Add("⚠️ `Write Color Buffers` is enabled, and can cause screen flicker");
                    needChanges = true;
                }
                if (items["read_color_buffer"] == DisabledMark)
                {
                    notes.Add("⚠️ Please enable `Read Color Buffer`");
                    needChanges = true;
                }
                if (items["read_depth_buffer"] == DisabledMark)
                {
                    notes.Add("⚠️ Please enable `Read Depth Buffer`");
                    needChanges = true;
                }
                if (needChanges)
                    generalNotes.Add("⚠️ Game version newer than v1.05 require additional settings to be enabled");
            }
            else
            {
                var needChanges = false;
                if (items["write_color_buffers"] == EnabledMark)
                {
                    notes.Add("⚠️ `Write Color Buffers` is not required");
                    needChanges = true;
                }
                if (items["read_color_buffer"] == EnabledMark)
                {
                    notes.Add("⚠️ `Read Color Buffer` is not required");
                    needChanges = true;
                }
                if (items["write_depth_buffers"] == EnabledMark)
                {
                    notes.Add("⚠️ `Write Depth Buffers` is not required");
                    needChanges = true;
                }
                if (items["read_depth_buffer"] == EnabledMark)
                {
                    notes.Add("⚠️ `Read Depth Buffer` is not required");
                    needChanges = true;
                }
                if (needChanges)
                    generalNotes.Add("⚠️ Game versions up to v1.05 do not require advanced settings");
            }
        }
        else
        {
            generalNotes.Add("⚠️ Game version newer than v1.05 require additional settings to be enabled");
        }
    }
        
    private static readonly HashSet<string> RatchetToDIds = new()
    {
        "BCAS20045", "BCES00052", "BCJS30014", "BCJS70004", "BCJS70012", "BCKS10054", "BCUS98127", "BCUS98153",
        "NPEA00452", "NPEA90017", "NPHA20002", "NPUA80965", "NPUA98153", 
    };

    private static readonly HashSet<string> Sly4Ids = new()
    {
        "BCES01284", "BCUS98247", "BCUS99142",
        "NPEA00429", "NPUA80875",
        "NPEA90120", "NPUA70250", // demos
        "NPUA30123", // soundtrack ???
    };

    private static void CheckSly4Settings(string serial, NameValueCollection items, List<string> notes)
    {
        if (!Sly4Ids.Contains(serial))
            return;

        if (items["resolution_scale"] is string resScale
            && int.TryParse(resScale, out var resScaling)
            && resScaling > 100
            && items["cpu_blit"] == DisabledMark)
        {
            notes.Add("⚠️ Proper resolution scaling requires `Force CPU Blit` to be `Enabled`");
        }
    }

    private static readonly HashSet<string> DragonsCrownIds = new()
    {
        "BCAS20290", "BCAS20298", "BLES01950", "BLJM61041", "BLUS30767",
        "NPEB01836", "NPUB31235",
    };

    private static void CheckDragonsCrownSettings(string serial, NameValueCollection items, List<string> notes)
    {
        if (!DragonsCrownIds.Contains(serial))
            return;

        if (items["spu_loop_detection"] == EnabledMark)
            notes.Add("⚠️ Please disable `SPU Loop Detection` for this game");
    }

    private static readonly HashSet<string> Lbp1Ids = new()
    {
        "BCAS20058", "BCAS20078", "BCAS20091", "BCES00611", "BCES00141", "BCJS70009", "BCKS10059", "BCUS98148", "BCUS98199", "BCUS98208",
        "NPEA00241", "NPHA80093", "NPUA80472", "NPUA80479",
    };

    private static readonly HashSet<string> Lbp2Ids = new()
    {
        "BCAS20201", "BCES00850", "BCES01086", "BCES01345", "BCES01346", "BCES01693", "BCES01694", "BCJS70024", "BCUS90260", "BCUS98249", "BCUS98372",
        "NPEA00324", "NPHA80161", "NPUA80662",
    };

    private static readonly HashSet<string> Lbp3Ids = new()
    {
        "BCAS20322", "BCES01663", "BCES02068", "BCUS98245", "BCUS98362",
        "NPEA00515", "NPHA80277", "NPUA81116",
    };

    private static readonly HashSet<string> AllLbpGames = new(Lbp1Ids.Concat(Lbp2Ids).Concat(Lbp3Ids))
    {
        "NPEA00147", "NPJA90074", "NPJA90097", "NPUA70045", // lbp1 demos and betas
        "NPUA70117", "NPHA80163", // lbp2 demo and beta
    };

    private static void CheckLbpSettings(string serial, NameValueCollection items, List<string> generalNotes)
    {
        if (!AllLbpGames.Contains(serial))
            return;

        /*
        if (items["gpu_info"] is string gpu
            && (gpu.Contains("RTX ") || gpu.Contains("GTX 16")))
            generalNotes.Add("⚠️ LittleBigPlanet games may fail to boot on nVidia Turing or newer GPUs ");
        */

        if (Lbp1Ids.Contains(serial))
        {
            if (items["game_version"] is string gameVer
                && Version.TryParse(gameVer, out var v))
            {
                if (v < new Version(1, 24))
                    generalNotes.Add("⚠️ Please update the game to prevent hang on boot");
            }
        }
    }

    private static void CheckVshSettings(NameValueCollection items, List<string> notes, List<string> generalNotes)
    {
        if (items["write_color_buffers"] is not null and not EnabledMark)
            notes.Add("ℹ️ `Write Color Buffers` should be enabled for proper visuals");
        if (items["cpu_blit"] is not null and not EnabledMark)
            notes.Add("ℹ️ `Force CPU Blit` should be enabled for proper visuals");
    }

    private static void CheckPs1ClassicsSettings(NameValueCollection items, List<string> notes, List<string> generalNotes)
    {
        if (items["spu_decoder"] is string spuDecoder
            && !spuDecoder.Contains("ASMJIT"))
            notes.Add("⚠️ Please set `SPU Decoder` to use `Recompiler (ASMJIT)`");
        if (items["cpu_blit"] == EnabledMark)
            notes.Add("ℹ️ Please disable `Force CPU Blit` for PS1 Classics");
        generalNotes.Add("ℹ️ PS1 Classics compatibility is subject to [official Sony emulator accuracy](https://www.psdevwiki.com/ps3/PS1_Classics_Emulator_Compatibility_List)");
    }
}
