﻿using System;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatBot.EventHandlers;
using CompatBot.EventHandlers.LogParsing.POCOs;
using DSharpPlus;
using DSharpPlus.Entities;

namespace CompatBot.ResultFormatters
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

        public static async Task<DiscordEmbed> AsEmbedAsync(this LogParseState state, DiscordClient client, DiscordMessage message)
        {
            DiscordEmbedBuilder builder;
            var collection = state.CompleteCollection ?? state.WipCollection;
            if (collection?.Count > 0)
            {
                var gameInfo = await client.LookupGameInfoAsync(collection["serial"], false).ConfigureAwait(false);
                builder = new DiscordEmbedBuilder(gameInfo);
                if (state.Error == LogParseState.ErrorCode.PiracyDetected)
                {
                    state.PiracyContext = state.PiracyContext.Sanitize();
                    var msg = $"{message.Author.Mention}, you are being denied further support until you legally dump the game!\n" +
                              "Please note that the RPCS3 community and its developers do not support piracy!\n" +
                              "Most of the issues caused by pirated dumps is because they have been tampered with in such a way " +
                              "and therefore act unpredictably on RPCS3.\n" +
                              "If you need help obtaining legal dumps please read <https://rpcs3.net/quickstart>";
                    builder.WithColor(Config.Colors.LogAlert)
                        .WithTitle("Pirated release detected")
                        .WithDescription(msg);
                }
                else
                {
                    CleanupValues(collection);
                    BuildInfoSection(builder, collection);
                    BuildCpuSection(builder, collection);
                    BuildGpuSection(builder, collection);
                    BuildLibsSection(builder, collection);
                    await BuildNotesSectionAsync(builder, state, collection).ConfigureAwait(false);
                }
            }
            else
            {
                builder = new DiscordEmbedBuilder
                {
                    Description = "Log analysis failed, most likely cause is an empty log. Try reuploading a new copy.",
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
            if (items["driver_manuf_new"] != null)
                items["gpu_info"] = items["driver_manuf_new"];
            else if (items["vulkan_gpu"] != "\"\"")
                items["gpu_info"] = items["vulkan_gpu"];
            else if (items["d3d_gpu"] != "\"\"")
                items["gpu_info"] = items["d3d_gpu"];
            else if (items["driver_manuf"] != null)
                items["gpu_info"] = items["driver_manuf"];
            else
                items["gpu_info"] = "Unknown";
            if (items["driver_version_new"] != null)
                items["gpu_info"] = items["gpu_info"] + " (" + items["driver_version_new"] + ")";
            else if (items["driver_version"] != null)
                items["gpu_info"] = items["gpu_info"] + " (" + items["driver_version"] + ")";
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
            else
                items["os_path"] = "Unknown";
            if (items["library_list"] is string libs)
            {
                var libList = libs.Split('\n').Select(l => l.Trim(' ', '\t', '-', '\r', '[', ']')).Where(s => !string.IsNullOrEmpty(s)).ToList();
                items["library_list"] = libList.Count > 0 ? string.Join(", ", libList) : "None";
            }

            foreach (var key in items.AllKeys)
            {
                var value = items[key];
                if ("true".Equals(value, StringComparison.CurrentCultureIgnoreCase))
                    value = "[x]";
                else if ("false".Equals(value, StringComparison.CurrentCultureIgnoreCase))
                    value = "[ ]";
                items[key] = value.Sanitize();
            }
        }

        private static void BuildInfoSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            builder.AddField("Build Info", $"{items["build_and_specs"]}{Environment.NewLine}GPU: {items["gpu_info"]}");
        }

        private static void BuildCpuSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            var content = new StringBuilder()
                .AppendLine($"`PPU Decoder: {items["ppu_decoder"],21}`")
                .AppendLine($"`SPU Decoder: {items["spu_decoder"],21}`")
                .AppendLine($"`SPU Lower Thread Priority: {items["spu_lower_thread_priority"],7}`")
                .AppendLine($"`SPU Loop Detection: {items["spu_loop_detection"],14}`")
                .AppendLine($"`Thread Scheduler: {items["thread_scheduler"],16}`")
                .AppendLine($"`Detected OS: {items["os_path"],21}`")
                .AppendLine($"`SPU Threads: {items["spu_threads"],21}`")
                .AppendLine($"`Force CPU Blit: {items["cpu_blit"] ?? "N/A",18}`")
                .AppendLine($"`Hook Static Functions: {items["hook_static_functions"],11}`")
                .AppendLine($"`Lib Loader: {items["lib_loader"],22}`")
                .ToString();
            builder.AddField(items["custom_config"] == null ? "CPU Settings" : "Per-game CPU Settings", content, true);
        }

        private static void BuildGpuSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            var content = new StringBuilder()
                .AppendLine($"`Renderer: {items["renderer"],24}`")
                .AppendLine($"`Aspect ratio: {items["aspect_ratio"],20}`")
                .AppendLine($"`Resolution: {items["resolution"],22}`")
                .AppendLine($"`Resolution Scale: {items["resolution_scale"] ?? "N/A",16}`")
                .AppendLine($"`Resolution Scale Threshold: {items["texture_scale_threshold"] ?? "N/A",6}`")
                .AppendLine($"`Write Color Buffers: {items["write_color_buffers"],13}`")
                .AppendLine($"`Use GPU texture scaling: {items["gpu_texture_scaling"],9}`")
                .AppendLine($"`Anisotropic Filter: {items["af_override"] ?? "N/A",14}`")
                .AppendLine($"`Frame Limit: {items["frame_limit"],21}`")
                .AppendLine($"`Disable Vertex Cache: {items["vertex_cache"],12}`")
                .ToString();
            builder.AddField(items["custom_config"] == null ? "GPU Settings" : "Per-game GPU Settings", content, true);
        }

        private static void BuildLibsSection(DiscordEmbedBuilder builder, NameValueCollection items)
        {
            if (items["lib_loader"] is string libs && libs.Contains("manual", StringComparison.InvariantCultureIgnoreCase))
                builder.AddField("Selected Libraries", items["library_list"]);
        }

        private static async Task BuildNotesSectionAsync(DiscordEmbedBuilder builder, LogParseState state, NameValueCollection items)
        {
            if (items["fatal_error"] is string fatalError)
                builder.AddField("Fatal Error", $"`{fatalError}`");
            string notes = null;
            if (state.Error == LogParseState.ErrorCode.SizeLimit)
                notes += "Log was too large, showing last processed run";

            // should be last check here
            var updateInfo = await CheckForUpdateAsync(items).ConfigureAwait(false);
            if (updateInfo != null)
                notes += $"{Environment.NewLine}Outdated RPCS3 build detected, consider updating";
            if (notes != null)
                builder.AddField("Notes", notes);

            if (updateInfo != null)
                await updateInfo.AsEmbedAsync(builder).ConfigureAwait(false);
        }

        private static async Task<UpdateInfo> CheckForUpdateAsync(NameValueCollection items)
        {
            if (!(items["build_and_specs"] is string buildAndSpecs))
                return null;

            var buildInfo = BuildInfoInLog.Match(buildAndSpecs.ToLowerInvariant());
            if (!buildInfo.Success || buildInfo.Groups["branch"].Value != "head")
                return null;

            var updateInfo = await compatClient.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
            var link = updateInfo.LatestBuild?.Windows?.Download ?? updateInfo.LatestBuild?.Linux?.Download;
            if (string.IsNullOrEmpty(link))
                return null;

            var latestBuildInfo = BuildInfoInUpdate.Match(link.ToLowerInvariant());
            if (!latestBuildInfo.Success || buildInfo.Groups["commit"].Value == latestBuildInfo.Groups["commit"].Value)
                return null;

            return updateInfo;
        }
    }
}