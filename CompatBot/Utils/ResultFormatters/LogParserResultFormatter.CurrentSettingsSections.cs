using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using CompatApiClient.Utils;
using DSharpPlus.Entities;

namespace CompatBot.Utils.ResultFormatters;

internal static partial class LogParserResult
{
    private static void BuildInfoSection(DiscordEmbedBuilder builder, NameValueCollection items)
    {
        var systemInfo = items["build_and_specs"] ?? "";
        var valid = items["first_unicode_dot"] != null;
        if (!valid)
        {
            systemInfo = string.Join('\n', systemInfo.Split('\n', 4).Take(3)).Trim();
            items["log_from_ui"] = EnabledMark;
        }
        var idxStart = systemInfo.IndexOf('\0');
        if (idxStart > 0)
        {
            var idxEnd = systemInfo.IndexOf(" | ", idxStart);
            if (idxEnd > 0)
                systemInfo = systemInfo[..idxStart] + systemInfo[idxEnd..];
        }
        var sysInfoParts = systemInfo.Split(NewLineChars, StringSplitOptions.RemoveEmptyEntries);
        var buildInfo = sysInfoParts.Length > 0 ? BuildInfoInLog.Match(sysInfoParts[0]) : BuildInfoInLog.Match(systemInfo);
        var cpuInfo = sysInfoParts.Length > 1 ? CpuInfoInLog.Match(sysInfoParts[1]) : CpuInfoInLog.Match(systemInfo);
        var osInfo = sysInfoParts.Length > 2 ? OsInfoInLog.Match(sysInfoParts[2]) : OsInfoInLog.Match(systemInfo);
        if (buildInfo.Success)
        {
            items["build_version"] = buildInfo.Groups["version"].Value.Trim();
            items["build_number"] = buildInfo.Groups["build"].Value.Trim();
            items["build_full_version"] = $"{items["build_version"]}.{items["build_number"]}";
            items["build_commit"] = buildInfo.Groups["commit"].Value.Trim();
            items["build_branch"] = buildInfo.Groups["branch"].Value.Trim();
            var fwVersion = buildInfo.Groups["fw_version_installed"].Value;
            if (!string.IsNullOrEmpty(fwVersion))
                items["fw_version_installed"] = fwVersion;
        }
        if (cpuInfo.Success)
        {
            var cpuModel = cpuInfo.Groups["cpu_model"].Value.StripMarks()
                .Replace(" CPU", "")
                .Replace("AMD FX -", "AMD FX-")
                .Trim();
            if (cpuModel.StartsWith("DG1", StringComparison.OrdinalIgnoreCase))
            {
                cpuModel = cpuModel[3] switch
                {
                    '0' => "AMD APU for PlayStation 4",     // DG1000FGF84HT
                    '1' => "AMD APU for PlayStation 4",     // DG1101SKF84HV
                    '2' => "AMD APU for PlayStation 4 Pro", // DG1201SLF87HW
                    '3' => "AMD APU for PlayStation 4 Pro", // DG1301SML87HY
                    '4' => "AMD APU for PlayStation 4 Slim",// DG1401SNF87ID 
                    _ => "AMD APU for PlayStation?",
                };
            }
            else if (cpuModel.Equals("VirtualApple", StringComparison.OrdinalIgnoreCase))
                cpuModel = items["gpu_name"] is string appleGpu && appleGpu.StartsWith("Apple M", StringComparison.OrdinalIgnoreCase) ? appleGpu : "Apple Mx";
            items["cpu_model"] = cpuModel;
            items["thread_count"] = cpuInfo.Groups["thread_count"].Value;
            items["memory_amount"] = cpuInfo.Groups["memory_amount"].Value;
            items["cpu_tsc"] = cpuInfo.Groups["tsc"].Value;
            items["cpu_extensions"] = cpuInfo.Groups["cpu_extensions"].Value;
        }
        if (osInfo.Success)
        {
            switch (osInfo.Groups["os_type"].Value.ToLowerInvariant())
            {
                case "windows":
                {
                    items["os_type"] = "Windows";
                    items["os_version"] = $"{osInfo.Groups["os_version_major"].Value}.{osInfo.Groups["os_version_minor"].Value}.{osInfo.Groups["os_version_build"].Value}";
                    if (Version.TryParse(items["os_version"], out var winVersion) && GetWindowsVersion(winVersion) is string winVer)
                        items["os_windows_version"] = winVer;
                    break;
                }
                case "posix":
                {
                    items["os_type"] = osInfo.Groups["posix_name"].Value;
                    items["os_version"] = osInfo.Groups["posix_release"].Value;
                    items["os_linux_version"] = GetLinuxVersion(items["os_type"], items["os_version"], osInfo.Groups["posix_version"].Value);
                    break;
                }
                case "macos":
                {
                    items["os_type"] = "MacOS";
                    items["os_version"] = osInfo.Groups["macos_version"].Value;
                    if (Version.TryParse(items["os_version"], out var macVer) && GetMacOsVersion(macVer) is string macOsVer)
                        items["os_mac_version"] = macOsVer; 
                    break;
                }
            }
        }
        else if (items["os_version_major"] is not null || items["posix_name"] is not null)
        {
            switch (items["os_type"]?.ToLowerInvariant())
            {
                case "windows":
                {
                    items["os_type"] = "Windows";
                    items["os_version"] = $"{items["os_version_major"]}.{items["os_version_minor"]}.{items["os_version_build"]}";
                    if (Version.TryParse(items["os_version"], out var winVersion) && GetWindowsVersion(winVersion) is string winVer)
                        items["os_windows_version"] = winVer;
                    break;
                }
                case "posix":
                {
                    items["os_type"] = items["posix_name"];
                    items["os_version"] = items["posix_release"];
                    items["os_linux_version"] = GetLinuxVersion(items["posix_name"], items["os_version"], osInfo.Groups["posix_version"].Value);
                    break;
                }
            }
        }
        if (buildInfo.Success)
        {
            systemInfo = $"RPCS3 v{buildInfo.Groups["version_string"].Value} {buildInfo.Groups["stage"].Value}";
            if (!string.IsNullOrEmpty(buildInfo.Groups["branch"].Value))
                systemInfo += " | " + buildInfo.Groups["branch"].Value;
            if (!string.IsNullOrEmpty(items["fw_version_installed"]))
                systemInfo += " | FW " + items["fw_version_installed"];
            if (!string.IsNullOrEmpty(items["os_type"]))
            {
                systemInfo += " | ";
                if (items["os_windows_version"] is string winVer)
                    systemInfo += "Windows " + winVer;
                else if (items["os_linux_version"] is string linVer)
                    systemInfo += linVer;
                else if (items["os_mac_version"] is string macVer)
                    systemInfo += $"{macVer} {items["os_version"]}";
                else
                {
                    systemInfo += items["os_type"];
                    if (!string.IsNullOrEmpty(items["os_version"]))
                        systemInfo += " " + items["os_version"];
                }
            }
            var threadCountItem = items["thread_count"]?.Trim();
            systemInfo += $"{Environment.NewLine}{items["cpu_model"]} | {threadCountItem} Thread{(threadCountItem == "1" ? "" : "s")} | {items["memory_amount"]} GiB RAM";
            if (!string.IsNullOrEmpty(items["cpu_extensions"]))
                systemInfo += " | " + items["cpu_extensions"];
        }
        if (items["gpu_info"] is string gpu)
            systemInfo += $"{Environment.NewLine}GPU: {gpu}";
        else if (items["gpu_available_info"] is string availableGpus)
        {
            var multiple = availableGpus.Contains(Environment.NewLine);
            systemInfo += $"{Environment.NewLine}GPU{(multiple ? "s" : "")}:{(multiple ? Environment.NewLine : " ")}{availableGpus}";
        }
        builder.AddField("Build Info", systemInfo.Trim(EmbedPager.MaxFieldLength));
    }

