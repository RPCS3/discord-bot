using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using DSharpPlus.Entities;

namespace CompatBot.Utils.ResultFormatters
{
    internal static partial class LogParserResult
    {
        private static void BuildWeirdSettingsSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(items["log_disabled_channels"]))
                notes.Add("❗ Some logging priorities were modified, please reset and upload a new log");
            if (!string.IsNullOrEmpty(items["resolution"]) && items["resolution"] != "1280x720")
                notes.Add("⚠ `Resolution` was changed from the recommended `1280x720`");
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
            if (items["vertex_cache"] == EnabledMark
                && items["serial"] is string serial
                && !KnownDisableVertexCacheIds.Contains(serial))
                notes.Add("⚠ `Vertex Cache` is disabled, please re-enable");
            if (items["cpu_blit"] is string cpuBlit && cpuBlit == EnabledMark &&
                items["write_color_buffers"] is string wcb && wcb == DisabledMark)
                notes.Add("⚠ `Force CPU Blit` is enabled, but `Write Color Buffers` is disabled");
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

            if (items["hle_lwmutex"] is string hleLwmutex && hleLwmutex == EnabledMark)
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
    }
}