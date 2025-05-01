using System.Collections.Specialized;
using System.Globalization;
using System.Text.RegularExpressions;
using CompatApiClient.Utils;
using CompatBot.EventHandlers.LogParsing.POCOs;

namespace CompatBot.Utils.ResultFormatters;

internal static partial class LogParserResult
{
    [GeneratedRegex(@"Radeon RX 5\d{3}", RegexOptions.IgnoreCase)]
    private static partial Regex RadeonRx5xxPattern();
    
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
        if (TryGetRpcs3Version(items, out var buildVersion)
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
        var isAppleGpu = items["gpu_info"] is string gpuInfoApple && gpuInfoApple.Contains("Apple", StringComparison.OrdinalIgnoreCase);
        var canUseRelaxedZcull = items["renderer"] is not "Vulkan" || multiItems["vk_ext"].Contains("VK_EXT_depth_range_unrestricted");
        if (items["llvm_arch"] is string llvmArch)
            notes.Add($"❔ LLVM target CPU architecture override is set to `{llvmArch.Sanitize(replaceBackTicks: true)}`");
        if (items["renderer"] is "D3D12")
            notes.Add("💢 Do **not** use DX12 renderer");
        if (items["renderer"] is "OpenGL"
            && items["supported_gpu"] is EnabledMark
            && !GowHDIds.Contains(serial))
            notes.Add("⚠️ `Vulkan` is the recommended `Renderer`");
        if (items["renderer"] is "Vulkan")
        {
            if (items["supported_gpu"] is DisabledMark)
                notes.Add("❌ Selected `Vulkan` device is not supported, please use `OpenGL` instead");
        }
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
                                     && RadeonRx5xxPattern().IsMatch(gpuInfo) // RX 590 is a thing 😔
                                     && !gpuInfo.Contains("RADV");
        if (items["msaa"] is "Disabled")
        {
            if (!isWireframeBugPossible && !isAppleGpu)
                notes.Add("ℹ️ `Anti-aliasing` is disabled, which may result in visual artifacts");
        }
        else if (items["msaa"] is not null)
        {
            if (isAppleGpu)
                notes.Add("⚠️ `Anti-aliasing` is not supported for Apple GPUs, please disable");
            else if (isWireframeBugPossible)
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
            if (isAppleGpu)
            {
                notes.Add("⚠️ `Async Texture Streaming` is not supported on Apple GPUs");
            }
            else
            {
                if (items["async_queue_scheduler"] == "Device")
                    notes.Add("⚠️ If you experience visual artifacts, try setting `Async Queue Scheduler` to use `Host`");
                notes.Add("⚠️ If you experience visual artifacts, try disabling `Async Texture Streaming`");
            }
        }
            
        if (items["ppu_decoder"] is string ppuDecoder)
        {
            if (KnownGamesThatRequireInterpreter.Contains(serial))
            {
                if (ppuDecoder.Contains("Recompiler", StringComparison.InvariantCultureIgnoreCase))
                    notes.Add("⚠️ This game requires `PPU Decoder` to use `Interpreter (static)`");
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

        if (KnownGamesThatRequireAccurateXfloat.Contains(serial) && items["xfloat_mode"] is not "Accurate")
            notes.Add("⚠️ `Accurate xfloat` is required for this game");
        else if (items["xfloat_mode"] is "Accurate" && !KnownGamesThatRequireAccurateXfloat.Contains(serial))
            notes.Add("⚠️ `Accurate xfloat` is not required, and significantly impacts performance");
        else if (items["xfloat_mode"] is "Relaxed" or "Inaccurate" && !KnownNoApproximateXFloatIds.Contains(serial))
            notes.Add("⚠️ `Approximate xfloat` is disabled, please enable");
        else if (items["xfloat_mode"] is "Inaccurate" && !KnownNoRelaxedXFloatIds.Contains(serial))
            notes.Add("⚠️ `Relaxed xfloat` is disabled, please enable");
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

        if (items["zcull_status"] is not null and not "Full" && !canUseRelaxedZcull)
            notes.Add("⚠️ This GPU does not support `VK_EXT_depth_range_unrestricted` extension, please disable `Relaxed ZCull Sync`");
        else if (items["zcull_status"] is "Disabled")
            notes.Add("⚠️ `ZCull Occlusion Queries` is disabled, which can result in visual artifacts");
        else if (items["relaxed_zcull"] is string relaxedZcull)
        {
            if (relaxedZcull is EnabledMark
                && !KnownGamesThatWorkWithRelaxedZcull.Contains(serial))
            {
                notes.Add("ℹ️ `Relaxed ZCull Sync` is enabled and can cause performance and visual issues");
            }
            else if (relaxedZcull is DisabledMark
                     && KnownGamesThatWorkWithRelaxedZcull.Contains(serial)
                     && canUseRelaxedZcull)
            {
                notes.Add("ℹ️ Enabling `Relaxed ZCull Sync` for this game may improve performance");
            }
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
                var weirdModules = lleLibList.Split(',', StringSplitOptions.TrimEntries).Except(["libvdec.sprx"]).ToArray();
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
            CheckSimpsonsSettings(serial, items, generalNotes, ppuPatches, patchNames);
            CheckNierSettings(serial, items, notes, ppuPatches, ppuHashes, generalNotes);
            CheckDod3Settings(serial, items, notes, ppuPatches, ppuHashes, generalNotes);
            CheckScottPilgrimSettings(serial, items, notes, generalNotes);
            CheckGoWSettings(serial, items, notes, generalNotes);
            CheckDesSettings(serial, items, notes, ppuPatches, ppuHashes, generalNotes);
            CheckTlouSettings(serial, items, notes, ppuPatches, ppuHashes, patchNames);
            CheckRdrSettings(serial, items, notes);
            CheckMgs4Settings(serial, items, ppuPatches, ppuHashes, generalNotes);
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
            if (isAppleGpu && af is "Auto")
                notes.Add("⚠️ `Anisotropic Filter` override is not supported on Apple GPUs, please use `Auto`");
            else if (af is "Disabled")
                notes.Add("❌ `Anisotropic Filter` is `Disabled`, please use `Auto` instead");
            else if (af is not "auto" and not "16")
                notes.Add($"❔ `Anisotropic Filter` is set to `{af}x`, which makes little sense over `16x` or `Auto`");
        }

        if (items["shader_mode"]?.Contains("Interpreter") is true && isAppleGpu)
            notes.Add("⚠️ Interpreter `Shader Mode` is not supported on Apple GPUs, please use Async-only option");
        else if (items["shader_mode"] == "Interpreter only")
            notes.Add("⚠️ `Shader Interpreter Only` mode is not accurate and very demanding");
        else if (items["shader_mode"]?.StartsWith("Async") is false && !isAppleGpu)
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
            if (isAppleGpu)
                notes.Add("⚠️ `Multithreaded RSX` is not supported for Apple GPUs");
            else if (multiItems["fatal_error"].Any(f => f.Contains("VK_ERROR_OUT_OF_POOL_MEMORY_KHR")))
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
                if (items["os_type"] is "Windows" && !audioBackend.Equals("XAudio2", StringComparison.OrdinalIgnoreCase))
                    notes.Add("⚠️ Please use `XAudio2` as the audio backend for this build");
                else if (items["os_type"] == "Linux"
                         && !audioBackend.Equals("OpenAL", StringComparison.OrdinalIgnoreCase)
                         && !audioBackend.Equals("FAudio", StringComparison.OrdinalIgnoreCase))
                    notes.Add("ℹ️ `FAudio` and `OpenAL` are the recommended audio backends for this build");
            }
            else
            {
                if (items["os_type"] is "Windows" or "Linux"
                    && !audioBackend.Equals("Cubeb", StringComparison.OrdinalIgnoreCase)
                    && !audioBackend.Equals("XAudio2", StringComparison.OrdinalIgnoreCase))
                    notes.Add("⚠️ Please use `Cubeb` as the audio backend");
            }
            if (audioBackend.Equals("null", StringComparison.OrdinalIgnoreCase))
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

    private static readonly HashSet<string> P5Ids =
    [
        "BLES02247", "BLUS31604", "BLJM61346",
        "NPEB02436", "NPUB31848", "NPJB00769",
    ];

        
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

    private static readonly HashSet<string> AllStarBattleIds =
    [
        "BLES01986", "BLUS31405", "BLJS10217",
        "NPEB01922", "NPUB31391", "NPJB00331",
    ];

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

    private static void CheckSimpsonsSettings(string serial, NameValueCollection items, List<string> generalNotes, Dictionary<string, int> ppuPatches, UniqueList<string> patchNames)
    {
        if (serial is not ("BLES00142" or "BLUS30065"))
            return;

        var hasPatch = ppuPatches.Any() && patchNames.Any(n => n.Contains("Fix pad initialization", StringComparison.OrdinalIgnoreCase));
        if ((!TryGetRpcs3Version(items, out var v) || v < FixedSimpsonsBuild)
            && !hasPatch)
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

    private static readonly HashSet<string> Gow3Ids =
    [
        "BCAS25003", "BCES00510", "BCES00516", "BCES00799", "BCJS37001", "BCUS98111", "BCKS15003",
    ];

    private static readonly HashSet<string> GowHDIds =
    [
        "BCAS20102", "BCES00791", "BCES00800", "BLJM60200", "BCUS98229", // collection except volume II
        "NPUA80491", "NPUA80490", "NPEA00255", "NPEA00256", "NPJA00062", "NPJA00061", "NPJA00066",
    ];

    private static readonly HashSet<string> GowAscIds =
    [
        "BCAS25016", "BCES01741", "BCES01742", "BCUS98232",
        "NPEA00445", "NPEA90123", "NPUA70216", "NPUA70269", "NPUA80918",
        "NPHA80258",
    ];

    private static void CheckGoWSettings(string serial, NameValueCollection items, List<string> notes, List<string> generalNotes)
    {
        if (serial == "NPUA70080") // GoW3 Demo
            return;

        if (Gow3Ids.Contains(serial))
            generalNotes.Add("ℹ️ Black screen after Santa Monica logo is fine for up to 5 minutes");
        else if (GowAscIds.Contains(serial))
            generalNotes.Add("ℹ️ This game is known to be very unstable");
    }

    private static readonly HashSet<string> DesIds =
    [
        "BLES00932", "BLUS30443", "BCJS30022", "BCAS20071",
        "NPEB01202", "NPUB30910", "NPJA00102",
        "BLUD80018", // trade demo
    ];

    private static readonly HashSet<string> KnownDesPatches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "83681f6110d33442329073b72b8dc88a2f677172", // BLUS30443 1.00
        "5446a2645880eefa75f7e374abd6b7818511e2ef", // BLES00932 1.00
        "9403fe1678487def5d7f3c380b4c4fb275035378", // BCAS20071 1.04
        "f965a746d844cd0c572a7e8731b5b3b7a81f7bdd", // BLUD80018 1.01
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

    private static readonly HashSet<string> Dod3Ids =
    [
        "BLUS31197", "NPUB31251",
        "NPEB01407",
        "BLJM61043", "NPJB00380",
        "BCAS20311", "NPHB00633", "NPHB00639",
    ];

    private static readonly HashSet<string> KnownDod3Patches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "2b393f064786e5895d5a576621deb4c9107a8f0b", // BLUS31197 1.00
        "f2f7f7ea0444353884bb715152147c3a29f4e790", // BLUS31197 1.01
        "b18834a8f21cd29a091b287a66656a279ccba507", // NPUB31251 1.00
        "9c04f427625a0064282432e4edfefe9e0956c303", // NPUB31251 1.01
        "e1a44e5d3fb03a37f0445e92ed13abce8d6efdd4", // NPEB01407
        "a017576369165f3746730724c8ae762ed9bc64d8", // BLJM61043 1.00
        "eda0339b931f6fe15420b053703ddd89b27d615b", // BLJM61043 1.01
        "62eb0f5d8f0f929cb23309311b89ce21eaa3bc9e", // BLJM61043 1.02
        "384a28c62ff179a4ae815ab7b711e76fbb1167b4", // BLJM61043 1.03
        "c09c496514f6dc591434575b04eb7c003826c11d", // BLJM61043 1.04
        "56cc988f7d5b5127049f28ed9278b98de2e4ff1f", // BCAS20311 1.01
        "ac64494f4ea31f8b0f82584c48916d30dad16300", // BCAS20311 1.02
        "20183817f17fb358d28131e195c5af1fc9579ada", // NPHB00633 1.00
        "def0c4b28e5c35da73fcc07731ec0cc3d7fe9485", // NPHB00633 1.01
        "8342766aab0791f480d0a6f8984cc5c199455c64", // NPHB00633 1.02
        // missing NPJB00380, NPHB00639
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

    private static readonly HashSet<string> TlouIds =
    [
        "BCAS20270", "BCES01584", "BCES01585", "BCJS37010", "BCUS98174",
        "NPEA00435", "NPJA00096", "NPHA80243", "NPUA80960",
        "NPEA00521", "NPJA00129", "NPHA80279", "NPUA81175", // left behind
        "NPEA90122", "NPHA80246", "NPUA70257", // demos
        "NPEA00454", "NPUA30134", "NPEA00517", // soundtrack
        "NPJM00012", // bonus video
        "NPUO30130", // manual
    ];

    private static readonly HashSet<string> KnownTlouPatches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "9df60dc1aa5005a0c80e9066e4951dc0471553e6", // 1.00
        "120fb71f7352d62521c639b0e99f960018c10a56", // 1.11
        "9e5c67eaf69077c591b7c503bed2b48617643134", // 1.00 left behind
        "e71d82fa70d09b7df9493f0336717dd9a4977216", // demo
        "4c92d2f16d69b0c43c941279b59d124c16952ac0", // NPJM00012
        "2a02b9850cacca089914273ed0e76bfc2edebcc6", // NPEA00454
    };

    private static void CheckTlouSettings(string serial, NameValueCollection items, List<string> notes, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes, UniqueList<string> patchNames)
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

        if (TryGetRpcs3Version(items, out var buildVersion)
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

    private static readonly HashSet<string> Killzone3Ids =
    [
        "BCAS20157", "BCAS25008", "BCES01007", "BCJS30066", "BCJS37003", "BCJS70016", "BCJS75002", "BCUS98234",
        "NPEA00321", "NPEA90084", "NPEA90085", "NPEA90086", "NPHA80140", "NPJA90178", "NPUA70133",
    ];

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
    private static readonly HashSet<string> RdrIds =
    [
        "BLAS50296", "BLES00680", "BLES01179", "BLES01294", "BLUS30418", "BLUS30711", "BLUS30758",
        "BLJM60314", "BLJM60403", "BLJM61181", "BLKS20315",
        "NPEB00833", "NPHB00465", "NPHB00466", "NPUB30638", "NPUB30639",
        "NPUB50139", // max payne 3 / rdr bundle???
    ];

    private static void CheckRdrSettings(string serial, NameValueCollection items, List<string> notes)
    {
        if (!RdrIds.Contains(serial))
            return;

        if (items["write_color_buffers"] == DisabledMark)
            notes.Add("ℹ️ `Write Color Buffers` is required for proper visuals at night");
    }

    private static readonly HashSet<string> Mgs4Ids =
    [
        "BLAS55005", "BLES00246", "BLJM57001", "BLJM67001", "BLKS25001", "BLUS30109", "BLUS30148",
        "NPEB02182", "NPJB00698", "NPUB31633",
        "NPEB90116", "NPHB00065", "NPHB00067", "NPJB90149", "NPUB90176", // demos
        "NPEB00027", "NPJB90113", "NPUB90126", // database
    ];

    private static readonly HashSet<string> KnownMgs4Patches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "6e1a0a58a43ad437488e88e402e9ac16a0b23caa", // BLES00246 / BLUS30109 1.00 eboot
        "9712144d93487f0b62e39f55e175af783b58af72", // BLUS30109 1.00
        "33e09a0bd8fa2a3b28780a3feeb7b0e018bae381", // BLUS30109 2.00
        "a79a75426fc84a407265f91f8818681c864231b9", // BLES00246 1.00
        "c937999ea44fb6260455b85c9f25eea55b1208b9", // BLES00246 2.00
        "0d33f5054f70a738799bd5da86d8baa10635f623", // BLJM67001 2.00 eboot
        "7ddd13b8a7e9ff386659bfbccd183dc7f7f701f4", // BLJM67001 1.00
        "c19b3b57017488d9bd126a4b6cfba3566e0905fb", // BLJM67001 2.00
        "6886ae8f4270fe3fa4c7cf4299307044ad4ce989", // NPUB31633 / NPEB02182 eboot
        "bbf4c85f1c01e182e7f96d34f734772c4430a426", // NPUB31633
        "347d16fbdb0a12f1083c0fb98343c4642d4641cb", // NPEB02182
        "2b65154021bb8c3b25616324975af795720c5f78", // NPJB00698
        "044d68440c37065c5248a3a08a0e6da6082435df", // NPEB00027
        "3685929dd4a6e62ff8a61d43871c6b4714a76136", // NPUB90126
        "c09e68e24682720027d194ef4f6dd067dc2e0908", // NPJB90113
        "07cb711984e305c27108848d5f6579d4dd7f6c47", // NPEB90116
        "7efe5774b7325c8346539489b41cb132857af1f7", // NPJB90149
        "30bdcdc31c75c737b8e699c0f22516488a7a50c0", // NPHB00065
        "158a56cf4bdad65fa6e01a338a25500d6953cd68", // NPHB00067
        "fb73182c2590843c6c5bc39c9292d887716006e7", // NPUB90176
    };

    private static void CheckMgs4Settings(string serial, NameValueCollection items, Dictionary<string, int> ppuPatches, HashSet<string> ppuHashes, List<string> generalNotes)
    {
        if (!Mgs4Ids.Contains(serial))
            return;

        if (items["build_branch"] == "mgs4")
        {
            //notes.Clear();
            generalNotes.Add("⚠️ Custom RPCS3 builds are not officially supported");
            generalNotes.Add("⚠️ This custom build comes with pre-configured settings, don't change anything");
        }
        if (ppuHashes.Overlaps(KnownMgs4Patches))
            generalNotes.Add("ℹ️ This game has an FPS unlock patch");
         else if (ppuHashes.Any())
            generalNotes.Add("🤔 Very interesting version of the game you got there");
    }

    private static readonly HashSet<string> PdfIds =
    [
        "BLJM60527", "BLUS31319", "BLAS50576",
        "NPEB01393", "NPUB31241", "NPHB00559", "NPJB00287",
    ];


    private static readonly HashSet<string> KnownPdfPatches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "f3227f57ec001582b253035fd90de77f05ead470",
        "c02e3b52e3d75f52f76fb8f0fb5be7ca4d921949",
        "1105af0a4d6a4a1481930c6f3090c476cde06c4c",
    };

    private static readonly HashSet<string> Pdf2ndIds =
    [
        "BCAS50693", "BLAS50693", "BLES02029", "BLJM61079",
        "NPUB31488", "NPHB00671", "NPHB00662", "NPEB02013", "NPJB00435",
    ];

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

    private static readonly HashSet<string> Gt5Ids =
    [
        "BCAS20108", "BCAS20151", "BCAS20154", "BCAS20164", "BCAS20229", "BCAS20267",
        "BCES00569",
        "BCJS30001", "BCJS30050", "BCJS30100",
        "BCUS98114", "BCUS98272", "BCUS98394",
        "NPEA90052", "NPHA80080", "NPUA70087", // time trial
        "NPUA70115", // kiosk demo
    ];

    private static readonly HashSet<string> KnownGt5Patches = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "5eb226d8430cf943cca1344fcf0c76db15aaaeb7",
        "9216b03cf8f4ff27a57ff44ede2bc43a9d3087c0",
        "ef552ab6594271862d9c6ab62e982c92380ad6cd",
        "223cc85fc80a6667fae775c7c02f7f65e6b2871f",
        "d73f342bf28ee016ef3d0ccb309b1acb03d8ecce",
        "a5e547ce3ce25092ac6cae85631f50ba5d9ea914",
        "7a5ee7bc2fef9566dd80e35893fe2c5571197726",
        "0f0c629a7365cd77974a0ff48b734f98a43785cd", // NPUA70115
        "a2df55fc8f07504eb44a5ba3c8db056ca93bb3e9", // NPEA90052
        "582c850bac9b5f92ed94ab9f8a58fc5474eff6c9", // NPUA70087
        "6386fe7c2a3b97d05fbe58473e595c6223382c0b", // NPHA80080
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

    private static readonly HashSet<string> Gt6Ids =
    [
        "BCAS20519", "BCAS20520", "BCAS20521", "BCAS25018", "BCAS25019",
        "BCES01893", "BCES01905", "BCJS37016", "BCUS98296", "BCUS99247",
        "NPEA00502", "NPJA00113", "NPUA81049",
    ];

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
        
    private static readonly HashSet<string> RatchetToDIds =
    [
        "BCAS20045", "BCES00052", "BCJS30014", "BCJS70004", "BCJS70012", "BCKS10054", "BCUS98127", "BCUS98153",
        "NPEA00452", "NPEA90017", "NPHA20002", "NPUA80965", "NPUA98153",
    ];

    private static readonly HashSet<string> Sly4Ids =
    [
        "BCES01284", "BCUS98247", "BCUS99142",
        "NPEA00429", "NPUA80875",
        "NPEA90120", "NPUA70250", // demos
        "NPUA30123", // soundtrack ???
    ];

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

    private static readonly HashSet<string> DragonsCrownIds =
    [
        "BCAS20290", "BCAS20298", "BLES01950", "BLJM61041", "BLUS30767",
        "NPEB01836", "NPUB31235",
    ];

    private static void CheckDragonsCrownSettings(string serial, NameValueCollection items, List<string> notes)
    {
        if (!DragonsCrownIds.Contains(serial))
            return;

        if (items["spu_loop_detection"] == EnabledMark)
            notes.Add("⚠️ Please disable `SPU Loop Detection` for this game");
    }

    private static readonly HashSet<string> Lbp1Ids =
    [
        "BCAS20058", "BCAS20078", "BCAS20091", "BCES00611", "BCES00141", "BCJS70009", "BCKS10059", "BCUS98148",
        "BCUS98199", "BCUS98208",
        "NPEA00241", "NPHA80093", "NPUA80472", "NPUA80479",
    ];

    private static readonly HashSet<string> Lbp2Ids =
    [
        "BCAS20201", "BCES00850", "BCES01086", "BCES01345", "BCES01346", "BCES01693", "BCES01694", "BCJS70024",
        "BCUS90260", "BCUS98249", "BCUS98372",
        "NPEA00324", "NPHA80161", "NPUA80662",
    ];

    private static readonly HashSet<string> Lbp3Ids =
    [
        "BCAS20322", "BCES01663", "BCES02068", "BCUS98245", "BCUS98362",
        "NPEA00515", "NPHA80277", "NPUA81116",
    ];

    private static readonly HashSet<string> AllLbpGames =
    [
        ..Lbp1Ids, ..Lbp2Ids, ..Lbp3Ids,
        "NPEA00147", "NPJA90074", "NPJA90097",
        "NPUA70045", // lbp1 demos and betas
        "NPUA70117", "NPHA80163", // lbp2 demo and beta
    ];

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