    private const int ColumnWidth = 30;

    private static (string? name, List<string>? lines) BuildCpuSection(NameValueCollection items)
    {
        if (string.IsNullOrEmpty(items["ppu_decoder"]))
            return (null, null);
        var lines = new List<string>
        {
            $"PPU Decoder:{items["ppu_decoder"],ColumnWidth-11}",
            $"SPU Decoder:{items["spu_decoder"],ColumnWidth-11}",
            //$"SPU Lower Thread Priority:{items["spu_lower_thread_priority"],ColumnWidth-25}",
            $"SPU Loop Detection:{items["spu_loop_detection"],ColumnWidth-18}",
            $"Thread Scheduler:{items["thread_scheduler"],ColumnWidth-16}",
            $"SPU Threads:{items["spu_threads"],ColumnWidth-11}",
            $"SPU Block Size:{items["spu_block_size"] ?? "N/A",ColumnWidth-14}",
            $"Accurate xfloat:{items["accurate_xfloat"] ?? "N/A",ColumnWidth-15}",
            $"Force CPU Blit:{items["cpu_blit"] ?? "N/A",ColumnWidth-14}",
            //$"Lib Mode:{items["lib_loader"],ColumnWidth-8}",
        };
        return ("CPU Settings", lines);
    }

    private static (string? name, List<string>? lines) BuildGpuSection(NameValueCollection items)
    {
        if (string.IsNullOrEmpty(items["renderer"]))
            return (null, null);

        var enabledBuffers = (items["read_color_buffers"], items["write_color_buffers"], items["read_depth_buffer"], items["write_depth_buffer"]) switch
        {
            (EnabledMark, EnabledMark, EnabledMark, EnabledMark) => "RWCB+RWDB",
            (EnabledMark, EnabledMark, EnabledMark,           _) => "RWCB+RDB",
            (EnabledMark, EnabledMark,           _, EnabledMark) => "RWCB+WDB",
            (EnabledMark, EnabledMark,           _,           _) => "RWCB",

            (EnabledMark,           _, EnabledMark, EnabledMark) => "RCB+RWDB",
            (EnabledMark,           _, EnabledMark,           _) => "RCB+RDB",
            (EnabledMark,           _,           _, EnabledMark) => "RCB+WDB",
            (EnabledMark,           _,           _,           _) => "RCB",

            (          _, EnabledMark, EnabledMark, EnabledMark) => "WCB+RWDB",
            (          _, EnabledMark, EnabledMark,           _) => "WCB+RDB",
            (          _, EnabledMark,           _, EnabledMark) => "WCB+WDB",
            (          _, EnabledMark,           _,           _) => "WCB",

            (          _,           _, EnabledMark, EnabledMark) => "RWDB",
            (          _,           _, EnabledMark,           _) => "RDB",
            (          _,           _,           _, EnabledMark) => "WDB",
            _                                                    => "None",
        };

        var lines = new List<string>
        {
            $"Renderer:{items["renderer"],ColumnWidth-8}",
            $"Resolution:{items["resolution"],ColumnWidth-10}",
            $"Resolution Scale:{items["resolution_scale"] ?? "N/A",ColumnWidth-16}",
            $"Res Scale Threshold:{items["texture_scale_threshold"] ?? "N/A",ColumnWidth-19}",
            //$"Anti-Aliasing:{items["msaa"] ?? "N/A",ColumnWidth-13}",
            $"Anisotropic Filter:{items["af_override"] ?? "N/A",ColumnWidth-18}",
            $"RSX Buffers:{enabledBuffers,ColumnWidth-11}",
            $"Shader Mode:{items["shader_mode"],ColumnWidth-11}",
            $"ZCull:{items["zcull_status"],ColumnWidth-5}",
            $"Frame Limit:{items["frame_limit_combined"],ColumnWidth-11}",
        };
        return ("GPU Settings", lines);
    }

