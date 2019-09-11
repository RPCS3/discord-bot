using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Database;
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
         * If trigger is matched, then the associated regex will be run on THE WHOLE sliding window
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
                    ["RPCS3"] = new Regex(@"^\s*(?<build_and_specs>RPCS3.*)\r?$", DefaultSingleLineOptions),
                },
                OnNewLineAsync = PiracyCheckAsync,
                EndTrigger = new[] {"·"},
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["Physical device intialized"] = new Regex(@"Physical device intialized\. GPU=(?<vulkan_gpu>.+), driver=(?<vulkan_driver_version_raw>-?\d+)\r?$", DefaultOptions),
                    ["Found vulkan-compatible GPU:"] = new Regex(@"Found vulkan-compatible GPU: (?<vulkan_found_device>'(?<vulkan_compatible_device_name>.+)' running.+)\r?$", DefaultOptions),
                    ["Finished reading database from file:"] = new Regex(@"Finished reading database from file: (?<compat_database_path>.*compat_database.dat) }.*\r?$", DefaultOptions),
                    ["Serial:"] = new Regex(@"Serial: (?<serial>[A-z]{4}\d{5})\r?$", DefaultOptions),
                    ["Successfully installed PS3 firmware"] = new Regex(@"(?<fw_installed_message>Successfully installed PS3 firmware) version (?<fw_version_installed>\d+\.\d+).*\r?$", DefaultOptions),
                    ["Title:"] = new Regex(@"Title: (?<game_title>.*)?\r?$", DefaultOptions),
                    ["Category:"] = new Regex(@"Category: (?<game_category>.*)?\r?$", DefaultOptions),
                    ["LDR:"] = new Regex(@"(Path|Cache): ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions),
                    ["LDR: Path:"] = new Regex(@"Path: (?<ldr_path_full>.*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["custom config:"] = new Regex(@"custom config: (?<custom_config>.*?)\r?$", DefaultOptions),
                },
                OnNewLineAsync = PiracyCheckAsync,
                EndTrigger = new[] {"Core:"},
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["PPU Decoder:"] = new Regex(@"PPU Decoder: (?<ppu_decoder>.*?)\r?$", DefaultOptions),
                    ["PPU Threads:"] = new Regex(@"Threads: (?<ppu_threads>.*?)\r?$", DefaultOptions),
                    ["Use LLVM CPU:"] = new Regex("Use LLVM CPU: \\\"?(?<llvm_arch>.*?)\\\"?\r?$", DefaultOptions),
                    ["thread scheduler:"] = new Regex(@"scheduler: (?<thread_scheduler>.*?)\r?$", DefaultOptions),
                    ["SPU Decoder:"] = new Regex(@"SPU Decoder: (?<spu_decoder>.*?)\r?$", DefaultOptions),
                    ["secondary cores:"] = new Regex(@"secondary cores: (?<spu_secondary_cores>.*?)\r?$", DefaultOptions),
                    ["priority:"] = new Regex(@"priority: (?<spu_lower_thread_priority>.*?)\r?$", DefaultOptions),
                    ["SPU Threads:"] = new Regex(@"SPU Threads: (?<spu_threads>.*?)\r?$", DefaultOptions),
                    ["SPU delay penalty:"] = new Regex(@"SPU delay penalty: (?<spu_delay_penalty>.*?)\r?$", DefaultOptions),
                    ["SPU loop detection:"] = new Regex(@"SPU loop detection: (?<spu_loop_detection>.*?)\r?$", DefaultOptions),
                    ["Max SPURS Threads:"] = new Regex(@"Max SPURS Threads: (?<spurs_threads>\d*?)\r?$", DefaultOptions),
                    ["SPU Block Size:"] = new Regex(@"SPU Block Size: (?<spu_block_size>.*?)\r?$", DefaultOptions),
                    ["Enable TSX:"] = new Regex(@"Enable TSX: (?<enable_tsx>.*?)\r?$", DefaultOptions),
                    ["Accurate xfloat:"] = new Regex(@"Accurate xfloat: (?<accurate_xfloat>.*?)\r?$", DefaultOptions),
                    ["Approximate xfloat:"] = new Regex(@"Approximate xfloat: (?<approximate_xfloat>.*?)\r?$", DefaultOptions),
                    ["Debug Console Mode:"] = new Regex(@"Debug Console Mode: (?<debug_console_mode>.*?)\r?$", DefaultOptions),
                    ["Lib Loader:"] = new Regex(@"[Ll]oader: (?<lib_loader>.*?)\r?$", DefaultOptions),
                    ["Hook static functions:"] = new Regex(@"Hook static functions: (?<hook_static_functions>.*?)\r?$", DefaultOptions),
                    ["Load libraries:"] = new Regex(@"libraries:\r?\n(?<library_list>(.*?(- .*?|\[\])\r?\n)+)", DefaultOptions),
                    ["HLE lwmutex:"] = new Regex(@"HLE lwmutex: (?<hle_lwmutex>.*?)\r?$", DefaultOptions),
                    ["Clocks scale:"] = new Regex(@"Clocks scale: (?<clock_scale>.*?)\r?$", DefaultOptions),
                    ["Sleep Timers Accuracy:"] = new Regex(@"Sleep Timers Accuracy: (?<sleep_timer>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = new[] {"VFS:"},
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["Enable /host_root/:"] = new Regex(@"Enable /host_root/: (?<host_root>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = new[] {"Video:"},
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["Renderer:"] = new Regex("Renderer: (?<renderer>.*?)\r?$", DefaultOptions),
                    ["Resolution:"] = new Regex("Resolution: (?<resolution>.*?)\r?$", DefaultOptions),
                    ["Aspect ratio:"] = new Regex("Aspect ratio: (?<aspect_ratio>.*?)\r?$", DefaultOptions),
                    ["Frame limit:"] = new Regex("Frame limit: (?<frame_limit>.*?)\r?$", DefaultOptions),
                    ["MSAA:"] = new Regex("MSAA: (?<msaa>.*?)\r?$", DefaultOptions),
                    ["Write Color Buffers:"] = new Regex("Write Color Buffers: (?<write_color_buffers>.*?)\r?$", DefaultOptions),
                    ["Write Depth Buffer:"] = new Regex("Write Depth Buffer: (?<write_depth_buffer>.*?)\r?$", DefaultOptions),
                    ["Read Color Buffers:"] = new Regex("Read Color Buffers: (?<read_color_buffers>.*?)\r?$", DefaultOptions),
                    ["Read Depth Buffer:"] = new Regex("Read Depth Buffer: (?<read_depth_buffer>.*?)\r?$", DefaultOptions),
                    ["VSync:"] = new Regex("VSync: (?<vsync>.*?)\r?$", DefaultOptions),
                    ["GPU texture scaling:"] = new Regex("Use GPU texture scaling: (?<gpu_texture_scaling>.*?)\r?$", DefaultOptions),
                    ["Stretch To Display Area:"] = new Regex("Stretch To Display Area: (?<stretch_to_display>.*?)\r?$", DefaultOptions),
                    ["Strict Rendering Mode:"] = new Regex("Strict Rendering Mode: (?<strict_rendering_mode>.*?)\r?$", DefaultOptions),
                    ["Occlusion Queries:"] = new Regex("Occlusion Queries: (?<zcull>.*?)\r?$", DefaultOptions),
                    ["Vertex Cache:"] = new Regex("Disable Vertex Cache: (?<vertex_cache>.*?)\r?$", DefaultOptions),
                    ["Frame Skip:"] = new Regex("Enable Frame Skip: (?<frame_skip>.*?)\r?$", DefaultOptions),
                    ["Blit:"] = new Regex("Blit: (?<cpu_blit>.*?)\r?$", DefaultOptions),
                    ["Asynchronous Shader Compiler:"] = new Regex("Asynchronous Shader Compiler: (?<async_shaders>.*?)\r?$", DefaultOptions),
                    ["Multithreaded RSX:"] = new Regex("Multithreaded RSX: (?<mtrsx>.*?)\r?$", DefaultOptions),
                    ["Resolution Scale:"] = new Regex("Resolution Scale: (?<resolution_scale>.*?)\r?$", DefaultOptions),
                    ["Anisotropic Filter"] = new Regex("Anisotropic Filter Override: (?<af_override>.*?)\r?$", DefaultOptions),
                    ["Scalable Dimension:"] = new Regex("Minimum Scalable Dimension: (?<texture_scale_threshold>.*?)\r?$", DefaultOptions),
                    ["Driver Recovery Timeout:"] = new Regex("Driver Recovery Timeout: (?<driver_recovery_timeout>.*?)\r?$", DefaultOptions),
                    ["Vblank Rate:"] = new Regex("Vblank Rate: (?<vblank_rate>.*?)\r?$", DefaultOptions),
                    ["12:"] = new Regex(@"(D3D12|DirectX 12):\s*\r?\n\s*Adapter: (?<d3d_gpu>.*?)\r?$", DefaultOptions),
                    ["Vulkan:"] = new Regex(@"Vulkan:\s*\r?\n\s*Adapter: (?<vulkan_gpu>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = new[] {"Audio:"},
            },
            new LogSection // Audio, Input/Output, System, Net, Miscellaneous
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["Renderer:"] = new Regex("Renderer: (?<audio_backend>.*?)\r?$", DefaultOptions),
                    ["Downmix to Stereo:"] = new Regex("Downmix to Stereo: (?<audio_stereo>.*?)\r?$", DefaultOptions),
                    ["Master Volume:"] = new Regex("Master Volume: (?<audio_volume>.*?)\r?$", DefaultOptions),
                    ["Enable Buffering:"] = new Regex("Enable Buffering: (?<audio_buffering>.*?)\r?$", DefaultOptions),
                    ["Desired Audio Buffer Duration:"] = new Regex("Desired Audio Buffer Duration: (?<audio_buffer_duration>.*?)\r?$", DefaultOptions),
                    ["Enable Time Stretching:"] = new Regex("Enable Time Stretching: (?<audio_stretching>.*?)\r?$", DefaultOptions),

                    ["Pad:"] = new Regex("Pad: (?<pad_handler>.*?)\r?$", DefaultOptions),

                    ["Use native user interface:"] = new Regex("Use native user interface: (?<native_ui>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = new[] {"Log:"},
            },
            new LogSection 
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["Log:"] = new Regex(@"Log:\s*\r?\n?\s*(\{(?<log_disabled_channels>.*?)\}|(?<log_disabled_channels_multiline>(\s+\w+\:\s*\w+\r?\n)+))\r?$", DefaultOptions),
                },
                EndTrigger = new[] {"·"},
                OnSectionEnd = MarkAsComplete,
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["LDR: Game:"] = new Regex(@"Game: (?<ldr_game_full>.*(?<ldr_game>/dev_hdd0/game/(?<ldr_game_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["LDR: Disc"]  = new Regex(@"Disc( path)?: (?<ldr_disc_full>.*(?<ldr_disc>/dev_hdd0/game/(?<ldr_disc_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["LDR: Path:"] = new Regex(@"Path: (?<ldr_path_full>.*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["LDR: Boot path:"] = new Regex(@"Boot path: (?<ldr_boot_path_full>.*(?<ldr_boot_path>/dev_hdd0/game/(?<ldr_boot_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions),
                    ["Elf path:"] = new Regex(@"Elf path: (?<host_root_in_boot>/host_root/)?(?<elf_boot_path_full>(?<elf_boot_path>/dev_hdd0/game/(?<elf_boot_path_serial>[^/\r\n]+)/USRDIR/EBOOT\.BIN|.*?))\r?$", DefaultOptions),
                    ["Invalid or unsupported file format:"] = new Regex(@"Invalid or unsupported file format: (?<failed_to_boot>.*?)\r?$", DefaultOptions),
                    ["SELF:"] = new Regex(@"(?<failed_to_decrypt>Failed to decrypt)? SELF: (?<failed_to_decrypt>Failed to (decrypt|load SELF))?.*\r?$", DefaultOptions),
                    ["sceNp: npDrmIsAvailable(): Failed to verify"] = new Regex(@"Failed to verify (?<failed_to_verify>(sce|npd)) file.*\r?$", DefaultOptions),
                    ["{rsx::thread} RSX: 4"] = new Regex(@"RSX:(\d|\.|\s|\w|-)* (?<driver_version>(\d+\.)*\d+)\r?\n[^\n]*?" +
                                         @"RSX: [^\n]+\r?\n[^\n]*?" +
                                         @"RSX: (?<driver_manuf>.*?)\r?\n[^\n]*?" +
                                         @"RSX: Supported texel buffer size", DefaultOptions),
                    ["GL RENDERER:"] = new Regex(@"GL RENDERER: (?<driver_manuf_new>.*?)\r?$", DefaultOptions),
                    ["GL VERSION:"] = new Regex(@"GL VERSION: (?<opengl_version>(\d|\.)+)(\d|\.|\s|\w|-)*?( (?<driver_version_new>(\d+\.)*\d+))?\r?$", DefaultOptions),
                    ["GLSL VERSION:"] = new Regex(@"GLSL VERSION: (?<glsl_version>(\d|\.)+).*?\r?$", DefaultOptions),
                    ["texel buffer size reported:"] = new Regex(@"RSX: Supported texel buffer size reported: (?<texel_buffer_size_new>\d*?) bytes", DefaultOptions),
                    ["Physical device in"] = new Regex(@"Physical device ini?tialized\. GPU=(?<vulkan_gpu>.+), driver=(?<vulkan_driver_version_raw>-?\d+)\r?$", DefaultOptions),
                    ["Found vulkan-compatible GPU:"] = new Regex(@"Found vulkan-compatible GPU: (?<vulkan_found_device>.+)\r?$", DefaultOptions),
                    ["Renderer initialized on device"] = new Regex(@"Renderer initialized on device '(?<vulkan_initialized_device>.+)'\r?$", DefaultOptions),
                    ["RSX: Failed to compile shader"] = new Regex(@"RSX: Failed to compile shader: ERROR: (?<shader_compile_error>.+?)\r?$", DefaultOptions),
                    ["F "] = new Regex(@"F \d+:\d+:\d+\.\d+ ({(?<fatal_error_context>.+?)} )?(?<fatal_error>.*?(\:\W*\r?\n\(.*?)*)\r?$", DefaultOptions),
                    ["Failed to load RAP file:"] = new Regex(@"Failed to load RAP file: (?<rap_file>.*?\.rap).*\r?$", DefaultOptions),
                    ["Rap file not found:"] = new Regex(@"Rap file not found: (?<rap_file>.*?)\r?$", DefaultOptions),
                    ["Pad handler expected but none initialized"] = new Regex(@"(?<native_ui_input>Pad handler expected but none initialized).*?\r?$", DefaultOptions),
                    ["XAudio2Thread"] = new Regex(@"XAudio2Thread\s*: (?<xaudio_init_error>.+failed\s*\((?<xaudio_error_code>0x.+)\).*)\r?$", DefaultOptions),
                    ["cellAudio Thread"] = new Regex(@"XAudio2Backend\s*: (?<xaudio_init_error>.+failed\s*\((?<xaudio_error_code>0x.+)\).*)\r?$", DefaultOptions),
                    ["PPU executable hash:"] = new Regex(@"PPU executable hash: PPU-(?<ppu_hash>\w+) \(<-\s*(?<ppu_hash_patch>(?!0)\d+)\).*?\r?$", DefaultOptions),
                    ["OVL executable hash:"] = new Regex(@"OVL executable hash: OVL-(?<ovl_hash>\w+) \(<-\s*(?<ovl_hash_patch>(?!0)\d+)\).*?\r?$", DefaultOptions),
                    ["Loaded SPU image:"] = new Regex(@"Loaded SPU image: SPU-(?<spu_hash>\w+) \(<-\s*(?<spu_hash_patch>(?!0)\d+)\).*?\r?$", DefaultOptions),
                    ["'sys_fs_open' failed"] = new Regex(@"'sys_fs_open' failed .+\xE2\x80\x9C/dev_bdvd/(?<broken_filename>.+)\xE2\x80\x9D.*?\r?$", DefaultOptions),
                    ["'sys_fs_opendir' failed"] = new Regex(@"'sys_fs_opendir' failed .+\xE2\x80\x9C/dev_bdvd/(?<broken_directory>.+)\xE2\x80\x9D.*?\r?$", DefaultOptions),
                    ["LDR: EDAT: "] = new Regex(@"EDAT: Block at offset (?<edat_block_offset>0x[0-9a-f]+) has invalid hash!.*?\r?$", DefaultOptions),
                    ["PS3 firmware is not installed"] = new Regex(@"(?<fw_missing_msg>PS3 firmware is not installed.+)\r?$", DefaultOptions),
                    ["do you have the PS3 firmware installed"] = new Regex(@"(?<fw_missing_something>do you have the PS3 firmware installed.*)\r?$", DefaultOptions),
                    ["Unimplemented syscall"] = new Regex(@"U \d+:\d+:\d+\.\d+ ({(?<unimplemented_syscall_context>.+?)} )?.*Unimplemented syscall (?<unimplemented_syscall>.*)\r?$", DefaultOptions),
                    ["Could not enqueue"] = new Regex(@"cellAudio: Could not enqueue buffer onto audio backend(?<enqueue_buffer_error>.).*\r?$", DefaultOptions),
                    ["Failed to bind device"] = new Regex(@"Failed to bind device (?<failed_pad>.+) to handler (?<failed_pad_handler>.+).*\r?$", DefaultOptions),
                    ["{PPU["] = new Regex(@"{PPU\[.+\]} (?<syscall_module>[^ :]+)( TODO)?: (?<syscall_name>[^ :]+?)\(.*\r?$", DefaultOptions),
                    ["undub"] =  new Regex(@"(\b|_)(?<game_mod>undub)(\b|_)", DefaultOptions | RegexOptions.IgnoreCase),
                },
                OnNewLineAsync = LimitedPiracyCheckAsync,
                OnSectionEnd = MarkAsCompleteAndReset,
                EndTrigger = new[] {"All threads stopped...", "LDR: Booting from"},
            }
        };

        public static readonly HashSet<string> MultiValueItems = new HashSet<string>
        {
            "rap_file",
            "vulkan_found_device",
            "vulkan_compatible_device_name",
            "ppu_hash",
            "ppu_hash_patch",
            "ovl_hash",
            "ovl_hash_patch",
            "spu_hash",
            "spu_hash_patch",
            "broken_filename",
            "broken_directory",
            "edat_block_offset",
            "failed_to_verify",
        };

        public static readonly string[] CountValueItems = {"enqueue_buffer_error"};

        private static async Task PiracyCheckAsync(string line, LogParseState state)
        {
            if (await ContentFilter.FindTriggerAsync(FilterContext.Log, line).ConfigureAwait(false) is Piracystring match
                && match.Actions.HasFlag(FilterAction.RemoveContent))
            {
                var m = match;
                if (line.Contains("not valid, removing from"))
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
                    if (fh.context.Length > line.Length)
                        state.FilterTriggers[m.Id] = (m, line.ToUtf8());
                }
                else
                {
                    var utf8line = line.ToUtf8();
                    state.FilterTriggers[m.Id] = (m, utf8line);
                    if (m.Actions.HasFlag(FilterAction.IssueWarning))
                        state.Error = LogParseState.ErrorCode.PiracyDetected;
                }
            }
        }

        private static Task LimitedPiracyCheckAsync(string line, LogParseState state)
        {
            if (state.LinesAfterConfig > 10)
                return Task.CompletedTask;

            state.LinesAfterConfig++;
            return PiracyCheckAsync(line, state);
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
                "build_and_specs", "fw_version_installed",
                "vulkan_gpu", "d3d_gpu",
                "driver_version", "driver_manuf",
                "driver_manuf_new", "driver_version_new",
                "vulkan_found_device", "vulkan_compatible_device_name",
                "vulkan_gpu", "vulkan_driver_version_raw",
                "compat_database_path"
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
