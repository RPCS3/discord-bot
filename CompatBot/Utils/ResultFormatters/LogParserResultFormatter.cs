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

namespace CompatBot.Utils.ResultFormatters
{
    internal static class LogParserResult
    {
        private static readonly Client compatClient = new Client();

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

        private const string TrueMark = "[x]";
        private const string FalseMark = "[ ]";

        public static async Task<DiscordEmbed> AsEmbedAsync(this LogParseState state, DiscordClient client, DiscordMessage message)
        {
            DiscordEmbedBuilder builder;
            var collection = state.CompleteCollection ?? state.WipCollection;
            if (collection?.Count > 0)
            {
                var gameInfo = await client.LookupGameInfoAsync(collection["serial"], collection["game_title"], true).ConfigureAwait(false);
                builder = new DiscordEmbedBuilder(gameInfo) {ThumbnailUrl = null}; // or this will fuck up all formatting
                if (state.Error == LogParseState.ErrorCode.PiracyDetected)
                {
                    state.PiracyContext = state.PiracyContext.Sanitize();
                    var msg = $"{message.Author.Mention}, you are being denied further support until you legally dump the game.\n" +
                              "Please note that the RPCS3 community and its developers do not support piracy.\n" +
                              "Most of the issues with pirated dumps occur due to them having been tampered with in some way " +
                              "and therefore act unpredictably on RPCS3.\n" +
                              "If you need help obtaining legal dumps, please read <https://rpcs3.net/quickstart>";
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
                    await BuildNotesSectionAsync(builder, state, collection).ConfigureAwait(false);
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
            else
                items["gpu_info"] = "Unknown";

            items["driver_version_info"] = GetOpenglDriverVersion(items["gpu_info"], (items["driver_version_new"] ?? items["driver_version"])) ??
                                           GetVulkanDriverVersion(items["vulkan_initialized_device"], items["vulkan_found_device"]) ??
                                           GetVulkanDriverVersionRaw(items["gpu_info"], items["vulkan_driver_version_raw"]);
            if (items["driver_version_info"] != null)
                items["gpu_info"] += $" ({items["driver_version_info"]})";
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
                items[key] = value.Sanitize();
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
            builder.AddField("Build Info", systemInfo);
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
            var notes = new StringBuilder();
            if (!string.IsNullOrEmpty(items["resolution"]) && items["resolution"] != "1280x720")
                notes.AppendLine("`Resolution` was changed from the recommended `1280x720`");
            if (items["hook_static_functions"] is string hookStaticFunctions && hookStaticFunctions == TrueMark)
                notes.AppendLine("`Hook Static Functions` is enabled, please disable");
            if (items["gpu_texture_scaling"] is string gpuTextureScaling && gpuTextureScaling == TrueMark)
                notes.AppendLine("`GPU Texture Scaling` is enabled, please disable");
            if (items["af_override"] is string af && af == "Disabled")
                notes.AppendLine("`Anisotropic Filter` is `Disabled`, please use `Auto` instead");
            if (items["resolution_scale"] is string resScale && int.TryParse(resScale, out var resScaleFactor) && resScaleFactor < 100)
                notes.AppendLine($"`Resolution Scale` is `{resScale}%`.");
            if (items["cpu_blit"] is string cpuBlit && cpuBlit == TrueMark && items["write_color_buffers"] is string wcb && wcb == FalseMark)
                notes.AppendLine("`Force CPU Blit` is enabled, but `Write Color Buffers` is disabled");
            if (items["zcull"] is string zcull && zcull == TrueMark)
                notes.AppendLine("`ZCull Occlusion Queries` are disabled, can result in visual artifacts");
            if (items["spu_block_size"] is string spuBlockSize && spuBlockSize == "Giga")
                notes.AppendLine("`Giga` mode for `SPU Block Size` is strongly not recommended to use");

            var notesContent = notes.ToString().Trim();
            PageSection(builder, notesContent, "Important Settings to Review");
        }

        private static async Task BuildNotesSectionAsync(DiscordEmbedBuilder builder, LogParseState state, NameValueCollection items)
        {
            BuildWeirdSettingsSection(builder, items);
            var notes = new StringBuilder();
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
            if (items["fatal_error"] is string fatalError)
            {
                builder.AddField("Fatal Error", $"```{fatalError.Trim(1022)}```");
                if (fatalError.Contains("psf.cpp"))
                    notes.AppendLine("Game save data might be corrupted");
            }

            if (items["failed_to_decrypt"] is string _)
                notes.AppendLine("Failed to decrypt game content, license file might be corrupted");
            if (items["failed_to_boot"] is string _)
                notes.AppendLine("Failed to boot the game, the dump might be encrypted or corrupted");
            if (string.IsNullOrEmpty(items["ppu_decoder"]) || string.IsNullOrEmpty(items["renderer"]))
                notes.AppendLine("The log is empty, you need to run the game before uploading the log");
            if (!string.IsNullOrEmpty(items["hdd_game_path"]) && (items["serial"]?.StartsWith("BL", StringComparison.InvariantCultureIgnoreCase) ?? false))
                notes.AppendLine($"Disc game inside `{items["hdd_game_path"]}`");
            if (!string.IsNullOrEmpty(items["native_ui_input"]))
                notes.AppendLine("Pad initialization problem detected; try disabling `Native UI`");
            if (state.Error == LogParseState.ErrorCode.SizeLimit)
                notes.AppendLine("The log was too large, so only the last processed run is shown");

            // should be last check here
            var updateInfo = await CheckForUpdateAsync(items).ConfigureAwait(false);
            if (updateInfo != null)
                notes.AppendLine("Outdated RPCS3 build detected, please consider updating it");
            var notesContent = notes.ToString().Trim();
            PageSection(builder, notesContent, "Notes");
            //if (updateInfo != null)
            //    await updateInfo.AsEmbedAsync(builder).ConfigureAwait(false);
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

            var updateInfo = await compatClient.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
            var link = updateInfo?.LatestBuild?.Windows?.Download ?? updateInfo?.LatestBuild?.Linux?.Download;
            if (string.IsNullOrEmpty(link))
                return null;

            var latestBuildInfo = BuildInfoInUpdate.Match(link.ToLowerInvariant());
            if (!latestBuildInfo.Success || SameCommits(buildInfo.Groups["commit"].Value, latestBuildInfo.Groups["commit"].Value))
                return null;

            return updateInfo;
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
                return AmdDriverVersionProvider.GetFromVulkanAsync(result).GetAwaiter().GetResult();

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
    }
}