    private static void BuildSettingsSections(DiscordEmbedBuilder builder, NameValueCollection items, (string? name, List<string>? lines) colA, (string? name, List<string>? lines) colB)
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
                foreach (var t in tmp)
                    if (!t.EndsWith("N/A") || linesToSkip-- > 0)
                        colA.lines.Add(t);

                linesToSkip = colBToRemove - linesToRemove;
                tmp = colB.lines;
                colB.lines = new List<string>(tmp.Count - linesToRemove);
                for (var i = 0; i < tmp.Count; i++)
                    if (!tmp[i].EndsWith("N/A") || linesToSkip-- > 0)
                        colB.lines.Add(tmp[i]);
            }
            AddSettingsSection(builder, colA.name!, colA.lines, isCustomSettings);
            AddSettingsSection(builder, colB.name!, colB.lines, isCustomSettings);
        }
    }

    private static void AddSettingsSection(DiscordEmbedBuilder builder, string name, List<string> lines, bool isCustomSettings)
    {
        var result = new StringBuilder();
        foreach (var line in lines)
            result.Append('`').Append(line).AppendLine("`");
        if (isCustomSettings)
            name = "Per-game " + name;
        else
            name = "Global " + name;
        builder.AddField(name, result.ToString().FixSpaces(), true);
    }

    private static void BuildLibsSection(DiscordEmbedBuilder builder, NameValueCollection items)
    {
        if (items["lib_loader"] is string libs
            && (libs.Contains("manual", StringComparison.InvariantCultureIgnoreCase)
                || libs.Contains("strict", StringComparison.InvariantCultureIgnoreCase)))
            builder.AddField("Selected Libraries", items["library_list"]?.Trim(1024));
        if (items["library_list_lle"] is string lle && lle != "None")
            builder.AddField("LLE Library Override", lle.Trim(1024));
        if (items["library_list_hle"] is string hle && hle != "None")
            builder.AddField("HLE Library Override", hle.Trim(1024));
    }
}