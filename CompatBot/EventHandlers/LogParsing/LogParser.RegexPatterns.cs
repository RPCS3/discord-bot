using System.Text.RegularExpressions;

namespace CompatBot.EventHandlers.LogParsing;

internal partial class LogParser
{
    private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.ExplicitCapture;
    private const RegexOptions DefaultSingleLine = RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture;

    [GeneratedRegex(@"(^|.+\d:\d\d:\d\d\.\d{6})\s*(?<build_and_specs>RPCS3 [^\xC2\xB7]+?)\r?(\n·|$)", DefaultSingleLine)]
    private static partial Regex Rpcs3LogHeader();
    [GeneratedRegex(@"(?<first_unicode_dot>·).+\r?$", DefaultOptions)]
    private static partial Regex FirstLineWithDot();
    [GeneratedRegex(@"Operating system: (?<os_type>[^,]+), (Name: (?<posix_name>[^,]+), Release: (?<posix_release>[^,]+), Version: (?<posix_version>[^\r\n]+)|Major: (?<os_version_major>\d+), Minor: (?<os_version_minor>\d+), Build: (?<os_version_build>\d+), Service Pack: (?<os_service_pack>[^,]+), Compatibility mode: (?<os_compat_mode>[^,\r\n]+)|Version: (?<macos_version>[^\r\n]+))\r?$", DefaultSingleLine)]
    // Operating system: Windows, Major: 10, Minor: 0, Build: 22000, Service Pack: none, Compatibility mode: 0
    // Operating system: POSIX, Name: Linux, Release: 5.15.11-zen1-1-zen, Version: #1 ZEN SMP PREEMPT Wed, 22 Dec 2021 09:23:53 +0000
    // Operating system: macOS, Version 12.1.0
    internal static partial Regex OsInfo();
    [GeneratedRegex(@"Current Time: (?<log_start_timestamp>.+)\r?$", DefaultOptions)]
    private static partial Regex CurrentTime();
    [GeneratedRegex(@"Installation ID: (?<hw_id>.+)\r?$", DefaultOptions)]
    private static partial Regex InstallationId();
    [GeneratedRegex(@"Physical device ini?tialized\. GPU=(?<vulkan_gpu>.+), driver=(?<vulkan_driver_version_raw>-?\d+)\r?$", DefaultOptions)]
    private static partial Regex PhysicalDeviceName();
    [GeneratedRegex(@"Found [Vv]ulkan-compatible GPU: (?<vulkan_found_device>'(?<vulkan_compatible_device_name>.+)' running.+)\r?$", DefaultOptions)]
    private static partial Regex VulkanDeviceName();
    [GeneratedRegex(@"Finished reading database from file: (?<compat_database_path>.*compat_database.dat).*\r?$", DefaultOptions)]
    private static partial Regex CompatDbFoundPath();
    [GeneratedRegex(@"Database file not found: (?<compat_database_path>.*compat_database.dat).*\r?$", DefaultOptions)]
    private static partial Regex CompatDbNotFoundPath();
    [GeneratedRegex(@"(?<fw_installed_message>Successfully installed PS3 firmware) version (?<fw_version_installed>\d+\.\d+).*\r?$", DefaultOptions)]
    private static partial Regex FwInstallMessage();
    [GeneratedRegex(@"Firmware version: (?<fw_version_installed>\d+\.\d+).*\r?$", DefaultOptions)]
    private static partial Regex FwVersion();
    [GeneratedRegex(@"(?:LDR|SYS): Title: (?<game_title>.*)?\r?$", DefaultOptions)]
    private static partial Regex GameTitle();
    [GeneratedRegex(@"Serial: (?<serial>[A-z]{4}\d{5})\r?$", DefaultOptions)]
    private static partial Regex GameSerial();
    [GeneratedRegex(@"Category: (?<game_category>.*)?\r?$", DefaultOptions)]
    private static partial Regex GameCategory();
    [GeneratedRegex(@"Version: (?<disc_app_version>\S+) / (?<disc_package_version>\S+).*?\r?$", DefaultOptions)]
    private static partial Regex DiscVersionLdr();
    [GeneratedRegex(@"Version: (APP_VER=)?(?<disc_app_version>\S+) (/ |VERSION=)(?<disc_package_version>\S+).*?\r?$", DefaultOptions)]
    private static partial Regex DiscVersionSys();
    [GeneratedRegex(@"Cache: ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions)]
    private static partial Regex CachePathLdr();
    [GeneratedRegex(@"Cache: ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions)]
    private static partial Regex CachePathSys();
    [GeneratedRegex(@"Path: ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions)]
    private static partial Regex BootPathLdr();
    [GeneratedRegex(@"Path: ((?<win_path>\w:/)|(?<lin_path>/[^/])).*?\r?$", DefaultOptions)]
    private static partial Regex BootPathSys();
    [GeneratedRegex(@"Path: (?<ldr_path_full>.*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions)]
    private static partial Regex BootPathDigitalLdr();
    [GeneratedRegex(@"Path: (?<ldr_path_full>.*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions)]
    private static partial Regex BootPathDigitalSys();
    [GeneratedRegex(@"custom config: (?<custom_config>.*?)\r?$", DefaultOptions)]
    private static partial Regex CustomConfigPath();
    [GeneratedRegex(@"patch_log: Failed to load patch file (?<patch_error_file>\S*)\r?\n.* line (?<patch_error_line>\d+), column (?<patch_error_column>\d+): (?<patch_error_text>.*?)$", DefaultOptions)]
    private static partial Regex FailedPatchPath();
    
    [GeneratedRegex(@"PPU Decoder: (?<ppu_decoder>.*?)\r?$", DefaultOptions)]
    private static partial Regex PpuDecoderType();
    [GeneratedRegex(@"PPU Threads: (?<ppu_threads>.*?)\r?$", DefaultOptions)]
    private static partial Regex PpuThreadCount();
    [GeneratedRegex("Use LLVM CPU: \\\"?(?<llvm_arch>.*?)\\\"?\r?$", DefaultOptions)]
    private static partial Regex LlvmCpuArch();
    [GeneratedRegex(@"[Ss]cheduler( Mode)?: (?<thread_scheduler>.*?)\r?$", DefaultOptions)]
    private static partial Regex ThreadSchedulerMode();
    [GeneratedRegex(@"SPU Decoder: (?<spu_decoder>.*?)\r?$", DefaultOptions)]
    private static partial Regex SpuDecoderType();
    [GeneratedRegex(@"Disable SPU GETLLAR Spin Optimization: (?<disable_getllar_spin_optimization>.*?)\r?$", DefaultOptions)]
    private static partial Regex DisableSpuGetllarSpinOptimization();
    [GeneratedRegex(@"secondary cores: (?<spu_secondary_cores>.*?)\r?$", DefaultOptions)]
    private static partial Regex SecondaryCores();
    [GeneratedRegex(@"priority: (?<spu_lower_thread_priority>.*?)\r?$", DefaultOptions)]
    private static partial Regex LowerThreadPriority();
    [GeneratedRegex(@"SPU Threads: (?<spu_threads>.*?)\r?$", DefaultOptions)]
    private static partial Regex SpuThreadCount();
    [GeneratedRegex(@"SPU delay penalty: (?<spu_delay_penalty>.*?)\r?$", DefaultOptions)]
    private static partial Regex SpuDelayPenalty();
    [GeneratedRegex(@"SPU loop detection: (?<spu_loop_detection>.*?)\r?$", DefaultOptions)]
    private static partial Regex SpuLoopDetection();
    [GeneratedRegex(@"Max SPURS Threads: (?<spurs_threads>\d*?)\r?$", DefaultOptions)]
    private static partial Regex SpursThreadCount();
    [GeneratedRegex(@"SPU Block Size: (?<spu_block_size>.*?)\r?$", DefaultOptions)]
    private static partial Regex SpuBlockSize();
    [GeneratedRegex(@"Enable TSX: (?<enable_tsx>.*?)\r?$", DefaultOptions)]
    private static partial Regex TsxMode();
    [GeneratedRegex(@"Accurate xfloat: (?<accurate_xfloat>.*?)\r?$", DefaultOptions)]
    private static partial Regex AccurateXfloat();
    [GeneratedRegex(@"Approximate xfloat: (?<approximate_xfloat>.*?)\r?$", DefaultOptions)]
    private static partial Regex ApproximateXfloat();
    [GeneratedRegex(@"Relaxed xfloat: (?<relaxed_xfloat>.*?)\r?$", DefaultOptions)]
    private static partial Regex RelaxedXfloat();
    [GeneratedRegex(@"XFloat Accuracy: (?<xfloat_mode>.*?)\r?$", DefaultOptions)]
    private static partial Regex XfloatMode();
    [GeneratedRegex(@"Accurate GETLLAR: (?<accurate_getllar>.*?)\r?$", DefaultOptions)]
    private static partial Regex GetLlarMode();
    [GeneratedRegex(@"Accurate PUTLLUC: (?<accurate_putlluc>.*?)\r?$", DefaultOptions)]
    private static partial Regex PutLlucMode();
    [GeneratedRegex(@"Accurate RSX reservation access: (?<accurate_rsx_reservation>.*?)\r?$", DefaultOptions)]
    private static partial Regex RsxReservationAccessMode();
    [GeneratedRegex(@"RSX FIFO Accuracy: (?<rsx_fifo_mode>.*?)\r?$", DefaultOptions)]
    private static partial Regex RsxFifoMode();
    [GeneratedRegex(@"Debug Console Mode: (?<debug_console_mode>.*?)\r?$", DefaultOptions)]
    private static partial Regex DebugConsoleMode();
    [GeneratedRegex(@"[Ll]oader: (?<lib_loader>.*?)\r?$", DefaultOptions)]
    private static partial Regex LibLoaderMode();
    [GeneratedRegex(@"Hook static functions: (?<hook_static_functions>.*?)\r?$", DefaultOptions)]
    private static partial Regex HookStaticFunctions();
    [GeneratedRegex(@"libraries:\r?\n(?<library_list>(.*?(- .*?|\[\])\r?\n)+)", DefaultOptions)]
    private static partial Regex LoadLibrariesList();
    [GeneratedRegex(@"Libraries Control:\r?\n(?<library_list>(.*?(- .*?|\[\])\r?\n)+)", DefaultOptions)]
    private static partial Regex LibrariesControlList();
    [GeneratedRegex(@"HLE lwmutex: (?<hle_lwmutex>.*?)\r?$", DefaultOptions)]
    private static partial Regex HleLwmutex();
    [GeneratedRegex(@"Clocks scale: (?<clock_scale>.*?)\r?$", DefaultOptions)]
    private static partial Regex ClockScale();
    [GeneratedRegex(@"Max CPU Preempt Count: (?<cpu_preempt_count>.*?)\r?$", DefaultOptions)]
    private static partial Regex CpuPreemptCount();
    [GeneratedRegex(@"Sleep Timers Accuracy: (?<sleep_timer>.*?)\r?$", DefaultOptions)]
    private static partial Regex SleepTimersMode();
    
    [GeneratedRegex(@"Enable /host_root/: (?<host_root>.*?)\r?$", DefaultOptions)]
    private static partial Regex EnableHostRoot();

    [GeneratedRegex("Renderer: (?<renderer>.*?)\r?$", DefaultOptions)]
    private static partial Regex RendererBackend();
    [GeneratedRegex("Resolution: (?<resolution>.*?)\r?$", DefaultOptions)]
    private static partial Regex ResolutionMode();
    [GeneratedRegex("Aspect ratio: (?<aspect_ratio>.*?)\r?$", DefaultOptions)]
    private static partial Regex AspectRatioMode();
    [GeneratedRegex("Frame limit: (?<frame_limit>.*?)\r?$", DefaultOptions)]
    private static partial Regex FrameLimit();
    [GeneratedRegex("MSAA: (?<msaa>.*?)\r?$", DefaultOptions)]
    private static partial Regex MsaaMode();
    [GeneratedRegex("Write Color Buffers: (?<write_color_buffers>.*?)\r?$", DefaultOptions)]
    private static partial Regex Wcb();
    [GeneratedRegex("Write Depth Buffer: (?<write_depth_buffer>.*?)\r?$", DefaultOptions)]
    private static partial Regex Wdb();
    [GeneratedRegex("Read Color Buffers: (?<read_color_buffers>.*?)\r?$", DefaultOptions)]
    private static partial Regex Rcb();
    [GeneratedRegex("Read Depth Buffer: (?<read_depth_buffer>.*?)\r?$", DefaultOptions)]
    private static partial Regex Rdb();
    [GeneratedRegex("VSync: (?<vsync>.*?)\r?$", DefaultOptions)]
    private static partial Regex VsyncMode();
    [GeneratedRegex("Use GPU texture scaling: (?<gpu_texture_scaling>.*?)\r?$", DefaultOptions)]
    private static partial Regex GpuTextureScaling();
    [GeneratedRegex("Stretch To Display Area: (?<stretch_to_display>.*?)\r?$", DefaultOptions)]
    private static partial Regex StretchToDisplay();
    [GeneratedRegex("Strict Rendering Mode: (?<strict_rendering_mode>.*?)\r?$", DefaultOptions)]
    private static partial Regex StrictRendering();
    [GeneratedRegex("Occlusion Queries: (?<zcull>.*?)\r?$", DefaultOptions)]
    private static partial Regex OcclusionQueriesMode();
    [GeneratedRegex("Disable Vertex Cache: (?<vertex_cache>.*?)\r?$", DefaultOptions)]
    private static partial Regex VertexCache();
    [GeneratedRegex("Enable Frame Skip: (?<frame_skip>.*?)\r?$", DefaultOptions)]
    private static partial Regex FrameSkip();
    [GeneratedRegex("Blit: (?<cpu_blit>.*?)\r?$", DefaultOptions)]
    private static partial Regex BlitMode();
    [GeneratedRegex("Disable Asynchronous Shader Compiler: (?<disable_async_shaders>.*?)\r?$", DefaultOptions)]
    private static partial Regex DisableAsyncShaders();
    [GeneratedRegex("Shader Mode: (?<shader_mode>.*?)\r?$", DefaultOptions)]
    private static partial Regex ShaderMode();
    [GeneratedRegex("Disable native float16 support: (?<disable_native_float16>.*?)\r?$", DefaultOptions)]
    private static partial Regex DisableNativeF16();
    [GeneratedRegex("Multithreaded RSX: (?<mtrsx>.*?)\r?$", DefaultOptions)]
    private static partial Regex RsxMultithreadMode();
    [GeneratedRegex("Relaxed ZCULL Sync: (?<relaxed_zcull>.*?)\r?$", DefaultOptions)]
    private static partial Regex RelaxedZcull();
    [GeneratedRegex("Resolution Scale: (?<resolution_scale>.*?)\r?$", DefaultOptions)]
    private static partial Regex ResolutionScaling();
    [GeneratedRegex("Anisotropic Filter Override: (?<af_override>.*?)\r?$", DefaultOptions)]
    private static partial Regex AnisoFilter();
    [GeneratedRegex("Minimum Scalable Dimension: (?<texture_scale_threshold>.*?)\r?$", DefaultOptions)]
    private static partial Regex ScalableDimensions();
    [GeneratedRegex("Driver Recovery Timeout: (?<driver_recovery_timeout>.*?)\r?$", DefaultOptions)]
    private static partial Regex DriverRecoveryTimeout();
    [GeneratedRegex("Driver Wake-Up Delay: (?<driver_wakeup_delay>.*?)\r?$", DefaultOptions)]
    private static partial Regex DriverWakeupDelay();
    [GeneratedRegex("Vblank Rate: (?<vblank_rate>.*?)\r?$", DefaultOptions)]
    private static partial Regex VblankRate();
    [GeneratedRegex(@"(D3D12|DirectX 12):\s*\r?\n\s*Adapter: (?<d3d_gpu>.*?)\r?$", DefaultOptions)]
    private static partial Regex SelectedD3d12Device();
    [GeneratedRegex(@"Vulkan:\s*\r?\n\s*Adapter: (?<vulkan_gpu>.*?)\r?$", DefaultOptions)]
    private static partial Regex SelectedVulkanDevice();
    [GeneratedRegex(@"Force FIFO present mode: (?<force_fifo_present>.*?)\r?$", DefaultOptions)]
    private static partial Regex FifoPresentMode();
    [GeneratedRegex(@"Asynchronous Texture Streaming( 2)?: (?<async_texture_streaming>.*?)\r?$", DefaultOptions)]
    private static partial Regex AsyncTextureStreaming();
    [GeneratedRegex(@"Asynchronous Queue Scheduler: (?<async_queue_scheduler>.*?)\r?$", DefaultOptions)]
    private static partial Regex AsyncQueueScheduler();
    
    [GeneratedRegex("Renderer: (?<audio_backend>.*?)\r?$", DefaultOptions)]
    private static partial Regex AudioBackend();
    [GeneratedRegex("Downmix to Stereo: (?<audio_stereo>.*?)\r?$", DefaultOptions)]
    private static partial Regex DownmixToStereo();
    [GeneratedRegex("Master Volume: (?<audio_volume>.*?)\r?$", DefaultOptions)]
    private static partial Regex MasterVolume();
    [GeneratedRegex("Enable Buffering: (?<audio_buffering>.*?)\r?$", DefaultOptions)]
    private static partial Regex AudioBuffering();
    [GeneratedRegex("Desired Audio Buffer Duration: (?<audio_buffer_duration>.*?)\r?$", DefaultOptions)]
    private static partial Regex AudioBufferLength();
    [GeneratedRegex("Enable Time Stretching: (?<audio_stretching>.*?)\r?$", DefaultOptions)]
    private static partial Regex AudioTimeStretching();
    [GeneratedRegex("Pad: (?<pad_handler>.*?)\r?$", DefaultOptions)]
    private static partial Regex GamepadType();
    [GeneratedRegex("Automatically start games after boot: (?<auto_start_on_boot>.*?)\r?$", DefaultOptions)]
    private static partial Regex AutoStartAfterBoot();
    [GeneratedRegex("Always start after boot: (?<always_start_on_boot>.*?)\r?$", DefaultOptions)]
    private static partial Regex AlwaysStartAfterBoot();
    [GeneratedRegex("Use native user interface: (?<native_ui>.*?)\r?$", DefaultOptions)]
    private static partial Regex NativeUIMode();
    [GeneratedRegex("Silence All Logs: (?<disable_logs>.*?)\r?$", DefaultOptions)]
    private static partial Regex SilenceAllLogs();

    [GeneratedRegex(@"Log:\s*\r?\n?\s*(\{(?<log_disabled_channels>.*?)\}|(?<log_disabled_channels_multiline>(\s+\w+\:\s*\w+\r?\n)+))\r?$", DefaultOptions)]
    private static partial Regex LogChannelList();
    
    [GeneratedRegex(@"Game: (?<ldr_game_full>.*(?<ldr_game>/dev_hdd0/game/(?<ldr_game_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions)]
    private static partial Regex GamePathLdr();
    [GeneratedRegex(@"Disc( path)?: (?<ldr_disc_full>.*(?<ldr_disc>/dev_hdd0/game/(?<ldr_disc_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions)]
    private static partial Regex DiscPathLdr();
    [GeneratedRegex(@"Path: (?<ldr_path_full>.*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions)]
    private static partial Regex DigitalPathLdr();
    [GeneratedRegex(@"Boot path: (?<ldr_boot_path_full>.*(?<ldr_boot_path>/dev_hdd0/game/(?<ldr_boot_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions)]
    private static partial Regex BootPathInBodyLdr();
    [GeneratedRegex(@"Game: (?<ldr_game_full>.*(?<ldr_game>/dev_hdd0/game/(?<ldr_game_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions)]
    private static partial Regex GamePathSys();
    [GeneratedRegex(@"Path: (?<ldr_path_full>.*(?<ldr_path>/dev_hdd0/game/(?<ldr_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions)]
    private static partial Regex DigitalPathSys();
    [GeneratedRegex(@"Boot path: (?<ldr_boot_path_full>.*(?<ldr_boot_path>/dev_hdd0/game/(?<ldr_boot_path_serial>[^/\r\n]+)).*|.*)\r?$", DefaultOptions)]
    private static partial Regex BootPathInBodySys();
    [GeneratedRegex(@"Elf path: (?<host_root_in_boot>/host_root/)?(?<elf_boot_path_full>(?<elf_boot_path>/dev_hdd0/game/(?<elf_boot_path_serial>[^/\r\n]+)/USRDIR/EBOOT\.BIN|.*?))\r?$", DefaultOptions)]
    private static partial Regex ElfPath();
    [GeneratedRegex(@"Mounted path ""/dev_bdvd"" to ""(?<mounted_dev_bdvd>[^""]+)""", DefaultOptions)]
    private static partial Regex VfsMountPath();
    [GeneratedRegex(@"Invalid or unsupported file format: (?<failed_to_boot>.*?)\r?$", DefaultOptions)]
    private static partial Regex InvalidFileFormat();
    [GeneratedRegex(@"(?<failed_to_decrypt>Failed to decrypt)? SELF: (?<failed_to_decrypt>Failed to (decrypt|load SELF))?.*\r?$", DefaultOptions)]
    private static partial Regex DecryptFailedSelfPath();
    [GeneratedRegex(@"Version: (APP_VER=)?(?<disc_app_version>\S+) (/ |VERSION=)(?<disc_package_version>\S+).*?\r?$", DefaultOptions)]
    private static partial Regex GameVersion();
    [GeneratedRegex(@"Failed to verify (?<failed_to_verify_npdrm>(sce|npd)) file.*\r?$", DefaultOptions)]
    private static partial Regex FailedToVerifyNpDrm();
    [GeneratedRegex(
        @"RSX:(\d|\.|\s|\w|-)* (?<driver_version>(\d+\.)*\d+)\r?\n[^\n]*?"+
        @"RSX: [^\n]+\r?\n[^\n]*?RSX: (?<driver_manuf>.*?)\r?\n[^\n]*?"+
        @"RSX: Supported texel buffer size",
        DefaultOptions
    )]
    private static partial Regex RsxDriverInfoLegacy();
    [GeneratedRegex(@"GL RENDERER: (?<driver_manuf_new>.*?)\r?$", DefaultOptions)]
    private static partial Regex GlRenderer();
    [GeneratedRegex(@"GL VERSION: (?<opengl_version>(\d|\.)+)(\d|\.|\s|\w|-)*?( (?<driver_version_new>(\d+\.)*\d+))?\r?$", DefaultOptions)]
    private static partial Regex GlVersion();
    [GeneratedRegex(@"GLSL VERSION: (?<glsl_version>(\d|\.)+).*?\r?$", DefaultOptions)]
    private static partial Regex GlslVersion();
    [GeneratedRegex(@"RSX: Supported texel buffer size reported: (?<texel_buffer_size_new>\d*?) bytes", DefaultOptions)]
    private static partial Regex GlTexelBufferSize();
    [GeneratedRegex(@"Physical device ini?tialized\. GPU=(?<vulkan_gpu>.+), driver=(?<vulkan_driver_version_raw>-?\d+)\r?$", DefaultOptions)]
    private static partial Regex PhysicalDeviceFound();
    [GeneratedRegex(@"Found [Vv]ulkan-compatible GPU: (?<vulkan_found_device>.+)\r?$", DefaultOptions)]
    private static partial Regex VulkanDeviceFound();
    [GeneratedRegex(@"Renderer initialized on device '(?<vulkan_initialized_device>.+)'\r?$", DefaultOptions)]
    private static partial Regex RenderDeviceInitialized();
    [GeneratedRegex(@"RSX: Failed to compile shader: ERROR: (?<shader_compile_error>.+?)\r?$", DefaultOptions)]
    private static partial Regex FailedToCompileShader();
    [GeneratedRegex(@"RSX: Compilation failed: ERROR: (?<shader_compile_error>.+?)\r?$", DefaultOptions)]
    private static partial Regex ShaderCompilationFailed();
    [GeneratedRegex(@"RSX: Linkage failed: (?<shader_compile_error>.+?)\r?$", DefaultOptions)]
    private static partial Regex ShaderLinkageFailed();
    [GeneratedRegex(@"RSX: Unsupported device: (?<rsx_unsupported_gpu>.+)\..+?\r?$", DefaultOptions)]
    private static partial Regex UnsupportedDevice();
    [GeneratedRegex(@"RSX: Your GPU does not support (?<rsx_not_supported_feature>.+)\..+?\r?$", DefaultOptions)]
    private static partial Regex UnsupportedDeviceFeatures();
    [GeneratedRegex(@"RSX: GPU/driver lacks support for (?<rsx_not_supported_feature>.+)\..+?\r?$", DefaultOptions)]
    private static partial Regex UnsupportedDriverFeatures();
    [GeneratedRegex(@"RSX: Swapchain: present mode (?<rsx_swapchain_mode>\d+?) in use.+?\r?$", DefaultOptions)]
    private static partial Regex SwapchainMode();
    [GeneratedRegex(@"RSX: \*\* Using (?<vk_ext>\w+?)\r?$", DefaultOptions)]
    private static partial Regex VkExtensions();
    [GeneratedRegex(@"RSX: \[CAPS\] Using (?<gl_ext>\w+?)\r?$", DefaultOptions)]
    private static partial Regex GlExtensions();
    [GeneratedRegex(@"F \d+:\d+:\d+\.\d+ (({(?<fatal_error_context>[^}]+)} )?(\w+:\s*(Thread terminated due to fatal error: )?|(\w+:\s*)?(class [^\r\n]+ thrown: ))\r?\n?)(?<fatal_error>.*?)(\r?\n)(\r?\n|·|$)", DefaultSingleLine)]
    private static partial Regex FatalError();
    [GeneratedRegex(@"Failed to load RAP file: (?<rap_file>.*?\.rap).*\r?$", DefaultOptions)]
    private static partial Regex FailedToLoadRap();
    [GeneratedRegex(@"Rap file not found: “?(?<rap_file>.*?\.rap)”?\r?$", DefaultOptions)]
    private static partial Regex RapNotFound();
    [GeneratedRegex(@"(?<native_ui_input>Pad handler expected but none initialized).*?\r?$", DefaultOptions)]
    private static partial Regex MissingGamepad();
    [GeneratedRegex(@"Failed to bind device (?<failed_pad>.+) to handler (?<failed_pad_handler>.+).*\r?$", DefaultOptions)]
    private static partial Regex FailedToBindGamepad();
    [GeneratedRegex(@"Input: (?<pad_handler>.*?) device .+ connected\r?$", DefaultOptions)]
    private static partial Regex InputDeviceConnected();
    [GeneratedRegex(@"XAudio2Thread\s*: (?<xaudio_init_error>.+failed\s*\((?<xaudio_error_code>0x.+)\).*)\r?$", DefaultOptions)]
    private static partial Regex XAudio2Thread();
    [GeneratedRegex(@"XAudio2Backend\s*: (?<xaudio_init_error>.+failed\s*\((?<xaudio_error_code>0x.+)\).*)\r?$", DefaultOptions)]
    private static partial Regex CellAudioThread();
    [GeneratedRegex(@"Audio renderer (?<audio_backend_init_error>.+) could not be initialized\r?$", DefaultOptions)]
    private static partial Regex AudioBackendFailed();
    [GeneratedRegex(@"PPU executable hash: PPU-(?<ppu_patch>\w+( \(<-\s*\d+\))?).*?\r?$", DefaultOptions)]
    private static partial Regex PpuHash();
    [GeneratedRegex(@"OVL executable hash: OVL-(?<ovl_patch>\w+( \(<-\s*\d+\))?).*?\r?$", DefaultOptions)]
    private static partial Regex OvlHash();
    [GeneratedRegex(@"SPU executable hash: SPU-(?<spu_patch>\w+( \(<-\s*\d+\))?).*?\r?$", DefaultOptions)]
    private static partial Regex SpuHash();
    [GeneratedRegex(@"PRX library hash: PRX-(?<prx_patch>\w+-\d+( \(<-\s*\d+\))?).*?\r?$", DefaultOptions)]
    private static partial Regex PrxHash();
    [GeneratedRegex(@"OVL hash of (\w|[\.\[\]])+: OVL-(?<ovl_patch>\w+( \(<-\s*\d+\))?).*?\r?$", DefaultOptions)]
    private static partial Regex OvlHash2();
    [GeneratedRegex(@"PRX hash of (\w|[\.\[\]])+: PRX-(?<prx_patch>\w+-\d+( \(<-\s*\d+\))?).*?\r?$", DefaultOptions)]
    private static partial Regex PrxHash2();
    [GeneratedRegex(@"Applied patch \(hash='(?:\w{3}-\w+(-\d+)?)', description='(?<patch_desc>.+?)', author='(?:.+?)', patch_version='(?:.+?)', file_version='(?:.+?)'\) \(<- (?:[1-9]\d*)\).*\r?$", DefaultOptions)]
    private static partial Regex AppliedPatch();
    [GeneratedRegex(@"Loaded SPU image: SPU-(?<spu_patch>\w+ \(<-\s*\d+\)).*?\r?$", DefaultOptions)]
    private static partial Regex SpuImageLoad();
    [GeneratedRegex(@"'sys_fs_stat' failed (?!with 0x8001002c).+“(/dev_bdvd/(?<broken_filename_or_dir>.+)|/dev_hdd0/game/NP\w+/(?<broken_digital_filename>.+))”.*?\r?$", DefaultOptions)]
    private static partial Regex SysFsStatFailed();
    [GeneratedRegex(@"'sys_fs_open' failed (?!with 0x8001002c).+“(/dev_bdvd/(?<broken_filename>.+)|/dev_hdd0/game/NP\w+/(?<broken_digital_filename>.+))”.*?\r?$", DefaultOptions)]
    private static partial Regex SysFsOpenFailed();
    [GeneratedRegex(@"'sys_fs_opendir' failed .+“/dev_bdvd/(?<broken_directory>.+)”.*?\r?$", DefaultOptions)]
    private static partial Regex SysFsOpenDirFailed();
    [GeneratedRegex(@"EDAT: Block at offset (?<edat_block_offset>0x[0-9a-f]+) has invalid hash!.*?\r?$", DefaultOptions)]
    private static partial Regex InvalidEdat();
    [GeneratedRegex(@"(?<fw_missing_msg>PS3 firmware is not installed.+)\r?$", DefaultOptions)]
    private static partial Regex FwNotInstalled();
    [GeneratedRegex(@"(?<fw_missing_something>do you have the PS3 firmware installed.*)\r?$", DefaultOptions)]
    private static partial Regex FwNotInstalled2();
    [GeneratedRegex(@"U \d+:\d+:\d+\.\d+ ({(?<unimplemented_syscall_context>.+?)} )?.*Unimplemented syscall (?<unimplemented_syscall>.*)\r?$", DefaultOptions)]
    private static partial Regex UnimplementedSyscall();
    [GeneratedRegex(@"cellAudio: Could not enqueue buffer onto audio backend(?<enqueue_buffer_error>.).*\r?$", DefaultOptions)]
    private static partial Regex CellAudioEnqueueFailed();
    [GeneratedRegex(@"{PPU\[.+\]} (?<log_channel>[^ :]+)( TODO)?: (?!“)(?<syscall_name>[^ :]+?)\(.*\r?$", DefaultOptions)]
    private static partial Regex PpuSyscallTodo();
    [GeneratedRegex(@"Verification failed.+\(e=0x(?<verification_error_hex>[0-9a-f]+)\[(?<verification_error>\d+)\]\)", DefaultOptions)]
    private static partial Regex VerificationFailed();
    [GeneratedRegex(@"sys_tty_write\(\)\: “(?<tty_line>.*?)”\r?(\n|$)", DefaultSingleLine)]
    private static partial Regex SysTtyWrite();
    [GeneratedRegex(@"⁂ (?<syscall_name>[^ :\[]+?) .*\r?$", DefaultOptions)]
    private static partial Regex SyscallDump();
    [GeneratedRegex(@"(\b|_)(?<game_mod>(undub|translation patch))(\b|_)", RegexOptions.IgnoreCase | DefaultOptions)]
    private static partial Regex UndubFlag();
    [GeneratedRegex(@"Input: Pad (?<pad_id>\d): device='(?<pad_controller_name>(?!Null).+?)', handler=(?<pad_handler>.+?), VID=.+?\r?$", DefaultOptions)]
    private static partial Regex InputDeviceGamepad();
    [GeneratedRegex(@"Found game controller \d: .+ has_accel=(?<pad_has_accel>.+?), has_gyro=(?<pad_has_gyro>.+?)\r?$", DefaultOptions)]
    private static partial Regex SdlControllerName();
}