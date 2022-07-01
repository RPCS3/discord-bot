﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.EventHandlers.LogParsing.POCOs;
using CompatBot.EventHandlers.LogParsing.SourceHandlers;
using DSharpPlus;
using DSharpPlus.Entities;
using IrdLibraryClient;

namespace CompatBot.Utils.ResultFormatters;

internal static partial class LogParserResult
{
    private static readonly Client CompatClient = new();
    private static readonly IrdClient IrdClient = new();
    private static readonly PsnClient.Client PsnClient = new();

    private static readonly RegexOptions DefaultSingleLine = RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Singleline;
    // RPCS3 v0.0.3-3-3499d08 Alpha | HEAD
    // RPCS3 v0.0.4-6422-95c6ac699 Alpha | HEAD
    // RPCS3 v0.0.5-7104-a19113025 Alpha | HEAD
    // RPCS3 v0.0.5-42b4ce13a Alpha | minor
    // RPCS3 v0.0.18-local_build Alpha | local_build
    private static readonly Regex BuildInfoInLog = new(
        @"RPCS3 v(?<version_string>(?<version>(\d|\.)+)(-(?<build>\d+))?-(?<commit>[0-9a-z_]+|unknown)) (?<stage>\w+)( \| (?<branch>[^|]+))?( \| Firmware version: (?<fw_version_installed>[^|\r\n]+))?( \| (?<unknown>.*))?\r?$",
        DefaultSingleLine);
    private static readonly Regex CpuInfoInLog = new(
        @"(\d{1,2}(th|rd|nd|st) Gen)?(?<cpu_model>[^|@]+?)\s*(((CPU\s*)?@\s*(?<cpu_speed>.+)\s*GHz\s*)|((APU with|(with )?Radeon|R\d, \d+ Compute) [^|]+)|((\w+[\- ]Core )?Processor))?\s* \| (?<thread_count>\d+) Threads \| (?<memory_amount>[0-9\.\,]+) GiB RAM( \| TSC: (?<tsc>\S+))?( \| (?<cpu_extensions>.*?))?\r?$",
        DefaultSingleLine);
    // Operating system: Windows, Major: 10, Minor: 0, Build: 22000, Service Pack: none, Compatibility mode: 0
    // Operating system: POSIX, Name: Linux, Release: 5.15.11-zen1-1-zen, Version: #1 ZEN SMP PREEMPT Wed, 22 Dec 2021 09:23:53 +0000
    // Operating system: macOS, Version 12.1.0
    internal static readonly Regex OsInfoInLog = new(
        @"Operating system: (?<os_type>[^,]+), (Name: (?<posix_name>[^,]+), Release: (?<posix_release>[^,]+), Version: (?<posix_version>[^\r\n]+)|Major: (?<os_version_major>\d+), Minor: (?<os_version_minor>\d+), Build: (?<os_version_build>\d+), Service Pack: (?<os_service_pack>[^,]+), Compatibility mode: (?<os_compat_mode>[^,\r\n]+)|Version: (?<macos_version>[^\r\n]+))\r?$",
        DefaultSingleLine);
    private static readonly Regex LinuxKernelVersion = new(@"(?<version>\d+\.\d+\.\d+(-\d+)?)", DefaultSingleLine);
    private static readonly Regex ProgramHashPatch = new(@"(?<hash>\w+(-\d+)?)( \(<-\s*(?<patch_count>\d+)\))?", DefaultSingleLine);
    private static readonly char[] NewLineChars = {'\r', '\n'};

    // rpcs3-v0.0.5-7105-064d0619_win64.7z
    // rpcs3-v0.0.5-7105-064d0619_linux64.AppImage
    private static readonly Regex BuildInfoInUpdate = new(@"rpcs3-v(?<version>(\d|\.)+)(-(?<build>\d+))?-(?<commit>[0-9a-f]+)_", DefaultSingleLine);
    private static readonly Regex VulkanDeviceInfo = new(@"'(?<device_name>.+)' running on driver (?<version>.+)\r?$", DefaultSingleLine);
    private static readonly Regex IntelGpuModel = new(@"Intel\s?(®|\(R\))? (?<gpu_model>((?<gpu_family>(\w|®| )+) Graphics)( (?<gpu_model_number>P?\d+))?)(\s+\(|$)", DefaultSingleLine);
    private static readonly Regex InstallPath = new(@"[A-Z]:/(?<program_files>Program Files( \(x86\))?/)?(?<desktop>([^/]+/)+Desktop/)?(?<rpcs3_folder>[^/]+/)*GuiConfigs/", DefaultSingleLine);

    private static readonly Version MinimumOpenGLVersion = new(4, 3);
    private static readonly Version MinimumFirmwareVersion = new(4, 80);
    private static readonly Version NvidiaFullscreenBugMinVersion = new(400, 0);
    private static readonly Version NvidiaFullscreenBugMaxVersion = new(499, 99);
    private static readonly Version NvidiaRecommendedWindowsVersion = new(452, 28);
    private static readonly Version NvidiaRecommendedLinuxVersion = new(450, 56);
    private static readonly Version AmdRecommendedOldWindowsVersion = new(20, 1, 4);
    private static readonly Version AmdLastGoodOpenGLWindowsVersion = new(20, 1, 4);
    private static readonly Version NvidiaFullscreenBugFixed = new(0, 0, 6, 8204);
    private static readonly Version TsxFaFixedVersion  = new(0, 0, 12, 10995);
    private static readonly Version RdnaMsaaFixedVersion  = new(0, 0, 13, 11300);
    private static readonly Version IntelThreadSchedulerBuildVersion  = new(0, 0, 15, 12008);
    private static readonly Version CubebBuildVersion  = new(0, 0, 19, 13050);
    

    private static readonly Dictionary<string, string> RsxPresentModeMap = new()
    {
        ["0"] = "VK_PRESENT_MODE_IMMEDIATE_KHR",    // no vsync
        ["1"] = "VK_PRESENT_MODE_MAILBOX_KHR",      // fast sync
        ["2"] = "VK_PRESENT_MODE_FIFO_KHR",         // vsync
        ["3"] = "VK_PRESENT_MODE_FIFO_RELAXED_KHR", // adaptive vsync
    };

