using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers.LogParsing.POCOs;

namespace CompatBot.EventHandlers.LogParsing
{
    internal partial class LogParser
    {
        private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.ExplicitCapture;

        /*
         * Extractors are defined in terms of trigger-extractor
         *
         * Parser scans the log from section to section with a sliding window of up to 50 lines of text
         * Triggers are scanned for in the first line of said sliding window
         * If trigger is matched, then the associated reges will be run on THE WHOLE sliding window
         * If any data was captured, it will be stored in the current collection of items with the key of the explicit capture group of regex
         *
         * Due to limitations, REGEX can't contain anything other than ASCII (triggers CAN however)
         *
         */
        private static readonly List<LogSection> LogSections = new List<LogSection>
        {
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["RPCS3"] = new Regex(@"(?<build_and_specs>.*)\r?$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture),
                },
                EndTrigger = "·",
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["Serial:"] = new Regex(@"Serial: (?<serial>[A-z]{4}\d{5})\r?$", DefaultOptions),
                    ["Path:"] = new Regex(@"Path: ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions),
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
                    ["delay penalty:"] = new Regex("penalty: (?<spu_delay_penalty>.*?)\r?$", DefaultOptions),
                    ["loop detection:"] = new Regex("detection: (?<spu_loop_detection>.*?)\r?$", DefaultOptions),
                    ["Lib Loader:"] = new Regex("[Ll]oader: (?<lib_loader>.*?)\r?$", DefaultOptions),
                    ["static functions:"] = new Regex("functions: (?<hook_static_functions>.*?)\r?$", DefaultOptions),
                    ["Load libraries:"] = new Regex("libraries: (?<library_list>.*)", DefaultOptions),
                },
                EndTrigger = "VFS:",
            },
            new LogSection
            {
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
                    ["Vertex Cache:"] = new Regex("Disable Vertex Cache: (?<vertex_cache>.*?)\r?$", DefaultOptions),
                    ["Blit:"] = new Regex("Blit: (?<cpu_blit>.*?)\r?$", DefaultOptions),
                    ["Resolution Scale:"] = new Regex("Resolution Scale: (?<resolution_scale>.*?)\r?$", DefaultOptions),
                    ["Anisotropic Filter"] = new Regex("Anisotropic Filter Override: (?<af_override>.*?)\r?$", DefaultOptions),
                    ["Scalable Dimension:"] = new Regex("Minimum Scalable Dimension: (?<texture_scale_threshold>.*?)\r?$", DefaultOptions),
                    ["12:"] = new Regex(@"(D3D12|DirectX 12):\s*\r?\n\s*Adapter: (?<d3d_gpu>.*?)\r?$", DefaultOptions),
                    ["Vulkan:"] = new Regex(@"Vulkan:\s*\r?\n\s*Adapter: (?<vulkan_gpu>.*?)\r?$", DefaultOptions),
                },
                EndTrigger = "Audio:",
            },
            new LogSection
            {
                EndTrigger = "Log:",
                OnSectionEnd = MarkAsComplete,
            },
            new LogSection
            {
                Extractors = new Dictionary<string, Regex>
                {
                    ["RSX:"] = new Regex(@"RSX:(\d|\.|\s|\w|-)* (?<driver_version>(\d+\.)*\d+)\r?\n[^\n]*?" +
                                         @"RSX: [^\n]+\r?\n[^\n]*?" +
                                         @"RSX: (?<driver_manuf>.*?)\r?\n[^\n]*?" +
                                         @"RSX: Supported texel buffer size", DefaultOptions),
                    ["GL RENDERER:"] = new Regex(@"GL RENDERER: (?<driver_manuf_new>.*?)\r?\n", DefaultOptions),
                    ["GL VERSION:"] = new Regex(@"GL VERSION:(\d|\.|\s|\w|-)* (?<driver_version_new>(\d+\.)*\d+)\r?\n", DefaultOptions),
                    ["texel buffer size reported:"] = new Regex(@"RSX: Supported texel buffer size reported: (?<texel_buffer_size_new>\d*?) bytes", DefaultOptions),
                    ["·F "] = new Regex(@"F \d+:\d+:\d+\.\d+ {.+?} (?<fatal_error>.*?(\:\W*\r?\n\(.*?)*)\r?$", DefaultOptions),
                },
                OnSectionEnd = MarkAsCompleteAndReset,
                EndTrigger = "Objects cleared...",
            }
        };

        private static async Task PiracyCheckAsync(string line, LogParseState state)
        {
            if (await PiracyStringProvider.FindTriggerAsync(line).ConfigureAwait(false) is string match)
            {
                state.PiracyTrigger = match;
                state.PiracyContext = Utf8.GetString(Encoding.ASCII.GetBytes(line));
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
                "driver_manuf_new", "driver_version_new"
            );
        }

        private static void MarkAsComplete(LogParseState state)
        {
            state.CompleteCollection = state.WipCollection;
        }

        private static void MarkAsCompleteAndReset(LogParseState state)
        {
            MarkAsComplete(state);
            ClearResults(state);
            state.Id = -1;
        }
    }
}