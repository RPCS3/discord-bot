using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers.LogParsing.POCOs;
using CompatBot.Utils;
using CompatBot.Utils.ResultFormatters;

namespace CompatBot.EventHandlers.LogParsing
{
    internal partial class LogParser
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.ExplicitCapture;
        private const RegexOptions DefaultSingleLineOptions = RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture;

        /*
         * Extractors are defined in terms of trigger-extractor
         *
         * Parser scans the log from section to section with a sliding window of up to 50 lines of text
         * Triggers are scanned for in the first line of said sliding window
         * If trigger is matched, then the associated regex will be run on THE WHOLE sliding window
         * If any data was captured, it will be stored in the current collection of items with the key of the explicit capture group of regex
         *
         * Due to limitations, REGEX can't contain anything other than ASCII (including triggers)
         *
         */
        private static readonly List<LogSection> LogSections = new()
        {
            new LogSection
            {
                Extractors = new()
                {
                    ["RPCS3 v"] = new(@"(^|.+0:00:00\.000000)\s*(?<build_and_specs>RPCS3 [^\xC2\xB7]+?)\r?(\n\xC2\xB7|$)", DefaultSingleLineOptions),
                    ["0:00:00.000000"] = new(@"(?<first_unicode_dot>·|\xC2\xB7).+\r?$", DefaultOptions),
                    ["Operating system:"] = LogParserResult.OsInfoInLog,
                    ["Physical device intialized"] = new(@"Physical device intialized\. GPU=(?<vulkan_gpu>.+), driver=(?<vulkan_driver_version_raw>-?\d+)\r?$", DefaultOptions),
                    ["Found vulkan-compatible GPU:"] = new(@"Found vulkan-compatible GPU: (?<vulkan_found_device>'(?<vulkan_compatible_device_name>.+)' running.+)\r?$", DefaultOptions),
                    ["Finished reading database from file:"] = new(@"Finished reading database from file: (?<compat_database_path>.*compat_database.dat).*\r?$", DefaultOptions),
                    ["Database file not found:"] = new(@"Database file not found: (?<compat_database_path>.*compat_database.dat).*\r?$", DefaultOptions),
                    ["Successfully installed PS3 firmware"] = new(@"(?<fw_installed_message>Successfully installed PS3 firmware) version (?<fw_version_installed>\d+\.\d+).*\r?$", DefaultOptions),
                    ["Title:"] = new(@"(?:LDR|SYS): Title: (?<game_title>.*)?\r?$", DefaultOptions),
                    ["Serial:"] = new(@"Serial: (?<serial>[A-z]{4}\d{5})\r?$", DefaultOptions),
                    ["Category:"] = new(@"Category: (?<game_category>.*)?\r?$", DefaultOptions),
                    ["LDR: Version:"] = new(@"Version: (?<disc_app_version>\S+) / (?<disc_package_version>\S+).*?\r?$", DefaultOptions),
                    ["SYS: Version:"] = new(@"Version: (APP_VER=)?(?<disc_app_version>\S+) (/ |VERSION=)(?<disc_package_version>\S+).*?\r?$", DefaultOptions),
                    ["LDR: Cache"] = new(@"Cache: ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions),
                    ["SYS: Cache"] = new(@"Cache: ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions),
                    ["LDR: Path"] = new(@"Path: ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions),
                    ["SYS: Path"] = new(@"Path: ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions),
                    ["LDR: Path:"] = new(@"Path: (?<ldr_path_full>.*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["SYS: Path:"] = new(@"Path: (?<ldr_path_full>.*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["custom config:"] = new(@"custom config: (?<custom_config>.*?)\r?$", DefaultOptions),
                    ["patch_log: Failed to load patch file"] = new(@"patch_log: Failed to load patch file (?<patch_error_file>\S*)\r?\n.* line (?<patch_error_line>\d+), column (?<patch_error_column>\d+): (?<patch_error_text>.*?)$", DefaultOptions),
                },
                EndTrigger = new[] {"Used configuration:"},
            },
            new LogSection
            {
                Extractors = new()
                {
                    ["PPU Decoder:"] = new(@"PPU Decoder: (?<ppu_decoder>.*?)\r?$", DefaultOptions),
                    ["PPU Threads:"] = new(@"PPU Threads: (?<ppu_threads>.*?)\r?$", DefaultOptions),
                    ["Use LLVM CPU:"] = new("Use LLVM CPU: \\\"?(?<llvm_arch>.*?)\\\"?\r?$", DefaultOptions),
                    ["thread scheduler:"] = new(@"scheduler: (?<thread_scheduler>.*?)\r?$", DefaultOptions),
                    ["SPU Decoder:"] = new(@"SPU Decoder: (?<spu_decoder>.*?)\r?$", DefaultOptions),
                    ["secondary cores:"] = new(@"secondary cores: (?<spu_secondary_cores>.*?)\r?$", DefaultOptions),
                    ["priority:"] = new(@"priority: (?<spu_lower_thread_priority>.*?)\r?$", DefaultOptions),
                    ["SPU Threads:"] = new(@"SPU Threads: (?<spu_threads>.*?)\r?$", DefaultOptions),
                    ["SPU delay penalty:"] = new(@"SPU delay penalty: (?<spu_delay_penalty>.*?)\r?$", DefaultOptions),
                    ["SPU loop detection:"] = new(@"SPU loop detection: (?<spu_loop_detection>.*?)\r?$", DefaultOptions),
                    ["Max SPURS Threads:"] = new(@"Max SPURS Threads: (?<spurs_threads>\d*?)\r?$", DefaultOptions),
                    ["SPU Block Size:"] = new(@"SPU Block Size: (?<spu_block_size>.*?)\r?$", DefaultOptions),
                    ["Enable TSX:"] = new(@"Enable TSX: (?<enable_tsx>.*?)\r?$", DefaultOptions),
                    ["Accurate xfloat:"] = new(@"Accurate xfloat: (?<accurate_xfloat>.*?)\r?$", DefaultOptions),
                    ["Accurate GETLLAR:"] = new(@"Accurate GETLLAR: (?<accurate_getllar>.*?)\r?$", DefaultOptions),
                    ["Accurate PUTLLUC:"] = new(@"Accurate PUTLLUC: (?<accurate_putlluc>.*?)\r?$", DefaultOptions),
                    ["Accurate RSX reservation access:"] = new(@"Accurate RSX reservation access: (?<accurate_rsx_reservation>.*?)\r?$", DefaultOptions),
                    ["Approximate xfloat:"] = new(@"Approximate xfloat: (?<approximate_xfloat>.*?)\r?$", DefaultOptions),
                    ["Debug Console Mode:"] = new(@"Debug Console Mode: (?<debug_console_mode>.*?)\r?$", DefaultOptions),
                    ["Lib Loader:"] = new(@"[Ll]oader: (?<lib_loader>.*?)\r?$", DefaultOptions),
                    ["Hook static functions:"] = new(@"Hook static functions: (?<hook_static_functions>.*?)\r?$", DefaultOptions),
                    ["Load libraries:"] = new(@"libraries:\r?\n(?<library_list>(.*?(- .*?|\[\])\r?\n)+)", DefaultOptions),
                    ["Libraries Control:"] = new(@"Libraries Control:\r?\n(?<library_list>(.*?(- .*?|\[\])\r?\n)+)", DefaultOptions),
                    ["HLE lwmutex:"] = new(@"HLE lwmutex: (?<hle_lwmutex>.*?)\r?$", DefaultOptions),
                    ["Clocks scale:"] = new(@"Clocks scale: (?<clock_scale>.*?)\r?$", DefaultOptions),
                    ["Sleep Timers Accuracy:"] = new(@"Sleep Timers Accuracy: (?<sleep_timer>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = new[] {"VFS:"},
            },
            new LogSection
            {
                Extractors = new()
                {
                    ["Enable /host_root/:"] = new(@"Enable /host_root/: (?<host_root>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = new[] {"Video:"},
            },
            new LogSection
            {
                Extractors = new()
                {
                    ["Renderer:"] = new("Renderer: (?<renderer>.*?)\r?$", DefaultOptions),
                    ["Resolution:"] = new("Resolution: (?<resolution>.*?)\r?$", DefaultOptions),
                    ["Aspect ratio:"] = new("Aspect ratio: (?<aspect_ratio>.*?)\r?$", DefaultOptions),
                    ["Frame limit:"] = new("Frame limit: (?<frame_limit>.*?)\r?$", DefaultOptions),
                    ["MSAA:"] = new("MSAA: (?<msaa>.*?)\r?$", DefaultOptions),
                    ["Write Color Buffers:"] = new("Write Color Buffers: (?<write_color_buffers>.*?)\r?$", DefaultOptions),
                    ["Write Depth Buffer:"] = new("Write Depth Buffer: (?<write_depth_buffer>.*?)\r?$", DefaultOptions),
                    ["Read Color Buffers:"] = new("Read Color Buffers: (?<read_color_buffers>.*?)\r?$", DefaultOptions),
                    ["Read Depth Buffer:"] = new("Read Depth Buffer: (?<read_depth_buffer>.*?)\r?$", DefaultOptions),
                    ["VSync:"] = new("VSync: (?<vsync>.*?)\r?$", DefaultOptions),
                    ["GPU texture scaling:"] = new("Use GPU texture scaling: (?<gpu_texture_scaling>.*?)\r?$", DefaultOptions),
                    ["Stretch To Display Area:"] = new("Stretch To Display Area: (?<stretch_to_display>.*?)\r?$", DefaultOptions),
                    ["Strict Rendering Mode:"] = new("Strict Rendering Mode: (?<strict_rendering_mode>.*?)\r?$", DefaultOptions),
                    ["Occlusion Queries:"] = new("Occlusion Queries: (?<zcull>.*?)\r?$", DefaultOptions),
                    ["Vertex Cache:"] = new("Disable Vertex Cache: (?<vertex_cache>.*?)\r?$", DefaultOptions),
                    ["Frame Skip:"] = new("Enable Frame Skip: (?<frame_skip>.*?)\r?$", DefaultOptions),
                    ["Blit:"] = new("Blit: (?<cpu_blit>.*?)\r?$", DefaultOptions),
                    ["Disable Asynchronous Shader Compiler:"] = new("Disable Asynchronous Shader Compiler: (?<disable_async_shaders>.*?)\r?$", DefaultOptions),
                    ["Shader Mode:"] = new("Shader Mode: (?<shader_mode>.*?)\r?$", DefaultOptions),
                    ["Disable native float16 support:"] = new("Disable native float16 support: (?<disable_native_float16>.*?)\r?$", DefaultOptions),
                    ["Multithreaded RSX:"] = new("Multithreaded RSX: (?<mtrsx>.*?)\r?$", DefaultOptions),
                    ["Relaxed ZCULL Sync:"] = new("Relaxed ZCULL Sync: (?<relaxed_zcull>.*?)\r?$", DefaultOptions),
                    ["Resolution Scale:"] = new("Resolution Scale: (?<resolution_scale>.*?)\r?$", DefaultOptions),
                    ["Anisotropic Filter"] = new("Anisotropic Filter Override: (?<af_override>.*?)\r?$", DefaultOptions),
                    ["Scalable Dimension:"] = new("Minimum Scalable Dimension: (?<texture_scale_threshold>.*?)\r?$", DefaultOptions),
                    ["Driver Recovery Timeout:"] = new("Driver Recovery Timeout: (?<driver_recovery_timeout>.*?)\r?$", DefaultOptions),
                    ["Driver Wake-Up Delay:"] = new("Driver Wake-Up Delay: (?<driver_wakeup_delay>.*?)\r?$", DefaultOptions),
                    ["Vblank Rate:"] = new("Vblank Rate: (?<vblank_rate>.*?)\r?$", DefaultOptions),
                    ["12:"] = new(@"(D3D12|DirectX 12):\s*\r?\n\s*Adapter: (?<d3d_gpu>.*?)\r?$", DefaultOptions),
                    ["Vulkan:"] = new(@"Vulkan:\s*\r?\n\s*Adapter: (?<vulkan_gpu>.*?)\r?$", DefaultOptions),
                    ["Force FIFO present mode:"] = new(@"Force FIFO present mode: (?<force_fifo_present>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = new[] {"Audio:"},
            },
            new LogSection // Audio, Input/Output, System, Net, Miscellaneous
            {
                Extractors = new()
                {
                    ["Renderer:"] = new("Renderer: (?<audio_backend>.*?)\r?$", DefaultOptions),
                    ["Downmix to Stereo:"] = new("Downmix to Stereo: (?<audio_stereo>.*?)\r?$", DefaultOptions),
                    ["Master Volume:"] = new("Master Volume: (?<audio_volume>.*?)\r?$", DefaultOptions),
                    ["Enable Buffering:"] = new("Enable Buffering: (?<audio_buffering>.*?)\r?$", DefaultOptions),
                    ["Desired Audio Buffer Duration:"] = new("Desired Audio Buffer Duration: (?<audio_buffer_duration>.*?)\r?$", DefaultOptions),
                    ["Enable Time Stretching:"] = new("Enable Time Stretching: (?<audio_stretching>.*?)\r?$", DefaultOptions),

                    ["Pad:"] = new("Pad: (?<pad_handler>.*?)\r?$", DefaultOptions),

                    ["Automatically start games after boot:"] = new("Automatically start games after boot: (?<auto_start_on_boot>.*?)\r?$", DefaultOptions),
                    ["Always start after boot:"] = new("Always start after boot: (?<always_start_on_boot>.*?)\r?$", DefaultOptions),
                    ["Use native user interface:"] = new("Use native user interface: (?<native_ui>.*?)\r?$", DefaultOptions),
                    ["Silence All Logs:"] = new("Silence All Logs: (?<disable_logs>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = new[] {"Log:"},
            },
            new LogSection 
            {
                Extractors = new()
                {
                    ["Log:"] = new(@"Log:\s*\r?\n?\s*(\{(?<log_disabled_channels>.*?)\}|(?<log_disabled_channels_multiline>(\s+\w+\:\s*\w+\r?\n)+))\r?$", DefaultOptions),
                },
                EndTrigger = new[] {"·"},
                OnSectionEnd = MarkAsComplete,
            },
            new LogSection
            {
                Extractors = new()
                {
                    ["LDR: Game:"] = new(@"Game: (?<ldr_game_full>.*(?<ldr_game>/dev_hdd0/game/(?<ldr_game_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["LDR: Disc"]  = new(@"Disc( path)?: (?<ldr_disc_full>.*(?<ldr_disc>/dev_hdd0/game/(?<ldr_disc_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["LDR: Path:"] = new(@"Path: (?<ldr_path_full>.*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["LDR: Boot path:"] = new(@"Boot path: (?<ldr_boot_path_full>.*(?<ldr_boot_path>/dev_hdd0/game/(?<ldr_boot_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["SYS: Game:"] = new(@"Game: (?<ldr_game_full>.*(?<ldr_game>/dev_hdd0/game/(?<ldr_game_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["SYS: Path:"] = new(@"Path: (?<ldr_path_full>.*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["SYS: Boot path:"] = new(@"Boot path: (?<ldr_boot_path_full>.*(?<ldr_boot_path>/dev_hdd0/game/(?<ldr_boot_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["Elf path:"] = new(@"Elf path: (?<host_root_in_boot>/host_root/)?(?<elf_boot_path_full>(?<elf_boot_path>/dev_hdd0/game/(?<elf_boot_path_serial>[^/\r\n]+)/USRDIR/EBOOT\.BIN|.*?))\r?$", DefaultOptions),
                    ["Invalid or unsupported file format:"] = new(@"Invalid or unsupported file format: (?<failed_to_boot>.*?)\r?$", DefaultOptions),
                    ["SELF:"] = new(@"(?<failed_to_decrypt>Failed to decrypt)? SELF: (?<failed_to_decrypt>Failed to (decrypt|load SELF))?.*\r?$", DefaultOptions),
                    ["SYS: Version:"] = new(@"Version: (APP_VER=)?(?<disc_app_version>\S+) (/ |VERSION=)(?<disc_package_version>\S+).*?\r?$", DefaultOptions),
                    ["sceNp: npDrmIsAvailable(): Failed to verify"] = new(@"Failed to verify (?<failed_to_verify>(sce|npd)) file.*\r?$", DefaultOptions),
                    ["{rsx::thread} RSX: 4"] = new(@"RSX:(\d|\.|\s|\w|-)* (?<driver_version>(\d+\.)*\d+)\r?\n[^\n]*?" +
                                                   @"RSX: [^\n]+\r?\n[^\n]*?" +
                                                   @"RSX: (?<driver_manuf>.*?)\r?\n[^\n]*?" +
                                                   @"RSX: Supported texel buffer size", DefaultOptions),
                    ["GL RENDERER:"] = new(@"GL RENDERER: (?<driver_manuf_new>.*?)\r?$", DefaultOptions),
                    ["GL VERSION:"] = new(@"GL VERSION: (?<opengl_version>(\d|\.)+)(\d|\.|\s|\w|-)*?( (?<driver_version_new>(\d+\.)*\d+))?\r?$", DefaultOptions),
                    ["GLSL VERSION:"] = new(@"GLSL VERSION: (?<glsl_version>(\d|\.)+).*?\r?$", DefaultOptions),
                    ["texel buffer size reported:"] = new(@"RSX: Supported texel buffer size reported: (?<texel_buffer_size_new>\d*?) bytes", DefaultOptions),
                    ["Physical device in"] = new(@"Physical device ini?tialized\. GPU=(?<vulkan_gpu>.+), driver=(?<vulkan_driver_version_raw>-?\d+)\r?$", DefaultOptions),
                    ["Found vulkan-compatible GPU:"] = new(@"Found vulkan-compatible GPU: (?<vulkan_found_device>.+)\r?$", DefaultOptions),
                    ["Renderer initialized on device"] = new(@"Renderer initialized on device '(?<vulkan_initialized_device>.+)'\r?$", DefaultOptions),
                    ["RSX: Failed to compile shader"] = new(@"RSX: Failed to compile shader: ERROR: (?<shader_compile_error>.+?)\r?$", DefaultOptions),
                    ["RSX: Compilation failed"] = new(@"RSX: Compilation failed: ERROR: (?<shader_compile_error>.+?)\r?$", DefaultOptions),
                    ["RSX: Unsupported device"] = new(@"RSX: Unsupported device: (?<rsx_unsupported_gpu>.+)\..+?\r?$", DefaultOptions),
                    ["RSX: Your GPU does not support"] = new(@"RSX: Your GPU does not support (?<rsx_not_supported_feature>.+)\..+?\r?$", DefaultOptions),
                    ["RSX: GPU/driver lacks support"] = new(@"RSX: GPU/driver lacks support for (?<rsx_not_supported_feature>.+)\..+?\r?$", DefaultOptions),
                    ["RSX: Swapchain:"] = new(@"RSX: Swapchain: present mode (?<rsx_swapchain_mode>\d+?) in use.+?\r?$", DefaultOptions),
                    ["F "] = new(@"F \d+:\d+:\d+\.\d+ (({(?<fatal_error_context>[^}]+)} )?(\w+:\s*(Thread terminated due to fatal error: )?|(\w+:\s*)?(class [^\r\n]+ thrown: ))\r?\n?)(?<fatal_error>.*?)(\r?\n)(\r?\n|\xC2\xB7|$)", DefaultSingleLineOptions),
                    ["Failed to load RAP file:"] = new(@"Failed to load RAP file: (?<rap_file>.*?\.rap).*\r?$", DefaultOptions),
                    ["Rap file not found:"] = new(@"Rap file not found: (\xE2\x80\x9C)?(?<rap_file>.*?)(\xE2\x80\x9D)?\r?$", DefaultOptions),
                    ["Pad handler expected but none initialized"] = new(@"(?<native_ui_input>Pad handler expected but none initialized).*?\r?$", DefaultOptions),
                    ["Failed to bind device"] = new(@"Failed to bind device (?<failed_pad>.+) to handler (?<failed_pad_handler>.+).*\r?$", DefaultOptions),
                    ["Input:"] = new(@"Input: (?<pad_handler>.*?) device .+ connected\r?$", DefaultOptions),
                    ["XAudio2Thread"] = new(@"XAudio2Thread\s*: (?<xaudio_init_error>.+failed\s*\((?<xaudio_error_code>0x.+)\).*)\r?$", DefaultOptions),
                    ["cellAudio Thread"] = new(@"XAudio2Backend\s*: (?<xaudio_init_error>.+failed\s*\((?<xaudio_error_code>0x.+)\).*)\r?$", DefaultOptions),
                    ["using a Null renderer instead"] = new(@"Audio renderer (?<audio_backend_init_error>.+) could not be initialized\r?$", DefaultOptions),
                    ["PPU executable hash:"] = new(@"PPU executable hash: PPU-(?<ppu_patch>\w+ \(<-\s*\d+\)).*?\r?$", DefaultOptions),
                    ["OVL executable hash:"] = new(@"OVL executable hash: OVL-(?<ovl_patch>\w+ \(<-\s*\d+\)).*?\r?$", DefaultOptions),
                    ["SPU executable hash:"] = new(@"SPU executable hash: SPU-(?<spu_patch>\w+ \(<-\s*\d+\)).*?\r?$", DefaultOptions),
                    ["PRX library hash:"] = new(@"PRX library hash: PRX-(?<prx_patch>\w+-\d+ \(<-\s*\d+\)).*?\r?$", DefaultOptions),
                    ["OVL hash of"] = new(@"OVL hash of (\w|[\.\[\]])+: OVL-(?<ovl_patch>\w+ \(<-\s*\d+\)).*?\r?$", DefaultOptions),
                    ["PRX hash of"] = new(@"PRX hash of (\w|[\.\[\]])+: PRX-(?<prx_patch>\w+-\d+ \(<-\s*\d+\)).*?\r?$", DefaultOptions),
                    [": Applied patch"] = new(@"Applied patch \(hash='(?:\w{3}-\w+(-\d+)?)', description='(?<patch_desc>.+?)', author='(?:.+?)', patch_version='(?:.+?)', file_version='(?:.+?)'\) \(<- (?:[1-9]\d*)\).*\r?$", DefaultOptions),
                    ["Loaded SPU image:"] = new(@"Loaded SPU image: SPU-(?<spu_patch>\w+ \(<-\s*\d+\)).*?\r?$", DefaultOptions),
                    ["'sys_fs_stat' failed"] = new(@"'sys_fs_stat' failed (?!with 0x8001002c).+\xE2\x80\x9C(/dev_bdvd/(?<broken_filename_or_dir>.+)|/dev_hdd0/game/NP\w+/(?<broken_digital_filename>.+))\xE2\x80\x9D.*?\r?$", DefaultOptions),
                    ["'sys_fs_open' failed"] = new(@"'sys_fs_open' failed (?!with 0x8001002c).+\xE2\x80\x9C(/dev_bdvd/(?<broken_filename>.+)|/dev_hdd0/game/NP\w+/(?<broken_digital_filename>.+))\xE2\x80\x9D.*?\r?$", DefaultOptions),
                    ["'sys_fs_opendir' failed"] = new(@"'sys_fs_opendir' failed .+\xE2\x80\x9C/dev_bdvd/(?<broken_directory>.+)\xE2\x80\x9D.*?\r?$", DefaultOptions),
                    ["EDAT: "] = new(@"EDAT: Block at offset (?<edat_block_offset>0x[0-9a-f]+) has invalid hash!.*?\r?$", DefaultOptions),
                    ["PS3 firmware is not installed"] = new(@"(?<fw_missing_msg>PS3 firmware is not installed.+)\r?$", DefaultOptions),
                    ["do you have the PS3 firmware installed"] = new(@"(?<fw_missing_something>do you have the PS3 firmware installed.*)\r?$", DefaultOptions),
                    ["Unimplemented syscall"] = new(@"U \d+:\d+:\d+\.\d+ ({(?<unimplemented_syscall_context>.+?)} )?.*Unimplemented syscall (?<unimplemented_syscall>.*)\r?$", DefaultOptions),
                    ["Could not enqueue"] = new(@"cellAudio: Could not enqueue buffer onto audio backend(?<enqueue_buffer_error>.).*\r?$", DefaultOptions),
                    ["{PPU["] = new(@"{PPU\[.+\]} (?<log_channel>[^ :]+)( TODO)?: (?!\xE2\x80\x9C)(?<syscall_name>[^ :]+?)\(.*\r?$", DefaultOptions),
                    ["⁂"] = new(@"\xE2\x81\x82 (?<syscall_name>[^ :\[]+?) .*\r?$", DefaultOptions),
                    ["undub"] =  new(@"(\b|_)(?<game_mod>(undub|translation patch))(\b|_)", DefaultOptions | RegexOptions.IgnoreCase),
                },
                OnSectionEnd = MarkAsCompleteAndReset,
                EndTrigger = new[] { "Stopping emulator...", "All threads stopped...", "LDR: Booting from"},
            }
        };

        private static readonly HashSet<string> MultiValueItems = new()
        {
            "pad_handler",
            "fatal_error_context",
            "fatal_error",
            "rap_file",
            "vulkan_found_device",
            "vulkan_compatible_device_name",
            "ppu_patch",
            "ovl_patch",
            "spu_patch",
            "prx_patch",
            "patch_desc",
            "broken_filename_or_dir",
            "broken_filename",
            "broken_digital_filename",
            "broken_directory",
            "edat_block_offset",
            "failed_to_verify",
            "rsx_not_supported_feature",
        };

        private static readonly string[] CountValueItems = {"enqueue_buffer_error"};

        private static async Task PiracyCheckAsync(string line, LogParseState state)
        {
            if (await ContentFilter.FindTriggerAsync(FilterContext.Log, line).ConfigureAwait(false) is Piracystring match
                && match.Actions.HasFlag(FilterAction.RemoveContent))
            {
                var m = match;
                if (line.Contains("not valid, removing from")
                    || line.Contains("Invalid disc path"))
                    m = new Piracystring
                    {
                        Id = match.Id,
                        Actions = match.Actions & ~FilterAction.IssueWarning,
                        Context = match.Context,
                        CustomMessage = match.CustomMessage,
                        Disabled = match.Disabled,
                        ExplainTerm = match.ExplainTerm,
                        String = match.String,
                        ValidatingRegex = match.ValidatingRegex,
                    };
                if (state.FilterTriggers.TryGetValue(m.Id, out var fh))
                {
                    var updatedActions = fh.filter.Actions | m.Actions;
                    if (fh.context.Length > line.Length)
                    {
                        m.Actions = updatedActions;
                        state.FilterTriggers[m.Id] = (m, line.ToUtf8());
                    }
                    else
                        fh.filter.Actions = updatedActions;
                    if (updatedActions.HasFlag(FilterAction.IssueWarning))
                        state.Error = LogParseState.ErrorCode.PiracyDetected;
                }
                else
                {
                    var utf8Line = line.ToUtf8();
                    state.FilterTriggers[m.Id] = (m, utf8Line);
                    if (m.Actions.HasFlag(FilterAction.IssueWarning))
                        state.Error = LogParseState.ErrorCode.PiracyDetected;
                }
            }
        }

        private static void ClearResults(LogParseState state)
        {
            void Copy(params string[] keys)
            {
                foreach (var key in keys)
                {
                    if (state.CompletedCollection?[key] is string value)
                        state.WipCollection[key] = value;
                    if (state.CompleteMultiValueCollection?[key] is UniqueList<string> collection)
                        state.WipMultiValueCollection[key] = collection;
                }
            }
            state.WipCollection = new NameValueCollection();
            state.WipMultiValueCollection = new NameUniqueObjectCollection<string>();
            Copy(
                "build_and_specs", "fw_version_installed",
                "first_unicode_dot",
                "vulkan_gpu", "d3d_gpu",
                "driver_version", "driver_manuf",
                "driver_manuf_new", "driver_version_new",
                "vulkan_found_device", "vulkan_compatible_device_name",
                "vulkan_gpu", "vulkan_driver_version_raw",
                "compat_database_path"
            );
            Config.Log.Trace("===== cleared");
        }

        private static void MarkAsComplete(LogParseState state)
        {
            state.CompletedCollection = state.WipCollection;
            state.CompleteMultiValueCollection = state.WipMultiValueCollection;
            Config.Log.Trace("----- complete section");
        }

        private static void MarkAsCompleteAndReset(LogParseState state)
        {
            MarkAsComplete(state);
            ClearResults(state);
            state.Id = -1;
        }
    }
}