    private static readonly HashSet<string> KnownSyncFolders = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "OneDrive",
        "MEGASync",
        "RslSync",
        "BTSync",
        "Google Drive",
        "Google Backup",
        "Dropbox",
    };

    private static readonly Dictionary<string, string> KnownDiscOnPsnIds = new(StringComparer.InvariantCultureIgnoreCase)
    {
        // Demon's Souls
        {"BLES00932", "NPEB01202"},
        {"BLUS30443", "NPUB30910"},
        {"BCJS30022", "NPJA00102"},
        //{"BCJS70013", "NPJA00102"},

        // White Knight Chronicles II
        {"BCJS30042", "NPJA00104"}
    };

    private static readonly string[] Known1080pIds =
    {
        "NPEB00258", "NPUB30162", "NPJB00068", // scott pilgrim
    };

    private static readonly string[] KnownDisableVertexCacheIds =
    {
        "NPEB00258", "NPUB30162", "NPJB00068", // scott pilgrim
        "NPEB00303", "NPUB30242", "NPHB00229", // crazy taxi
    };

    private static readonly HashSet<string> KnownNoApproximateXFloatIds = new()
    {
        "BLES02247", "BLUS31604", "BLJM61346", "NPEB02436", "NPUB31848", "NPJB00769", // p5
        "BLES00932", "BLUS30443", // DeS
    };

    private static readonly HashSet<string> KnownFpsUnlockPatchIds = new()
    {
        "BLES00932", "BLUS30443", // DeS
        "BLUS30481", "BLES00826", "BLJM60223", // Nier
        "BLUS31197", "NPUB31251", "NPEB01407", "BLJM61043", "BCAS20311", // DoD3
        "BLUS31405", // jojo asb
        "BLJS10318", // jojo eoh
    };

    private static readonly HashSet<string> KnownWriteColorBuffersIds = new()
    {
        "BLES00932", "BLUS30443", "BCJS70013", "BCJS30022", // DeS
        "BLUS30481", "BLES00826", "BLJM60223", // Nier
        "BCAS25003", "BCES00510", "BCES00516", "BCES00799", "BCJS37001", "BCUS98111", "BCKS15003", "NPUA70080", // God of War 3 / Demo
        "BCUS98167", "BCJS30041", "BCES00701", "NPUA80535", "NPEA00291", // Modnation Racers
        "NPUA70096", "NPEA90062", // Modnation Racers demos
        "NPUA70074", // Modnation Racers beta
        "BCES01422", "BCUS98254", "NPUA80848", "NPEA00421", "NPHA80239", // LittleBigPlanet Karting
        "NPJA90244", "NPEA90117", "NPUA70249", // LittleBigPlanet Karting demo
        "BCAS20066", "BCES00081", "BCUS98116", "NPUA98116", "NPUA70034", // Killzone 2
        "NPJA90092", "NPEA90034", "NPUA70034", // Killzone 2 demo
        "BCES00006", "BCUS98137", "NPEA00333", "NPUA80499", // Motorstorm
        "BCES00484", "BCUS98242", "NPEA00315", "NPUA80661", // Motorstorm Apocalypse
        "BCES00129", "BCUS98155", // Motorstorm Pacific Rift
        "NPEA90090", "NPUA70140", "NPEA90033", // Motorstorm demos
        "BLJM60528", "NPJB00235", "NPHB00522", "NPJB90534", //E.X. Troopers / demo
        "BLES01702", "BLJS10187", "BLUS31002", "NPEB01140", "NPJB00236", "NPUB30899", // Tekken Tag Tournament 2
        "BLES01987", "BLUS30964", "BLJS10160", // The Witch and the Hundred Knight
        "BCAS20100", "BCES00664", "NPEA00057", "NPJA00031", "NPUA80105", // wipeout hd

    };

    private static readonly HashSet<string> KnownResScaleThresholdIds = new()
    {
        "BCAS20270", "BCES01584", "BCES01585", "BCJS37010", "BCUS98174", // The Last of Us
        "NPEA00435", "NPEA90122", "NPHA80243", "NPHA80279", "NPJA00096", "NPJA00129", "NPUA70257", "NPUA80960", "NPUA81175", 
    };

    private static readonly HashSet<string> KnownMotionControlsIds = new()
    {
        "BCES00797", "BCES00802", "BCUS98164", "BCJS30040", "NPEA90053", "NPEA90076", "NPUA70088", "NPUA70112", // heavy rain
        "BCAS25017", "BCES01121", "BCES01122", "BCES01123", "BCUS98298", "NPEA00513", "NPUA81087", "NPEA90127", "NPJA90259", "NPUA72074", "NPJA00097", // beyond two souls
        "NPEA00094", "NPEA00250", "NPJA00039", "NPUA80083", // flower
        "NPEA00036", "NPUA80069", "NPJA00004", // locoroco
        "BCES01284", "BCUS98247", "BCUS99142", "NPEA00429", "NPUA80875", "NPEA90120", "NPUA70250", // sly cooper 4
        "BCAS20112", "BCAS20189", "BCKS10112", "BLES01101", "BLJS10072", "BLJS10114", "BLJS50026", "BLUS30652", "NPEB90321", // no more heroes
    };

    private static readonly HashSet<string> KnownGamesThatRequireInterpreter = new()
    {
        "NPEB00630", "NPUB30493", "NPJB00161", // daytona usa
        "BCAS25017", "BCES01121", "BCES01122", "BCES01123", "BCUS98298", "NPEA00513", "NPUA81087", "NPEA90127", "NPJA90259", "NPUA72074", "NPJA00097", // beyond two souls
    };

    private static readonly HashSet<string> KnownGamesThatRequireAccurateXfloat = new()
    {
        "BLES00229", "BLES00258", "BLES00887", "BLES01128", // gta4 + efls
        "BLJM55011", "BLJM60235", "BLJM60459", "BLJM60525", "BLJM61180", "BLKS20073", "BLKS20198", // gta4 + efls
        "BLUS30127", "BLUS30149", "BLUS30524", "BLUS30682", // gta4 + efls
        "NPEB00882", "NPUB30702", "NPUB30704", "NPEB00511", // gta4 + efls
    };

    private static readonly HashSet<string> KnownGamesThatWorkWithRelaxedZcull = new()
    {
        "BLAS50296", "BLES00680", "BLES01179", "BLES01294", "BLUS30418", "BLUS30711", "BLUS30758", //rdr
        "BLJM60314", "BLJM60403", "BLJM61181", "BLKS20315",
        "NPEB00833", "NPHB00465", "NPHB00466", "NPUB30638", "NPUB30639",
        "NPUB50139", // max payne 3 / rdr bundle???
        "BLAS55005", "BLES00246", "BLJM57001", "BLJM67001", "BLKS25001", "BLUS30109", "BLUS30148", //mgs4
        "NPEB00027", "NPEB02182", "NPEB90116", "NPJB00698", "NPJB90149", "NPUB31633",
        "NPHB00065", "NPHB00067",
        "BCAS20100", "BCES00664", "NPEA00057", "NPJA00031", "NPUA80105", // wipeout hd
    };

    private static readonly HashSet<string> KnownBogusLicenses = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "UP0700-NPUB30932_00-NNKDLFULLGAMEPTB.rap",
        "EP0700-NPEB01158_00-NNKDLFULLGAMEPTB.rap",
    };

    private static readonly HashSet<string> KnownCustomLicenses = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "EP4062-NPEB02436_00-PPERSONA5X000000.rap",
        "UP2611-NPUB31848_00-PPERSONA5X000000.rap",
    };

    // v1.5 https://wiki.rpcs3.net/index.php?title=Help:Game_Patches#Disable_SPU_MLAA
    private static readonly HashSet<string> KnownMlaaSpuHashes = new(StringComparer.InvariantCultureIgnoreCase)
    {
        "1549476fe258150ff9f902229ffaed69a932a9c1",
        "191fe1c92c8360992b3240348e70ea37d50812d4",
        "2239af4827b17317522bd6323c646b45b34ebf14",
        "45f98378f0837fc6821f63576f65d47d10f9bbcb",
        "5177cbc4bf45c8a0a6968c2a722da3a9e6cfb28b",
        "530c255936b07b25467a58e24ceff5fd4e2960b7",
        "702d0205a89d445d15dc0f96548546c4e2e7a59f",
        "794795c449beef176d076816284849d266f55f99",
        "7b5ea49122ec7f023d4a72452dc7a9208d9d6dbf",
        "7cd211ff1cbd33163eb0711440dccbb3c1dbcf6c",
        "82b3399c8e6533ba991eedb0e139bf20c7783bac",
        "9001b44fd7278b5a6fa5385939fe928a0e549394",
        "931132fd48a40bce0bec28e21f760b1fc6ca4364",
        "969cf3e9db75f52a6b41074ccbff74106b709854",
        "976d2128f08c362731413b75c934101b76c3d73b",
        "a129a01a270246c85df18eee0e959ef4263b6510",
        "ac189d7f87091160a94e69803ac0cff0a8bb7813",
        "df5b1c3353cc36bb2f0fb59197d849bb99c3fecd",
        "e3780fe1dc8953f849ac844ec9688ff4da3ca3ae",
    };

    private static readonly TimeSpan OldBuild = TimeSpan.FromDays(30);
    private static readonly TimeSpan VeryOldBuild = TimeSpan.FromDays(60);
    //private static readonly TimeSpan VeryVeryOldBuild = TimeSpan.FromDays(90);
    private static readonly TimeSpan AncientBuild = TimeSpan.FromDays(180);
    private static readonly TimeSpan PrehistoricBuild = TimeSpan.FromDays(365);

    private static readonly char[] PrioritySeparator = {' '};
    private static readonly string[] EmojiPriority = { "😱", "💢", "‼", "❗",  "❌", "⁉", "⚠", "❔", "✅", "ℹ" };
    private const string EnabledMark = "[x]";
    private const string DisabledMark = "[\u00a0]";

    public static async Task<DiscordEmbedBuilder> AsEmbedAsync(this LogParseState state, DiscordClient client, DiscordMessage message, ISource source)
    {
        DiscordEmbedBuilder builder;
        state.CompletedCollection ??= state.WipCollection;
        state.CompleteMultiValueCollection ??= state.WipMultiValueCollection;
        var collection = state.CompletedCollection;
        if (collection.Count > 0)
        {
            var ldrGameSerial = collection["ldr_game_serial"] ?? collection["ldr_path_serial"];
            if (collection["serial"] is string serial
                && KnownDiscOnPsnIds.TryGetValue(serial, out var psnSerial)
                && !string.IsNullOrEmpty(ldrGameSerial)
                && ldrGameSerial.StartsWith("NP", StringComparison.InvariantCultureIgnoreCase)
                && ldrGameSerial.Equals(psnSerial, StringComparison.InvariantCultureIgnoreCase))
            {
                collection["disc_to_psn_serial"] = serial;
                collection["serial"] = psnSerial;
                collection["game_category"] = "HG";
            }
            serial = collection["serial"] ?? "";
            var titleUpdateInfoTask = TitleUpdateInfoProvider.GetAsync(serial, Config.Cts.Token);
            var titleMetaTask = PsnClient.GetTitleMetaAsync(serial, Config.Cts.Token);
            var gameInfo = await client.LookupGameInfoWithEmbedAsync(serial, collection["game_title"], true, category: collection["game_category"]).ConfigureAwait(false);
            try
            {
                var titleUpdateInfo = await titleUpdateInfoTask.ConfigureAwait(false);
                if (titleUpdateInfo?.Tag?.Packages?.LastOrDefault()?.Version is string tuVersion)
                    collection["game_update_version"] = tuVersion;

            }
            catch {}
            try
            {
                var resInfo = await titleMetaTask.ConfigureAwait(false);
                if (resInfo?.Resolution is string resList)
                    collection["game_supported_resolution_list"] = resList;
            }
            catch {}
            builder = new(gameInfo.embedBuilder) {Thumbnail = null}; // or this will fuck up all formatting
            TitleInfo? compatData = null;
            if (gameInfo.compatResult?.Results?.TryGetValue(serial, out compatData) is true
                && compatData?.Status is string titleStatus)
                collection["game_status"] = titleStatus; 
            collection["embed_title"] = builder.Title ?? "";
            if (state.Error == LogParseState.ErrorCode.PiracyDetected)
            {
                var msg = "__You are being denied further support until you legally dump the game__.\n" +
                          "Please note that the RPCS3 community and its developers do not support piracy.\n" +
                          "Most of the issues with pirated dumps occur due to them having been tampered with in some way " +
                          "and therefore act unpredictably on RPCS3.\n" +
                          "If you need help obtaining legal dumps, please read [the quickstart guide](https://rpcs3.net/quickstart).";
                builder.WithColor(Config.Colors.LogAlert)
                    .WithTitle("Pirated content detected")
                    .WithDescription(msg);
            }
            else
            {
                CleanupValues(state);
                BuildInfoSection(builder, collection);
                var hwUpdateTask = HwInfoProvider.AddOrUpdateSystemAsync(message, collection, Config.Cts.Token);
                var colA = BuildCpuSection(collection);
                var colB = BuildGpuSection(collection);
                BuildSettingsSections(builder, collection, colA, colB);
                BuildLibsSection(builder, collection);
                await BuildNotesSectionAsync(builder, state, client).ConfigureAwait(false);
                await hwUpdateTask.ConfigureAwait(false);
            }
        }
        else
        {
            builder = new DiscordEmbedBuilder
            {
                Description = "Log analysis failed. Please try again.",
                Color = Config.Colors.LogResultFailed,
            };
            if (state.TotalBytes < 2048 || state.ReadBytes < 2048)
                builder.Description = "Log analysis failed, most likely cause is an empty log. Please try again.";
        }
        builder.AddAuthor(client, message, source, state);
        return builder;
    }

    private static void CleanupValues(LogParseState state)
    {
        var items = state.CompletedCollection!;
        var multiItems = state.CompleteMultiValueCollection!;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var itemKeys = items.AllKeys;
            foreach (var key in itemKeys)
                items[key] = items[key]?.TrimEnd('”');
            var colKeyList = multiItems.Keys;
            foreach (var key in colKeyList)
                multiItems[key] = new(multiItems[key].Select(i => i.TrimEnd('”')));
        }
        if (items["strict_rendering_mode"] == "true")
            items["resolution_scale"] = "Strict Mode";
        if (items["spu_threads"] == "0")
            items["spu_threads"] = "Auto";
        var threadSched = (items["thread_scheduler"] ?? items["spu_secondary_cores"]) switch
        {
            "false" => "OS",
            "Operating System" => "OS",
            "true" => "RPCS3",
            "RPCS3 Scheduler" => "RPCS3",
            "RPCS3 Alternative Scheduler" => "RPCS3 Alt",
            string s => s,
            null => null,
        };
        if (!string.IsNullOrEmpty(threadSched))
            items["thread_scheduler"] = threadSched;

        if (items["cpu_model"] is string cpu)
        {
            cpu = cpu.Replace("AMD FX -", "AMD FX-");

            items["cpu_model"] = cpu;
        }
        
        static string? StripOpenGlMaker(string? gpuName)
        {
            if (gpuName is null)
                return null;

            if (gpuName.EndsWith("(intel)", StringComparison.OrdinalIgnoreCase)
                || gpuName.EndsWith("(nvidia)", StringComparison.OrdinalIgnoreCase)
                || gpuName.EndsWith(" corporation)", StringComparison.OrdinalIgnoreCase)
                || gpuName.EndsWith("(amd)", StringComparison.OrdinalIgnoreCase)
                || gpuName.EndsWith(" inc.)", StringComparison.OrdinalIgnoreCase) // ati
                || gpuName.EndsWith("(apple)", StringComparison.OrdinalIgnoreCase)
                || gpuName.EndsWith("(x.org)", StringComparison.OrdinalIgnoreCase) // linux
            )
            {
                var idx = gpuName.LastIndexOf('(');
                if (idx > 0)
                    gpuName = gpuName[..idx].TrimEnd();
            }
            if (gpuName.EndsWith("/PCIe/SSE2"))
                gpuName = gpuName[..^10];
            return gpuName;
        }
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
        if (!string.IsNullOrEmpty(items["gpu_info"]))
        {
            items["gpu_info"] = items["gpu_info"]?.StripMarks();
            items["driver_version_info"] = GetVulkanDriverVersion(items["vulkan_initialized_device"], multiItems["vulkan_found_device"]) ??
                                           GetVulkanDriverVersion(items["gpu_info"], multiItems["vulkan_found_device"]) ??
                                           GetOpenglDriverVersion(items["gpu_info"], items["driver_version_new"] ?? items["driver_version"]) ??
                                           GetVulkanDriverVersionRaw(items["gpu_info"], items["vulkan_driver_version_raw"]);
        }
        items["gpu_info"] = StripOpenGlMaker(items["gpu_info"]);
        if (items["driver_version_info"] != null)
        {
            items["gpu_name"] = items["gpu_info"];
            items["gpu_info"] += $" ({items["driver_version_info"]})";
        }

        if (multiItems["vulkan_compatible_device_name"] is { Count: > 0 } vulkanDevices)
        {
            var devices = vulkanDevices
                .Distinct()
                .Select(n => new { name = n.StripMarks(), driverVersion = GetVulkanDriverVersion(n, multiItems["vulkan_found_device"]) })
                .Reverse()
                .ToList();
            if (string.IsNullOrEmpty(items["gpu_info"]) && devices.Count > 0)
            {
                var discreteGpu = devices.FirstOrDefault(d => IsNvidia(d.name))
                                  ?? devices.FirstOrDefault(d => IsAmd(d.name))
                                  ?? devices.First();
                items["gpu_name"] = StripOpenGlMaker(discreteGpu.name);
                items["discrete_gpu_info"] = $"{discreteGpu.name} ({discreteGpu.driverVersion})";
                items["driver_version_info"] = discreteGpu.driverVersion;
            }
            items["gpu_available_info"] = string.Join(Environment.NewLine, devices.Select(d => $"{d.name} ({d.driverVersion})"));
        }

        if (items["af_override"] is string af)
        {
            if (af == "0")
                items["af_override"] = "Auto";
            else if (af == "1")
                items["af_override"] = "Disabled";
        }
        if (items["zcull"] == "true")
            items["zcull_status"] = "Disabled";
        else if (items["relaxed_zcull"] == "true")
            items["zcull_status"] = "Relaxed";
        else
            items["zcull_status"] = "Full";
        if (items["lib_loader"] is string libLoader)
        {
            var liblv2 = libLoader.Contains("liblv2", StringComparison.InvariantCultureIgnoreCase);
            var auto = libLoader.Contains("auto", StringComparison.InvariantCultureIgnoreCase);
            var manual = libLoader.Contains("manual", StringComparison.InvariantCultureIgnoreCase);
            var strict = libLoader.Contains("strict", StringComparison.InvariantCultureIgnoreCase);
            if (auto && manual)
                items["lib_loader"] = "Auto & manual select";
            else if (liblv2 && manual)
                items["lib_loader"] = "Liblv2.sprx & manual";
            else if (liblv2 && strict)
                items["lib_loader"] = "Liblv2.sprx & strict";
            else if (auto)
                items["lib_loader"] = "Auto";
            else if (manual)
                items["lib_loader"] = "Manual selection";
            else
                items["lib_loader"] = "Liblv2.sprx only";
        }
        items["frame_limit_combined"] = (items["frame_limit"], items["vsync"]) switch
        {
            ("Off", "true") => "VSync",
            ("Off",      _) => "Off",
            (    _, "true") => items["frame_limit"] + "+VSync",
            _               => items["frame_limit"],
        };
        if (items["shader_mode"] is string sm)
        {
            var async = sm.Contains("async", StringComparison.InvariantCultureIgnoreCase);
            var recompiler = sm.Contains("recompiler", StringComparison.InvariantCultureIgnoreCase);
            var interpreter = sm.Contains("interpreter", StringComparison.InvariantCultureIgnoreCase);
            items["shader_mode"] = (async, recompiler, interpreter) switch
            {
                ( true, true, false) => "Async",
                ( true,    _,  true) => "Async+Interpreter",
                (false, true, false) => "Recompiler only",
                (false,    _,  true) => "Interpreter only",
                _                    => items["shader_mode"],
            };
        }
        else if (items["disable_async_shaders"] == DisabledMark)
            items["shader_mode"] = "Async";
        else if (items["disable_async_shaders"] == EnabledMark)
            items["shader_mode"] = "Recompiler only";

        static string? reformatDecoder(string? dec)
        {
            if (string.IsNullOrEmpty(dec))
                return dec;

            var match = Regex.Match(dec, @"(?<name>[^(]+)(\((?<type>.+)\))?", RegexOptions.ExplicitCapture | RegexOptions.Singleline);
            var n = match.Groups["name"].Value.TrimEnd();
            var t = match.Groups["type"].Value;
            return string.IsNullOrEmpty(t) ? n : $"{n}/{t}";
        }

        items["ppu_decoder"] = reformatDecoder(items["ppu_decoder"]);
        items["spu_decoder"] = reformatDecoder(items["spu_decoder"]);
        if (items["win_path"] != null)
            items["os_type"] = "Windows";
        else if (items["lin_path"] != null)
            items["os_type"] = "Linux";
        if (items["os_type"] == "Windows" && GetWindowsVersion(items["driver_version_new"] ?? items["driver_version"]) is string winVersion)
            items["os_windows_version"] = winVersion;
        if (items["library_list"] is string libs)
        {
            var libList = libs.Split('\n')
                .Select(l => l.Trim(' ', '\t', '-', '\r', '[', ']'))
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(l => l.Split(':'))
                .Select(p => (name: p[0], mode: p.Length > 1 ? p[1] : ""))
                .ToList();
            var newFormat = libs.Contains(".sprx:");
            if (newFormat)
            {
                items["library_list"] = "None";
                var lleList = new List<string>(libList.Count);
                var hleList = new List<string>(libList.Count);
                foreach (var (name, mode) in libList)
                {
                    if (mode == "lle")
                        lleList.Add(name);
                    else if (mode == "hle")
                        hleList.Add(name);
                    else
                        Config.Log.Warn($"Unknown library override mode '{mode}' in {libs}");
                }
                items["library_list_lle"] = lleList.Count > 0 ? string.Join(", ", lleList) : "None";
                items["library_list_hle"] = hleList.Count > 0 ? string.Join(", ", hleList) : "None";
            }
            else
                items["library_list"] = libList.Count > 0 ? string.Join(", ", libList.Select(i => i.name)) : "None";
        }
        else
            items["library_list"] = "None";
        if (items["app_version"] is string appVer && !appVer.Equals("Unknown", StringComparison.InvariantCultureIgnoreCase))
            items["game_version"] = appVer;
        else if (items["disc_app_version"] is string discAppVer && !discAppVer.Equals("Unknown", StringComparison.InvariantCultureIgnoreCase))
            items["game_version"] = discAppVer;
        else if (items["disc_package_version"] is string discPkgVer && !discPkgVer.Equals("Unknown", StringComparison.InvariantCultureIgnoreCase))
            items["game_version"] = discPkgVer;
        if (items["game_version"] is string gameVer && gameVer.StartsWith("0"))
            items["game_version"] = gameVer[1..];
        if (items["game_update_version"] is string gameUpVer && gameUpVer.StartsWith("0"))
            items["game_update_version"] = gameUpVer[1..];

        if (multiItems["fatal_error"] is UniqueList<string> {Count: > 0} fatalErrors)
            multiItems["fatal_error"] = new(fatalErrors.Select(str => str.Contains("'tex00'") ? str.Split('\n', 2)[0] : str), fatalErrors.Comparer);

        multiItems["broken_filename"].AddRange(multiItems["broken_filename_or_dir"]);
        multiItems["broken_directory"].AddRange(multiItems["broken_filename_or_dir"]);
            
        foreach (var key in items.AllKeys)
        {
            var value = items[key];
            if ("true".Equals(value, StringComparison.CurrentCultureIgnoreCase))
                value = EnabledMark;
            else if ("false".Equals(value, StringComparison.CurrentCultureIgnoreCase))
                value = DisabledMark;
            items[key] = value?.Sanitize(false);
        }
    }

    private static void PageSection(DiscordEmbedBuilder builder, string notesContent, string sectionName)
    {
        if (!string.IsNullOrEmpty(notesContent))
        {
            var fields = notesContent.Split(Environment.NewLine).BreakInFieldContent(100).ToList();
            if (fields.Count > 1)
                for (var idx = 0; idx < fields.Count; idx++)
                    builder.AddField($"{sectionName} #{idx + 1} of {fields.Count}", fields[idx].content);
            else
                builder.AddField(sectionName, fields[0].content);
        }
    }

    private static async Task<UpdateInfo?> CheckForUpdateAsync(NameValueCollection items)
    {
        if (string.IsNullOrEmpty(items["build_and_specs"]))
            return null;

        var currentBuildCommit = items["build_commit"];
        if (string.IsNullOrEmpty(currentBuildCommit))
            currentBuildCommit = null;
        var updateInfo = await CompatClient.GetUpdateAsync(Config.Cts.Token, currentBuildCommit).ConfigureAwait(false);
        if (updateInfo?.ReturnCode != 1 && currentBuildCommit != null)
            updateInfo = await CompatClient.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
        var link = updateInfo?.LatestBuild?.Windows?.Download ?? updateInfo?.LatestBuild?.Linux?.Download ?? updateInfo?.LatestBuild?.Mac?.Download;
        if (string.IsNullOrEmpty(link))
            return null;

        var latestBuildInfo = BuildInfoInUpdate.Match(link.ToLowerInvariant());
        if (latestBuildInfo.Success && VersionIsTooOld(items, latestBuildInfo, updateInfo))
            return updateInfo;

        return null;
    }

    private static bool VersionIsTooOld(NameValueCollection items, Match update, UpdateInfo? updateInfo)
    {
        if (updateInfo.GetUpdateDelta() is TimeSpan updateTimeDelta
            && updateTimeDelta < Config.BuildTimeDifferenceForOutdatedBuildsInDays)
            return false;

        if (Version.TryParse(items["build_version"], out var logVersion)
            && Version.TryParse(update.Groups["version"].Value, out var updateVersion))
        {
            if (logVersion < updateVersion)
                return true;

            if (int.TryParse(items["build_number"], out var logBuild)
                && int.TryParse(update.Groups["build"].Value, out var updateBuild))
            {
                if (logBuild + Config.BuildNumberDifferenceForOutdatedBuilds < updateBuild)
                    return true;
            }
            return false;
        }
        return !SameCommits(items["build_commit"], update.Groups["commit"].Value);
    }

    private static bool SameCommits(string? commitA, string? commitB)
    {
        if (string.IsNullOrEmpty(commitA) && string.IsNullOrEmpty(commitB))
            return true;

        if (string.IsNullOrEmpty(commitA) || string.IsNullOrEmpty(commitB))
            return false;

        var len = Math.Min(commitA.Length, commitB.Length);
        return commitA[..len] == commitB[..len];
    }

    private static string? GetOpenglDriverVersion(string? gpuInfo, string? version)
    {
        if (string.IsNullOrEmpty(version))
            return null;

        gpuInfo ??= "";
        if (gpuInfo.Contains("Radeon", StringComparison.InvariantCultureIgnoreCase)
            || gpuInfo.Contains("AMD", StringComparison.InvariantCultureIgnoreCase)
            || gpuInfo.Contains("ATI ", StringComparison.InvariantCultureIgnoreCase))
            return AmdDriverVersionProvider.GetFromOpenglAsync(version).ConfigureAwait(false).GetAwaiter().GetResult();

        return version;
    }

    private static string? GetVulkanDriverVersion(string? gpu, UniqueList<string> foundDevices)
    {
        if (string.IsNullOrEmpty(gpu) || !foundDevices.Any())
            return null;

        var info = (from line in foundDevices
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
        {
            if (gpu.Contains("RADV", StringComparison.InvariantCultureIgnoreCase))
                return result;

            return AmdDriverVersionProvider.GetFromVulkanAsync(result).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        if (gpu.Contains("Intel", StringComparison.InvariantCultureIgnoreCase)
            && Version.TryParse(result, out var ver)
            && ver.Minor > 400
            && ver.Build >= 0)
        {
            var build = ((ver.Minor & 0b_11) << 12) | ver.Build;
            var minor = ver.Minor >> 2;
            return new Version(ver.Major, minor, build).ToString();
        }

        if (Version.TryParse(result, out var nvVer))
        {
            result = $"{nvVer.Major}.{nvVer.Minor:00}";
            if (nvVer.Build > 0)
                result += $".{nvVer.Build}";
        }
        else
        {
            if (result.EndsWith(".0.0"))
                result = result[..^4];
            if (result.Length > 3 && result[^2] == '.')
                result = result[..^1] + "0" + result[^1];
        }
        return result;
    }

    private static string? GetVulkanDriverVersionRaw(string? gpuInfo, string? version)
    {
        if (string.IsNullOrEmpty(version))
            return null;

        gpuInfo ??= "";
        var ver = int.Parse(version);
        if (IsAmd(gpuInfo))
        {
            var major = (ver >> 22) & 0x3ff;
            var minor = (ver >> 12) & 0x3ff;
            var patch = ver & 0xfff;
            var result = $"{major}.{minor}.{patch}";
            if (gpuInfo.Contains("RADV", StringComparison.InvariantCultureIgnoreCase))
                return result;

            return AmdDriverVersionProvider.GetFromVulkanAsync(result).ConfigureAwait(false).GetAwaiter().GetResult();
        }
        else
        {
            var major = (ver >> 22) & 0x3ff;
            var minor = (ver >> 14) & 0xff;
            var patch = ver & 0x3fff;
            if (major == 0 && gpuInfo.Contains("Intel", StringComparison.InvariantCultureIgnoreCase))
                return $"{minor}.{patch}";

            if (IsNvidia(gpuInfo))
            {
                if (patch == 0)
                    return $"{major}.{minor}";
                return $"{major}.{minor:00}.{(patch >> 6) & 0xff}.{patch & 0x3f}";
            }

            return $"{major}.{minor}.{patch}";
        }
    }

    private static string? GetWindowsVersion(string? driverVersionString)
    {
        // see https://docs.microsoft.com/en-us/windows-hardware/drivers/display/wddm-2-1-features#driver-versioning
        if (string.IsNullOrEmpty(driverVersionString) || !Version.TryParse(driverVersionString, out var driverVer))
            return null;

        return driverVer.Major switch
        {
            6 => "XP", //XDDM
            7 => "Vista",
            8 => "7",
            9 => "8",
            10 => "8.1",
            >= 20 and < 40 => driverVer.Major switch
            {
                // see https://en.wikipedia.org/wiki/Windows_Display_Driver_Model#WDDM_2.0
                20 => "10",
                21 => "10 1607",
                22 => "10 1703",
                23 => "10 1709",
                24 => "10 1803",
                25 => "10 1809",
                26 => "10 1903",
                27 => "10 2004",
                28 => "10 20H1 Preview",
                29 => "10 21H1",
                30 => "11 21H2",
                31 => "11 22H2",
                _ => null,
            },
            _ => null,
        };
    }

    private static string? GetWindowsVersion(Version windowsVersion) =>
        windowsVersion.Major switch
        {
            5 => windowsVersion.Minor switch
            {
                0 => "2000",
                1 => "XP",
                2 => "XP x64",
                _ => null,
            },
            6 => windowsVersion.Minor switch
            {
                0 => "Vista",
                1 => "7",
                2 => "8",
                3 => "8.1",
                _ => null
            },
            10 => windowsVersion.Build switch
            {
                < 10240 => "10 TH1 Build " + windowsVersion.Build,
                10240 => "10 1507",
                < 10586 => "10 TH2 Build " + windowsVersion.Build,
                10586 => "10 1511",
                < 14393 => "10 RS1 Build " + windowsVersion.Build,
                14393 => "10 1607",
                < 15063 => "10 RS2 Build " + windowsVersion.Build,
                15063 => "10 1703",
                < 16299 => "10 RS3 Build " + windowsVersion.Build,
                16299 => "10 1709",
                < 17134 => "10 RS4 Build " + windowsVersion.Build,
                17134 => "10 1803",
                < 17763 => "10 RS5 Build " + windowsVersion.Build,
                17763 => "10 1809",
                < 18362 => "10 19H1 Build " + windowsVersion.Build,
                18362 => "10 1903",
                18363 => "10 1909",
                < 19041 => "10 20H1 Build " + windowsVersion.Build,
                19041 => "10 2004",
                19042 => "10 20H2",
                19043 => "10 21H1",
                19044 => "10 21H2",
                < 21390 => "10 Dev Build " + windowsVersion.Build,
                21390 => "10 21H2 Insider",
                < 22000 => "11 Internal Build " + windowsVersion.Build,
                22000 => "11 21H2",
                < 22621 => "11 22H2 Insider Build " + windowsVersion.Build,
                22621 => "11 22H2",
                _ => "11 Dev Build " + windowsVersion.Build,
            },
            _ => null,
        };

    private static string? GetLinuxVersion(string? osType, string? release, string version)
    {
        if (string.IsNullOrEmpty(release))
            return null;
            
        var kernelVersion = release;
        if (LinuxKernelVersion.Match(release) is {Success: true} m)
            kernelVersion = m.Groups["version"].Value;
        if (version.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase))
            return "Ubuntu " + kernelVersion;

        if (version.Contains("Debian", StringComparison.OrdinalIgnoreCase))
            return "Debian " + kernelVersion;

        if (release.Contains("-MANJARO", StringComparison.OrdinalIgnoreCase))
            return "Manjaro " + kernelVersion;

        if (release.Contains("-ARCH", StringComparison.OrdinalIgnoreCase))
            return "Arch " + kernelVersion;

        if (release.Contains("-gentoo", StringComparison.OrdinalIgnoreCase))
            return "Gentoo " + kernelVersion;

        if (release.Contains("-valve", StringComparison.OrdinalIgnoreCase))
            return "SteamOS" + kernelVersion;

        if (release.Contains(".fc"))
        {
            var ver = release.Split('.').FirstOrDefault(p => p.StartsWith("fc"))?[2..];
            return "Fedora " + ver;
        }

        return $"{osType} {kernelVersion}";
    }

    private static string? GetMacOsVersion(Version macVer)
        => macVer.Major switch
        {
            10 => macVer.Minor switch
            {
                0 => "Mac OS X Cheetah",
                1 => "Mac OS X Puma",
                2 => "Mac OS X Jaguar",
                3 => "Mac OS X Panther",
                4 => "Mac OS X Tiger",
                5 => "Mac OS X Leopard",
                6 => "Mac OS X Snow Leopard",
                7 => "OS X Lion",
                8 => "OS X Mountain Lion",
                9 => "OS X Mavericks",
                10 => "OS X Yosemite",
                11 => "OS X El Capitan",
                12 => "macOS Sierra",
                13 => "macOS High Sierra",
                14 => "macOS Mojave",
                15 => "macOS Catalina",
                _ => null,
            },
            11 => "macOS Big Sur",
            12 => "macOS Monterey",
            13 => "macOS Ventura",
            _ => null,
        };

    internal static bool IsAmd(string gpuInfo)
    {
        return gpuInfo.Contains("Radeon", StringComparison.InvariantCultureIgnoreCase) ||
               gpuInfo.Contains("AMD", StringComparison.InvariantCultureIgnoreCase) ||
               gpuInfo.Contains("ATI ", StringComparison.InvariantCultureIgnoreCase);
    }

    internal static bool IsNvidia(string gpuInfo)
    {
        return gpuInfo.Contains("GeForce", StringComparison.InvariantCultureIgnoreCase) ||
               gpuInfo.Contains("nVidia", StringComparison.InvariantCultureIgnoreCase) ||
               gpuInfo.Contains("Quadro", StringComparison.InvariantCultureIgnoreCase) ||
               gpuInfo.Contains("GTX", StringComparison.InvariantCultureIgnoreCase);
    }

    private static string GetTimeFormat(long microseconds)
    {
        if (microseconds < 1000)
            return $"{microseconds} µs";
        if (microseconds < 1_000_000)
            return $"{microseconds / 1000.0:0.##} ms";
        return $"{microseconds / 1_000_000.0:0.##} s";
    }

    private static List<string> SortLines(List<string> notes, DiscordEmoji? piracyEmoji = null)
    {
        if (notes.Count < 2)
            return notes;

        var priorityList = new List<string>(EmojiPriority);
        if (piracyEmoji != null)
            priorityList.Insert(0, piracyEmoji.ToString());
        return notes
            .Select(s =>
            {
                var prioritySymbol = s.Split(PrioritySeparator, 2)[0];
                var priority = priorityList.IndexOf(prioritySymbol);
                return new
                {
                    priority = priority == -1 ? 69 : priority,
                    line = s
                };
            })
            .OrderBy(i => i.priority)
            .Select(i => i.line)
            .ToList();
    }

    private static Dictionary<string, int> GetPatches(UniqueList<string> patchList, in bool onlyApplied)
    {
        if (!patchList.Any())
            return new();

        var result = new Dictionary<string, int>(patchList.Count);
        foreach (var patch in patchList)
        {
            var match = ProgramHashPatch.Match(patch);
            if (match.Success)
            {
                _ = int.TryParse(match.Groups["patch_count"].Value, out var pCount);
                if (!onlyApplied || pCount > 0)
                    result[match.Groups["hash"].Value] = pCount;
            }
        }
        return result;
    }

    internal static DiscordEmbedBuilder AddAuthor(this DiscordEmbedBuilder builder, DiscordClient client, DiscordMessage? message, ISource source, LogParseState? state = null)
    {
        if (message == null || state?.Error == LogParseState.ErrorCode.PiracyDetected)
            return builder;

        var author = message.Author;
        var member = client.GetMember(message.Channel?.Guild, author);
        string msg;
        if (member == null)
            msg = $"Log from {author.Username.Sanitize()} | {author.Id}\n";
        else
            msg = $"Log from {member.DisplayName.Sanitize()} | {member.Id}\n";
        msg += " | " + source.SourceType;
        if (state?.ReadBytes > 0 && source.LogFileSize > 0 && source.LogFileSize < 2L*1024*1024*1024 && state.ReadBytes <= source.LogFileSize)
            msg += $" | Parsed {state.ReadBytes * 100.0 / source.LogFileSize:0.##}%";
        else if (source.SourceFilePosition > 0 && source.SourceFileSize > 0 && source.SourceFilePosition <= source.SourceFileSize)
            msg += $" | Read {source.SourceFilePosition * 100.0 / source.SourceFileSize:0.##}%";
        else if (state?.ReadBytes > 0)
            msg += $" | Parsed {state.ReadBytes} byte{(state.ReadBytes == 1 ? "" : "s")}";
        else if (source.LogFileSize > 0)
            msg += $" | {source.LogFileSize} byte{(source.LogFileSize == 1 ? "" : "s")}";
#if DEBUG
        if (state?.ParsingTime.TotalMilliseconds > 0)
            msg += $" | {state.ParsingTime.TotalSeconds:0.###}s";
        msg += " | Test Bot Instance";
#endif
        builder.WithFooter(msg);
        return builder;
    }

    private static (int numerator, int denumerator) Reduce(int numerator, int denumerator)
    {
        var gcd = Gcd(numerator, denumerator);
        return (numerator / gcd, denumerator / gcd);
    }

    private static int Gcd(int a, int b)
    {
        while (a != b)
        {
            if (a % b == 0)
                return b;

            if (b % a == 0)
                return a;

            if (a > b)
                a -= b;
            if (b > a)
                b -= a;
        }
        return a;
    }

    private static List<(string fatalError, int count, double similarity)> GroupSimilar(UniqueList<string> fatalErrors)
    {
        var result = new List<(string fatalError, int count, double similarity)>(fatalErrors.Count);
        if (fatalErrors.Count == 0)
            return result;

        result.Add((fatalErrors[0], 1, 1.0));
        if (fatalErrors.Count < 2)
            return result;

        foreach (var error in fatalErrors[1..])
        {
            var idx = -1;
            var similarity = 0.0;
            for (var i = 0; i < result.Count; i++)
            {
                similarity = result[i].fatalError.GetFuzzyCoefficientCached(error);
                if (similarity > 0.75) // spu worker X gives .9763 confidence, ppu thread XXXX gives about .9525
                {
                    idx = i;
                    break;
                }
            }
            if (idx == -1)
                result.Add((error, 1, 1.0));
            else
            {
                var (e, c, s) = result[idx];
                result[idx] = (e, c + 1, Math.Min(s, similarity));
            }
        }
        return result;
    }
}