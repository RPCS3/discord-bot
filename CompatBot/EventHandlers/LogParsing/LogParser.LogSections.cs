using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers.LogParsing.POCOs;
using CompatBot.Utils;

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
         * If trigger is matched, then the associated reges will be run on THE WHOLE sliding window
         * If any data was captured, it will be stored in the current collection of items with the key of the explicit capture group of regex
         *
         * Due to limitations, REGEX can't contain anything other than ASCII (including triggers)
         *
         */
        private static readonly List<LogSection> LogSections = new List<LogSection>
        {
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["RPCS3"] = new Regex(@"(?<build_and_specs>.*)\r?$", DefaultSingleLineOptions),
                },
                EndTrigger = "·",
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["Physical device intialized"] = new Regex(@"Physical device intialized\. GPU=(?<vulkan_gpu>.+), driver=(?<vulkan_driver_version_raw>-?\d+)\r?$", DefaultOptions),
                    ["Found vulkan-compatible GPU:"] = new Regex(@"Found vulkan-compatible GPU: (?<vulkan_found_device>'(?<vulkan_compatible_device_name>.+)' running.+)\r?$", DefaultOptions),
                    ["Serial:"] = new Regex(@"Serial: (?<serial>[A-z]{4}\d{5})\r?$", DefaultOptions),
                    ["Successfully installed PS3 firmware"] = new Regex(@"(?<fw_installed_message>Successfully installed PS3 firmware version (?<fw_version_installed>\d+\.\d+)).*\r?$", DefaultOptions),
                    ["Title:"] = new Regex(@"Title: (?<game_title>.*)?\r?$", DefaultOptions),
                    ["Category:"] = new Regex(@"Category: (?<game_category>.*)?\r?$", DefaultOptions),
                    ["LDR:"] = new Regex(@"(Path|Cache): ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions),
                    ["custom config:"] = new Regex("custom config: (?<custom_config>.*?)\r?$", DefaultOptions),
                },
                OnNewLineAsync = PiracyCheckAsync,
                EndTrigger = "Core:",
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["PPU Decoder:"] = new Regex("PPU Decoder: (?<ppu_decoder>.*?)\r?$", DefaultOptions),
                    ["PPU Threads:"] = new Regex("Threads: (?<ppu_threads>.*?)\r?$", DefaultOptions),
                    ["thread scheduler:"] = new Regex("scheduler: (?<thread_scheduler>.*?)\r?$", DefaultOptions),
                    ["SPU Decoder:"] = new Regex("SPU Decoder: (?<spu_decoder>.*?)\r?$", DefaultOptions),
                    ["secondary cores:"] = new Regex("secondary cores: (?<spu_secondary_cores>.*?)\r?$", DefaultOptions),
                    ["priority:"] = new Regex("priority: (?<spu_lower_thread_priority>.*?)\r?$", DefaultOptions),
                    ["SPU Threads:"] = new Regex("SPU Threads: (?<spu_threads>.*?)\r?$", DefaultOptions),
                    ["SPU delay penalty:"] = new Regex("SPU delay penalty: (?<spu_delay_penalty>.*?)\r?$", DefaultOptions),
                    ["SPU loop detection:"] = new Regex("SPU loop detection: (?<spu_loop_detection>.*?)\r?$", DefaultOptions),
                    ["SPU Block Size:"] = new Regex("SPU Block Size: (?<spu_block_size>.*?)\r?$", DefaultOptions),
                    ["Accurate xfloat:"] = new Regex("Accurate xfloat: (?<accurate_xfloat>.*?)\r?$", DefaultOptions),
                    ["Lib Loader:"] = new Regex("[Ll]oader: (?<lib_loader>.*?)\r?$", DefaultOptions),
                    ["Hook static functions:"] = new Regex("Hook static functions: (?<hook_static_functions>.*?)\r?$", DefaultOptions),
                    ["Load libraries:"] = new Regex(@"libraries:\r?\n(?<library_list>(.*?(- .*?|\[\])\r?\n)+)", DefaultOptions),
                    ["HLE lwmutex:"] = new Regex(@"HLE lwmutex: (?<hle_lwmutex>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = "VFS:",
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["Enable /host_root/:"] = new Regex("Enable /host_root/: (?<host_root>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = "Video:",
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["Renderer:"] = new Regex("Renderer: (?<renderer>.*?)\r?$", DefaultOptions),
                    ["Resolution:"] = new Regex("Resolution: (?<resolution>.*?)\r?$", DefaultOptions),
                    ["Aspect ratio:"] = new Regex("Aspect ratio: (?<aspect_ratio>.*?)\r?$", DefaultOptions),
                    ["Frame limit:"] = new Regex("Frame limit: (?<frame_limit>.*?)\r?$", DefaultOptions),
                    ["Write Color Buffers:"] = new Regex("Write Color Buffers: (?<write_color_buffers>.*?)\r?$", DefaultOptions),
                    ["VSync:"] = new Regex("VSync: (?<vsync>.*?)\r?$", DefaultOptions),
                    ["GPU texture scaling:"] = new Regex("Use GPU texture scaling: (?<gpu_texture_scaling>.*?)\r?$", DefaultOptions),
                    ["Strict Rendering Mode:"] = new Regex("Strict Rendering Mode: (?<strict_rendering_mode>.*?)\r?$", DefaultOptions),
                    ["Occlusion Queries:"] = new Regex("Occlusion Queries: (?<zcull>.*?)\r?$", DefaultOptions),
                    ["Vertex Cache:"] = new Regex("Disable Vertex Cache: (?<vertex_cache>.*?)\r?$", DefaultOptions),
                    ["Blit:"] = new Regex("Blit: (?<cpu_blit>.*?)\r?$", DefaultOptions),
                    ["Asynchronous Shader Compiler:"] = new Regex("Asynchronous Shader Compiler: (?<async_shaders>.*?)\r?$", DefaultOptions),
                    ["Resolution Scale:"] = new Regex("Resolution Scale: (?<resolution_scale>.*?)\r?$", DefaultOptions),
                    ["Anisotropic Filter"] = new Regex("Anisotropic Filter Override: (?<af_override>.*?)\r?$", DefaultOptions),
                    ["Scalable Dimension:"] = new Regex("Minimum Scalable Dimension: (?<texture_scale_threshold>.*?)\r?$", DefaultOptions),
                    ["Driver Recovery Timeout:"] = new Regex("Driver Recovery Timeout: (?<driver_recovery_timeout>.*?)\r?$", DefaultOptions),
                    ["12:"] = new Regex(@"(D3D12|DirectX 12):\s*\r?\n\s*Adapter: (?<d3d_gpu>.*?)\r?$", DefaultOptions),
                    ["Vulkan:"] = new Regex(@"Vulkan:\s*\r?\n\s*Adapter: (?<vulkan_gpu>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = "Audio:",
            },
            new LogSection // Audio, Input/Output, System, Net, Miscellaneous
            {
                EndTrigger = "Log:",
            },
            new LogSection 
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["Log:"] = new Regex(@"Log:\s*\r?\n?\s*\{(?<log_disabled_channels>.*?)\}\r?$", DefaultOptions),
                },
                EndTrigger = "·",
                OnSectionEnd = MarkAsComplete,
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["LDR: Game:"] = new Regex(@"Game: .*(?<ldr_game>/dev_hdd0/game/(?<ldr_game_serial>[^/]+).*?)\r?$", DefaultOptions),
                    ["LDR: Disc"]  = new Regex(@"Disc( path)?: .*(?<ldr_disc>/dev_hdd0/game/(?<ldr_disc_serial>[^/]+).*?)\r?$", DefaultOptions),
                    ["LDR: Path:"] = new Regex(@"Path: .*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/]+).*?)\r?$", DefaultOptions),
                    ["Elf path:"] = new Regex(@"Elf path: (?<host_root_in_boot>/host_root/)?(?<elf_boot_path>.*?)\r?$", DefaultOptions),
                    ["Invalid or unsupported file format:"] = new Regex(@"Invalid or unsupported file format: (?<failed_to_boot>.*?)\r?$", DefaultOptions),
                    ["SELF:"] = new Regex(@"(?<failed_to_decrypt>Failed to decrypt)? SELF: (?<failed_to_decrypt>Failed to (decrypt|load SELF))?.*\r?$", DefaultOptions),
                    ["RSX:"] = new Regex(@"RSX:(\d|\.|\s|\w|-)* (?<driver_version>(\d+\.)*\d+)\r?\n[^\n]*?" +
                                         @"RSX: [^\n]+\r?\n[^\n]*?" +
                                         @"RSX: (?<driver_manuf>.*?)\r?\n[^\n]*?" +
                                         @"RSX: Supported texel buffer size", DefaultOptions),
                    ["GL RENDERER:"] = new Regex(@"GL RENDERER: (?<driver_manuf_new>.*?)\r?$", DefaultOptions),
                    ["GL VERSION:"] = new Regex(@"GL VERSION: (?<opengl_version>(\d|\.)+)(\d|\.|\s|\w|-)*?( (?<driver_version_new>(\d+\.)*\d+))?\r?$", DefaultOptions),
                    ["GLSL VERSION:"] = new Regex(@"GLSL VERSION: (?<glsl_version>(\d|\.)+).*?\r?$", DefaultOptions),
                    ["texel buffer size reported:"] = new Regex(@"RSX: Supported texel buffer size reported: (?<texel_buffer_size_new>\d*?) bytes", DefaultOptions),
                    ["Physical device intialized"] = new Regex(@"Physical device intialized\. GPU=(?<vulkan_gpu>.+), driver=(?<vulkan_driver_version_raw>-?\d+)\r?$", DefaultOptions),
                    ["Found vulkan-compatible GPU:"] = new Regex(@"Found vulkan-compatible GPU: (?<vulkan_found_device>.+)\r?$", DefaultOptions),
                    ["Renderer initialized on device"] = new Regex(@"Renderer initialized on device '(?<vulkan_initialized_device>.+)'\r?$", DefaultOptions),
                    ["RSX: Failed to compile shader"] = new Regex(@"RSX: Failed to compile shader: ERROR: (?<shader_compile_error>.+?)\r?$", DefaultOptions),
                    ["F "] = new Regex(@"F \d+:\d+:\d+\.\d+ {.+?} (?<fatal_error>.*?(\:\W*\r?\n\(.*?)*)\r?$", DefaultOptions),
                    ["Failed to load RAP file:"] = new Regex(@"Failed to load RAP file: (?<rap_file>.*?)\r?$", DefaultOptions),
                    ["Rap file not found:"] = new Regex(@"Rap file not found: (?<rap_file>.*?)\r?$", DefaultOptions),
                    ["Pad handler expected but none initialized"] = new Regex(@"(?<native_ui_input>Pad handler expected but none initialized).*?\r?$", DefaultOptions),
                    ["XAudio2Thread"] = new Regex(@"XAudio2Thread\s*: (?<xaudio_init_error>.+failed\s*\((?<xaudio_error_code>0x.+)\).*)\r?$", DefaultOptions),
                    ["cellAudio Thread"] = new Regex(@"XAudio2Backend\s*: (?<xaudio_init_error>.+failed\s*\((?<xaudio_error_code>0x.+)\).*)\r?$", DefaultOptions),
                    ["PPU executable hash:"] = new Regex(@"PPU executable hash: PPU-(?<ppu_hash>\w+) \(<-\s*(?<ppu_hash_patch>(?!0)\d+)\).*?\r?$", DefaultOptions),
                    ["Loaded SPU image:"] = new Regex(@"Loaded SPU image: SPU-(?<spu_hash>\w+) \(<-\s*(?<spu_hash_patch>(?!0)\d+)\).*?\r?$", DefaultOptions),
                    ["'sys_fs_open' failed"] = new Regex(@"'sys_fs_open' failed .+\xE2\x80\x9C/dev_bdvd/(?<broken_filename>.+)\xE2\x80\x9D.*?\r?$", DefaultOptions),
                    ["'sys_fs_opendir' failed"] = new Regex(@"'sys_fs_opendir' failed .+\xE2\x80\x9C/dev_bdvd/(?<broken_directory>.+)\xE2\x80\x9D.*?\r?$", DefaultOptions),
                    ["LDR: EDAT: "] = new Regex(@"EDAT: Block at offset (?<edat_block_offset>0x[0-9a-f]+) has invalid hash!.*?\r?$", DefaultOptions),
                    ["PS3 firmware is not installed"] = new Regex(@"(?<fw_missing_msg>PS3 firmware is not installed.+)\r?$", DefaultOptions),
                    ["do you have the PS3 firmware installed"] = new Regex(@"(?<fw_missing_something>do you have the PS3 firmware installed.*)\r?$", DefaultOptions),
                },
                OnSectionEnd = MarkAsCompleteAndReset,
                EndTrigger = "All threads stopped...",
            }
        };

        public static readonly HashSet<string> MultiValueItems = new HashSet<string>
        {
            "rap_file",
            "vulkan_found_device",
            "vulkan_compatible_device_name",
            "ppu_hash",
            "ppu_hash_patch",
            "spu_hash",
            "spu_hash_patch",
            "broken_filename",
            "broken_directory",
            "edat_block_offset",
        };

        private static async Task PiracyCheckAsync(string line, LogParseState state)
        {
            if (await PiracyStringProvider.FindTriggerAsync(line).ConfigureAwait(false) is string match)
            {
                state.PiracyTrigger = match;
                state.PiracyContext = line.ToUtf8();
                state.Error = LogParseState.ErrorCode.PiracyDetected;
            }
        }

        private static void ClearResults(LogParseState state)
        {
            void Copy(params string[] keys)
            {
                foreach (var key in keys)
                    if (state.CompleteCollection?[key] is string value)
                        state.WipCollection[key] = value;
            }
            state.WipCollection = new NameValueCollection();
            Copy(
                "build_and_specs",
                "vulkan_gpu", "d3d_gpu",
                "driver_version", "driver_manuf",
                "driver_manuf_new", "driver_version_new",
                "vulkan_found_device", "vulkan_compatible_device_name",
                "vulkan_gpu", "vulkan_driver_version_raw"
            );
#if DEBUG
            Console.WriteLine("===== cleared");
#endif
        }

        private static void MarkAsComplete(LogParseState state)
        {
            state.CompleteCollection = state.WipCollection;
#if DEBUG
            Console.WriteLine("----- complete section");
#endif
        }

        private static void MarkAsCompleteAndReset(LogParseState state)
        {
            MarkAsComplete(state);
            ClearResults(state);
            state.Id = -1;
        }
    }
}