using CompatBot.Database;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers.LogParsing.POCOs;

namespace CompatBot.EventHandlers.LogParsing;

internal partial class LogParser
{
    /*
     * Extractors are defined in terms of trigger-extractor
     *
     * Parser scans the log from section to section with a sliding window of up to 50 lines of text
     * Triggers are scanned for in the first line of said sliding window
     * If trigger is matched, then the associated regex will be run on THE WHOLE sliding window
     * If any data was captured, it will be stored in the current collection of items with the key of the explicit capture group of regex
     *
     */
    private static readonly List<LogSection> LogSections =
    [
        new()
        {
            Extractors = new()
            {
                ["RPCS3 v"] = Rpcs3LogHeader(),
                ["0:00:00.0"] = FirstLineWithDot(),
                ["Operating system:"] = OsInfo(),
                ["Current Time:"] = CurrentTime(),
                ["Installation ID:"] = InstallationId(),
                ["Physical device in"] = PhysicalDeviceName(),
                ["Found vulkan-compatible GPU:"] = VulkanDeviceName(),
                ["Finished reading database from file:"] = CompatDbFoundPath(),
                ["Database file not found:"] = CompatDbNotFoundPath(),
                ["Successfully installed PS3 firmware"] = FwInstallMessage(),
                ["Firmware version:"] = FwVersion(),
                ["Title:"] = GameTitle(),
                ["Serial:"] = GameSerial(),
                ["Category:"] = GameCategory(),
                ["LDR: Version:"] = DiscVersionLdr(),
                ["SYS: Version:"] = DiscVersionSys(),
                ["LDR: Cache"] = CachePathLdr(),
                ["SYS: Cache"] = CachePathSys(),
                ["LDR: Path"] = BootPathLdr(),
                ["SYS: Path"] = BootPathSys(),
                ["LDR: Path:"] = BootPathDigitalLdr(),
                ["SYS: Path:"] = BootPathDigitalSys(),
                ["custom config:"] = CustomConfigPath(),
                ["patch_log: Failed to load patch file"] = FailedPatchPath(),
                ["rpcs3.exe"] = UfcModFlag(),
            },
            EndTrigger = ["Used configuration:"],
        },

        new()
        {
            Extractors = new()
            {
                ["PPU Decoder:"] = PpuDecoderType(),
                ["PPU Threads:"] = PpuThreadCount(),
                ["Use LLVM CPU:"] = LlvmCpuArch(),
                ["thread scheduler"] = ThreadSchedulerMode(),
                ["SPU Decoder:"] = SpuDecoderType(),
                ["Disable SPU GETLLAR Spin Optimization:"] = DisableSpuGetllarSpinOptimization(),
                ["secondary cores:"] = SecondaryCores(),
                //["priority:"] = LowerThreadPriority(),
                ["SPU Threads:"] = SpuThreadCount(),
                ["SPU delay penalty:"] = SpuDelayPenalty(),
                ["SPU loop detection:"] = SpuLoopDetection(),
                ["Max SPURS Threads:"] = SpursThreadCount(),
                ["SPU Block Size:"] = SpuBlockSize(),
                ["Enable TSX:"] = TsxMode(),
                ["Accurate xfloat:"] = AccurateXfloat(),
                ["Approximate xfloat:"] = ApproximateXfloat(),
                ["Relaxed xfloat:"] = RelaxedXfloat(),
                ["XFloat Accuracy:"] = XfloatMode(),
                ["Accurate GETLLAR:"] = GetLlarMode(),
                ["Accurate PUTLLUC:"] = PutLlucMode(),
                ["Accurate RSX reservation access:"] = RsxReservationAccessMode(),
                ["RSX FIFO Accuracy:"] = RsxFifoMode(),
                ["Debug Console Mode:"] = DebugConsoleMode(),
                ["Lib Loader:"] = LibLoaderMode(),
                ["Hook static functions:"] = HookStaticFunctions(),
                ["Load libraries:"] = LoadLibrariesList(),
                ["Libraries Control:"] = LibrariesControlList(),
                ["HLE lwmutex:"] = HleLwmutex(),
                ["Clocks scale:"] = ClockScale(),
                ["Max CPU Preempt Count:"] = CpuPreemptCount(),
                ["Sleep Timers Accuracy:"] = SleepTimersMode(),
            },
            EndTrigger = ["VFS:"],
        },

        new()
        {
            Extractors = new()
            {
                ["Enable /host_root/:"] = EnableHostRoot(),
            },
            EndTrigger = ["Video:"],
        },

        new()
        {
            Extractors = new()
            {
                ["Renderer:"] = RendererBackend(),
                ["Resolution:"] = ResolutionMode(),
                ["Aspect ratio:"] = AspectRatioMode(),
                ["Frame limit:"] = FrameLimit(),
                ["MSAA:"] = MsaaMode(),
                ["Write Color Buffers:"] = Wcb(),
                ["Write Depth Buffer:"] = Wdb(),
                ["Read Color Buffers:"] = Rcb(),
                ["Read Depth Buffer:"] = Rdb(),
                ["VSync:"] = VsyncMode(),
                ["GPU texture scaling:"] = GpuTextureScaling(),
                ["Stretch To Display Area:"] = StretchToDisplay(),
                ["Strict Rendering Mode:"] = StrictRendering(),
                ["Occlusion Queries:"] = OcclusionQueriesMode(),
                ["Vertex Cache:"] = VertexCache(),
                ["Frame Skip:"] = FrameSkip(),
                ["Blit:"] = BlitMode(),
                ["Disable Asynchronous Shader Compiler:"] = DisableAsyncShaders(),
                ["Shader Mode:"] = ShaderMode(),
                ["Disable native float16 support:"] = DisableNativeF16(),
                ["Multithreaded RSX:"] = RsxMultithreadMode(),
                ["Relaxed ZCULL Sync:"] = RelaxedZcull(),
                ["Resolution Scale:"] = ResolutionScaling(),
                ["Anisotropic Filter"] = AnisoFilter(),
                ["Scalable Dimension:"] = ScalableDimensions(),
                ["Driver Recovery Timeout:"] = DriverRecoveryTimeout(),
                ["Driver Wake-Up Delay:"] = DriverWakeupDelay(),
                ["Vblank Rate:"] = VblankRate(),
                ["12:"] = SelectedD3d12Device(),
                ["Vulkan:"] = SelectedVulkanDevice(),
                ["Force FIFO present mode:"] = FifoPresentMode(),
                ["Asynchronous Texture Streaming"] = AsyncTextureStreaming(),
                ["Asynchronous Queue Scheduler:"] = AsyncQueueScheduler(),
            },
            EndTrigger = ["Audio:"],
        },

        new() // Audio, Input/Output, System, Net, Miscellaneous
        {
            Extractors = new()
            {
                ["Renderer:"] = AudioBackend(),
                ["Downmix to Stereo:"] = DownmixToStereo(),
                ["Master Volume:"] = MasterVolume(),
                ["Enable Buffering:"] = AudioBuffering(),
                ["Desired Audio Buffer Duration:"] = AudioBufferLength(),
                ["Enable Time Stretching:"] = AudioTimeStretching(),

                ["Pad:"] = GamepadType(),

                ["Automatically start games after boot:"] = AutoStartAfterBoot(),
                ["Always start after boot:"] = AlwaysStartAfterBoot(),
                ["Use native user interface:"] = NativeUIMode(),
                ["Silence All Logs:"] = SilenceAllLogs(),
            },
            EndTrigger = ["Log:"],
        },

        new()
        {
            Extractors = new()
            {
                ["Log:"] = LogChannelList(),
            },
            EndTrigger = ["·"],
            OnSectionEnd = MarkAsComplete,
        },

        new()
        {
            Extractors = new()
            {
                ["LDR: Game:"] = GamePathLdr(),
                ["LDR: Disc"] = DiscPathLdr(),
                ["LDR: Path:"] = DigitalPathLdr(),
                ["LDR: Boot path:"] = BootPathInBodyLdr(),
                ["SYS: Game:"] = GamePathSys(),
                ["SYS: Path:"] = DigitalPathSys(),
                ["SYS: Boot path:"] = BootPathInBodySys(),
                ["Elf path:"] = ElfPath(),
                ["VFS: Mounted path \"/dev_bdvd\""] = VfsMountPath(),
                ["Invalid or unsupported file format:"] = InvalidFileFormat(),
                ["SELF:"] = DecryptFailedSelfPath(),
                ["SYS: Version:"] = GameVersion(),
                ["sceNp: npDrmIsAvailable(): Failed to verify"] = FailedToVerifyNpDrm(),
                ["Failed to decrypt '"] = FailedToDecryptEdat(),
                ["{rsx::thread} RSX: 4"] = RsxDriverInfoLegacy(),
                ["{rsx::thread} RSX: 3"] = RsxDriverInfoLegacy(),
                ["GL RENDERER:"] = GlRenderer(),
                ["GL VERSION:"] = GlVersion(),
                ["GLSL VERSION:"] = GlslVersion(),
                ["texel buffer size reported:"] = GlTexelBufferSize(),
                ["Physical device in"] = PhysicalDeviceFound(),
                ["Found vulkan-compatible GPU:"] = VulkanDeviceFound(),
                ["Renderer initialized on device"] = RenderDeviceInitialized(),
                ["RSX: Failed to compile shader"] = FailedToCompileShader(),
                ["RSX: Compilation failed"] = ShaderCompilationFailed(),
                ["RSX: Linkage failed"] = ShaderLinkageFailed(),
                ["RSX: Unsupported device"] = UnsupportedDevice(),
                ["RSX: Your GPU does not support"] = UnsupportedDeviceFeatures(),
                ["RSX: GPU/driver lacks support"] = UnsupportedDriverFeatures(),
                ["RSX: Swapchain:"] = SwapchainMode(),
                ["RSX: ** Using"] = VkExtensions(),
                ["RSX: [CAPS] Using"] = GlExtensions(),
                ["F "] = FatalError(),
                ["Failed to load RAP file:"] = FailedToLoadRap(),
                ["Failed to locate the game license file:"] = FailedToLocateRap(),
                ["Rap file not found:"] = RapNotFound(),
                ["Pad handler expected but none initialized"] = MissingGamepad(),
                ["Failed to bind device"] = FailedToBindGamepad(),
                ["Input:"] = InputDeviceConnected(),
                ["XAudio2Thread"] = XAudio2Thread(),
                ["cellAudio Thread"] = CellAudioThread(),
                ["using a Null renderer instead"] = AudioBackendFailed(),
                ["PPU executable hash:"] = PpuHash(),
                ["OVL executable hash:"] = OvlHash(),
                ["SPU executable hash:"] = SpuHash(),
                ["PRX library hash:"] = PrxHash(),
                ["OVL hash of"] = OvlHash2(),
                ["PRX hash of"] = PrxHash2(),
                [": Applied patch"] = AppliedPatch(),
                ["Loaded SPU image:"] = SpuImageLoad(),
                ["'sys_fs_stat' failed"] = SysFsStatFailed(),
                ["'sys_fs_open' failed"] = SysFsOpenFailed(),
                ["'sys_fs_opendir' failed"] = SysFsOpenDirFailed(),
                ["EDAT: "] = InvalidEdat(),
                ["PS3 firmware is not installed"] = FwNotInstalled(),
                ["do you have the PS3 firmware installed"] = FwNotInstalled2(),
                ["Unimplemented syscall"] = UnimplementedSyscall(),
                ["Could not enqueue"] = CellAudioEnqueueFailed(),
                ["{PPU["] = PpuSyscallTodo(),
                ["Verification failed"] = VerificationFailed(),
                ["sys_tty_write():"] = SysTtyWrite(),
                ["⁂"] = SyscallDump(),
                ["undub"] = UndubFlag(),
                ["Input: Pad"] = InputDeviceGamepad(),
                ["SDL: Found game controller"] = SdlControllerName(),
            },
            OnSectionEnd = MarkAsCompleteAndReset,
            EndTrigger = ["Stopping emulator...", "All threads stopped...", "LDR: Booting from"],
        }
    ];

    private static readonly HashSet<string> MultiValueItems =
    [
        "pad_handler",
        "pad_controller_name",
        "pad_has_gyro",
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
        "failed_to_verify_npdrm",
        "failed_to_decrypt_edat",
        "rsx_not_supported_feature",
        "vk_ext",
        "gl_ext",
        "verification_error_hex",
        "verification_error",
        "tty_line",
    ];

    private static readonly string[] CountValueItems = ["enqueue_buffer_error"];

    private static async ValueTask PiracyCheckAsync(string line, LogParseState state)
    {
        if (await ContentFilter.FindTriggerAsync(FilterContext.Log, line).ConfigureAwait(false) is Piracystring match
            && match.Actions.HasFlag(FilterAction.RemoveContent))
        {
            var m = match;
            if (line.Contains("not valid, removing from")
                || line.Contains("Invalid disc path"))
                m = new()
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
        state.WipCollection = [];
        state.WipMultiValueCollection = new();
        Copy(
            "build_and_specs", "fw_version_installed",
            "log_start_timestamp", "hw_id",
            "os_type", "posix_name", "posix_release", "posix_version", "macos_version",
            "os_version_major", "os_version_minor", "os_version_build", "os_service_pack", "os_compat_mode",
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