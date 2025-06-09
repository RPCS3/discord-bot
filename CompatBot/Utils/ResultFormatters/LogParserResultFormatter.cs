using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using CompatApiClient;
using CompatApiClient.POCOs;
using CompatApiClient.Utils;
using CompatBot.Database.Providers;
using CompatBot.EventHandlers;
using CompatBot.EventHandlers.LogParsing.POCOs;
using CompatBot.EventHandlers.LogParsing.SourceHandlers;
using IrdLibraryClient;

namespace CompatBot.Utils.ResultFormatters;

internal static partial class LogParserResult
{
    private static readonly Client CompatClient = new();
    private static readonly IrdClient IrdClient = new();
    private static readonly PsnClient.Client PsnClient = new();

    private const RegexOptions DefaultSingleLine = RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase | RegexOptions.Singleline;
    // RPCS3 v0.0.3-3-3499d08 Alpha | HEAD
    // RPCS3 v0.0.4-6422-95c6ac699 Alpha | HEAD
    // RPCS3 v0.0.5-7104-a19113025 Alpha | HEAD
    // RPCS3 v0.0.5-42b4ce13a Alpha | minor
    // RPCS3 v0.0.18-local_build Alpha | local_build
    [GeneratedRegex(@"RPCS3 v(?<version_string>(?<version>(\d|\.)+)(-(?<build>\d+))?-(?<commit>[0-9a-z_]+|unknown))( (?<stage>\w+))?( \| (?<branch>[^|\r\n]+))?( \| Firmware version: (?<fw_version_installed>[^|\r\n]+))?( \| (?<unknown>.*))?\r?$", DefaultSingleLine)]
    private static partial Regex BuildInfoInLog();
    [GeneratedRegex(@"(\d{1,2}(th|rd|nd|st) Gen)?(?<cpu_model>[^|@]+?)\s*(((CPU\s*)?@\s*(?<cpu_speed>.+)\s*GHz\s*)|((APU with|(with )?Radeon|R\d, \d+ Compute) [^|]+)|((\w+[\- ]Core )?Processor))?\s* \| (?<thread_count>\d+) Threads \| (?<memory_amount>[0-9\.\,]+) GiB RAM( \| TSC: (?<tsc>\S+))?( \| (?<cpu_extensions>.*?))?\r?$", DefaultSingleLine)]
    private static partial Regex CpuInfoInLog();
    [GeneratedRegex(@"(?<version>\d+\.\d+\.\d+)", DefaultSingleLine)]
    private static partial Regex LinuxKernelVersion();
    [GeneratedRegex(@"(?<hash>\w+(-\d+)?)( \(<-\s*(?<patch_count>\d+)\))?", DefaultSingleLine)]
    private static partial Regex ProgramHashPatch();
    // rpcs3-v0.0.5-7105-064d0619_win64.7z
    // rpcs3-v0.0.5-7105-064d0619_linux64.AppImage
    [GeneratedRegex(@"rpcs3-v(?<version>(\d|\.)+)(-(?<build>\d+))?-(?<commit>[0-9a-f]+)_", DefaultSingleLine)]
    private static partial Regex BuildInfoInUpdate();
    [GeneratedRegex(@"'(?<device_name>.+)' running on driver (?<version>.+)\r?$", DefaultSingleLine)]
    private static partial Regex VulkanDeviceInfo();
    [GeneratedRegex(@"Intel\s?(®|\(R\))? (?<gpu_model>((?<gpu_family>(\w|®| )+) Graphics)( (?<gpu_model_number>P?\d+))?)(\s+\(|$)", DefaultSingleLine)]
    private static partial Regex IntelGpuModel();
    [GeneratedRegex(@"[A-Z]:/(?<program_files>Program Files( \(x86\))?/)?(?<desktop>([^/]+/)+Desktop/)?(?<rpcs3_folder>[^/]+/)*GuiConfigs/", DefaultSingleLine)]
    private static partial Regex InstallPath();

    private static readonly char[] NewLineChars = ['\r', '\n'];

    private static readonly Version MinimumOpenGLVersion = new(4, 3);
    private static readonly Version MinimumFirmwareVersion = new(4, 80);

    private static readonly Version NvidiaFullscreenBugMinVersion = new(400, 0);
    private static readonly Version NvidiaFullscreenBugMaxVersion = new(499, 99);
    private static readonly Version NvidiaTextureMemoryBugMinVersion = new(526, 0);
    private static readonly Version NvidiaTextureMemoryBugMaxVersion = new(526, 99);
    
    private static readonly Version NvidiaRecommendedWindowsDriverVersion = new(512, 16);
    private static readonly Version NvidiaRecommendedLinuxDriverVersion = new(515, 57);
    private static readonly Version AmdRecommendedWindowsDriverVersion = new(24, 2, 1);
    private static readonly Version IntelRecommendedWindowsDriverVersion = new(0, 101, 1660);

    private static readonly Version NvidiaFullscreenBugFixed = new(0, 0, 6, 8204);
    private static readonly Version TsxFaFixedVersion  = new(0, 0, 12, 10995);
    private static readonly Version RdnaMsaaFixedVersion  = new(0, 0, 13, 11300);
    private static readonly Version IntelThreadSchedulerBuildVersion  = new(0, 0, 15, 12008);
    private static readonly Version PsnDiscFixBuildVersion  = new(0, 0, 18, 12783);
    private static readonly Version CubebBuildVersion  = new(0, 0, 19, 13050);
    private static readonly Version FixedTlouRcbBuild = new(0, 0, 21, 13432); // the best I got was "it was fixed more than a year ago", so it's just a random build from a year ago
    private static readonly Version FixedSimpsonsBuild = new(0, 0, 29, 15470);
    private static readonly Version FixedSpuGetllarOptimizationBuild = new(0, 0, 36, 17938);

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
        {"BCJS30042", "NPJA00104"},

        // Aquanaut's Holiday: Hidden Memories
        {"BCJS30023", "NPJA00103"},
    };

    private static readonly string[] Known1080pIds =
    [
        "NPEB00258", "NPUB30162", "NPJB00068", // scott pilgrim
    ];

    private static readonly string[] KnownDisableVertexCacheIds =
    [
        "BLES01219", "BLUS30616", // penguins blowhole
        "NPEB00754", "NPUB30605", // real steel
        "BLES01379", "BLUS30813", // marvel comic combat
        "BLES00895", "BLUS30525", "BLAS50277", // marvel infinity gauntlet
        "BLES01446", "BLUS30565", "BLJM60391", // sh downpour
        "BLES02070", "BLUS31460", "BLJM61220", // kh2.5
        "NPEB00258", "NPUB30162", "NPJB00068", // scott pilgrim
        "NPEB00303", "NPUB30242", "NPHB00229", // crazy taxi
        "NPEB00630", "NPUB30493", "NPJB00161", "NPHB00383", // daytona usa
        "BLES00712", "BLUS30398", "BLJS10039", "BLJS50009", // way samurai 3
        "NPEB00304", "NPUB30249", "NPJB00083", "NPHB00228", // sonic adventure
        "BLES00024", "BLUS30032", "NPEB00788", "NPUB30636", "NPHB00481", // splinter cell double agent
        "BLES01342", "BLUS30666", "BLES01343", "BLKS20333", "BLES01747", "BLES01748", "BLUS31062", "NPEB00890", "NPUB30700", // saints row 3
    ];

    private static readonly HashSet<string> KnownNoRelaxedXFloatIds = [];
    
    private static readonly HashSet<string> KnownNoApproximateXFloatIds =
    [
        "BLES02247", "BLUS31604", "BLJM61346", "NPEB02436", "NPUB31848", "NPJB00769", // p5
        "BLES00932", "BLUS30443", // DeS
    ];

    private static readonly HashSet<string> KnownFpsUnlockPatchIds =
    [
        "BLES00932", "BLUS30443", // DeS
        "BLUS30481", "BLES00826", "BLJM60223", // Nier
        "BLUS31197", "NPUB31251", "NPEB01407", "BLJM61043", "BCAS20311", // DoD3
        "BLUS31405", // jojo asb
        "BLJS10318", // jojo eoh
        "BLES00148", "BLES00149", "BLES00154", "BLES00155", "BLES00156", "BLUS30072", "BLJS10013", "BLKS20048", "NPEB00740", "NPUB30588", // Call of Duty 4: Modern Warfare
        "BLES01329", "BLES01330", "BLUS30778", "BLJM60413", "BLAS50546", "BLES01885", "BLES01886", "BLUS31202", "BLJM61086", "BLAS50624", // The Elder Scrolls V: Skyrim
        "BLJM61211", "BLJM55085", "BLAS50763", "NPEB02076", "NPUB31552", "NPJB00653", // Resident Evil HD
        "BLJM61272", "NPEB02226", "NPUB31689", "NPJB00726", // Resident Evil 0
        "BLES01773", "BLUS31051", "NPEB90478", // Resident Evil: Revelations
        "BLES00875", "BLUS30490", "BLJM60180", "BLUD80019", // 3D Dot Game Heroes
        "BLES01265", "NPEB00625", // Alice: Madness Returns
        "NPEB01088", // Alien Rage
        "NPEB01224", // Aliens: Colonial Marines
        "NPEB00316", // All Zombies Must Die!
        "BLES00704", // Alpha Protocol
        "BLES01227", "BLUS30721", "BLJM60409", // Asura's Wrath
        "BLES00827", "BLUS30515", "NPEB01156", "NPEB90170", // Batman: Arkham Asylum
    ];

    private static readonly HashSet<string> KnownWriteColorBuffersIds =
    [
        "BLUS30235", "BLES00453", // AC/DC Live: Rock Band Track Pack
        "BLUS30399", "BCJS30021", "BCAS20050", // Afrika
        "BLUS30607", "BLES0126", "NPUB30545", "BLJM60359", // Alice: Madness Returns
        "BLUS30049", "NPUB90035", // All-Pro Football 2K8
        "BLES00937", "BLUS30555", "NPEB90257", "NPUB90448", // Apache: Air Assault
        "BLUS30027", "BLES00039", "BLJM60012", "NPEB90009", "NPUB90008", // Armored Core 4
        "BLES00370", "BLUS30187", "BLJM60066", "NPHB00033", "NPJB90067", // Armored Core: For Answer
        "BLES00168", "BLUS30057", // Army of TWO
        "BLES01763", "BLUS31069", "NPEB01216", "NPEB01217", "NPUB30987", "BLES01767", "BLJM60578", "NPJB00332", "NPEB90470, NPUB90862", // Army of TWO: The Devil's Cartel
        "BLES01882", "BLES01883", "BLES01884", "BLUS31193", "NPEB01396", "NPUB31246", "BLJM61056", "BLUS31483", "BLES02085", // Assassin's Creed IV: Black Flag
        "BLUS31152", "BLES01793", "NPUB31115", "NPEB01262", "BLJM60486", "BLJM61051", // Atelier Ayesha: The Alchemist of Dusk
        "BLUS30941", "BLES01593", "BLJM60348", "BLJM55041", "NPJB60142", // Atelier Meruru: The Apprentice of Arland
        "BLES00253", "NPHB00035", "NPUB30126", "BCAS20043", "BLJM60077", "NPHB00268", "NPJB90105", // Battle Fantasia
        "BLES01275", "BLUS30762", "NPEB00723", "NPJB00202", "NPUB30742", "BLAS50380", "BLJM60384", "NPHB00491", "NPUB90600", "BLET70016", // Battlefield 3
        "BLES01832", "BLUS31162", "NPEB01303", "NPUB31148", "BLJM61039", "BLAS50588", "NPJB00377", "NPHB00546", "BLET70034", "NPUB90959", "NPJB90637", // Battlefield 4
        "BLES00259", "BLUS30118", "BLJM60071", "BLES00261", "BLUS30121", "NPEB90073", "NPUB90070", "NPJB90112", // Battlefield: Bad Company
        "BLES00773", "BLES00779", "BLUS30517", "BLUS30458", "NPEB00724", "NPUB30583", "BLJM60197", "NPEB90212", "NPUB90347", "NPHB00186", "NPUB90316", "BLET70004", "NPHB00156", // Battlefield: Bad Company 2
        "BLES02039", "BLUS31440", "BLJM61203", "NPUB31511", "BLAS50725", "NPEB02038", "NPJB00641", "NPHB00673", "BLET70061", "NPUB91010", "NPJB90712", // Battlefield: Hardline
        "BLES00286", "BLUS30154", "NPEB90097", "NPUB90151", // Beijing 2008
        "NPEB00435", "NPUB30394", // Beyond Good & Evil HD
        "BCAS25017", "BCES01121", "BCES01122", "BCES01123", "BCUS98298", "BCUS99134", "BCJS37009", "NPEA00513", "NPUA81087", "NPHA80260", "BCES01888", "NPUA81088", "NPJA00097", "NPEA90127", "NPJA90259", "NPUA72074", // Beyond: Two Souls
        "BLES01397", "BLUS30831", "NPEB01119", "NPEB90417", // Birds of Steel
        "BLES00759", "BLUS30295", "BLJM60244", "NPEB90250", "NPUB90428", // Blur
        "NPUB30505", "NPEB00563", // Castlevania: Harmony of Despair
        "NPUB30722", // Closure
        "BLES00673", "BLUS30313", "NPEB90176", "NPUB90294", // Colin McRae: DiRT 2
        "BLUS30782", "BLES01402", "BLES01396", "BLJM60993", "BLAS50397", "BLES01765", "BLJM60517", // Dark Souls
        "NPEB00409", // Deadstorm Pirates
        "BLUS30024", "BLES00042", "BLAS50012", "BLJM60029", "NPUB90004", // Def Jam: Icon
        "BLES00932", "BLUS30443", "BCJS70013", "BCJS30022", // Demon's Souls
        "BLES01857", "BLUS31181", "BCJS35001", "NPEB02021", "NPUB31202", "NPEB02097", "NPUB31545", "NPJA90277", "NPEB90553", "NPJA90286", // Destiny
        "BLES01287", "NPEB00848", "NPUB30680", "BLUS30724", "BLES01548", "BLUS30975", // DiRT 3
        "NPJA00037", "NPJA90090", // Dress
        "BLES01147", "BLUS30615", "NPEB00828", "NPUB30655", "BLES01297", "BLJS10121", "BLAS50341", "NPHB00484", "NPEB90334", "NPUB90573", // Duke Nukem Forever
        "BLES00075", "BLUS30042", // Fantastic Four: Rise of the Silver Surfer
        "BLUS30178", "BLES00325", "BLES00324", "NPEB00599", "NPUB30523", "BLJM60108", "NPJB00397", "NPHB00423", // Far Cry 2
        "BLES01137", "BLES01138", "BLUS30687", "BLJM60532", "NPEB01096", "NPUB30825", "BLES01995", "BLUS31393", "NPJB00559", "BLET70025", // Far Cry 3
        "NPEB01322", "NPUB31142", // Far Cry 3: Blood Dragon
        "BLES02011", "BLES02012", "BLUS31420", "NPEB01982", "BLJM61179", "NPEB02272", "NPUB31470", "NPJB00603", // Far Cry 4
        "BLUS30504", "BLES01062", "BLJM60196", "BLJM60303", "NPEB90285", "NPUB90456", // Fist of the North Star: Ken's Rage
        "BCES00005", "BCUS98142", "NPEA90003", "BCJS30005", "BCAS20009", // Formula One Championship Edition
        "NPUB30418", "NPEB00466", // From Dust
        "BLES01724", "BLUS31040", "NPUB30874", "NPEB01112", "NPEB90442", "NPUB90815", // Fuse
        "NPEB01300", "NPUB31140", // God Mode
        "BCAS25003", "BCES00510", "BCES00516", "BCES00799", "BCJS37001", "BCUS98111", "BCKS15003", "NPUA70080", // God of War 3 / Demo
        "BCES01741", "BCES01742", "BCUS98232", "NPEA00445", "NPUA80918", "NPHA80258", "BCJS37008", "BCAS25016", "NPJA00094", "BCKS15012", "NPEA90123", "NPUA70265", "NPUA70269", "NPEA90115", "NPUA70216", "BCET70050", // God of War: Ascension
        "BLES00229", "BLUS30127", "NPEB00882, NPUB30702, BLES00258", "BLJM60093", "BLES01128", "BLUS30682", // Grand Theft Auto IV
        "BLES01807", "BLUS31156", "NPEB01283", "NPUB31154", "BLJM61019", "BLJM61182", "BLJM61304", "NPJB00516", // Grand Theft Auto V
        "BLES00887", "NPEB00907", "NPUB30704", "BLUS30524", "BLJM60235", // Grand Theft Auto: Episodes from Liberty City
        "BLJS10286", "BLAS50770", // Gundam Breaker 2
        "BCES00797", "BCES00802", "BCUS98164", "BCAS20107", "BCES00458", "BCES01293", "BCJS30040", "NPEA90076", "NPUA70112", "NPHA80118", "NPEA90053", "NPUA70088", "NPJA90129", "NPHA80086", // Heavy Rain
        "NPEB01341", "NPUB31200", // Hotline Miami
        "NPEB02007", "NPUB31481", // Hotline Miami 2: Wrong Number
        "BLUS30924", "NPUA80227", // Jeopardy!
        "BLUS30084", "BLES00143", "BLJM60058", "BLJM60127", "NPUB90053", // Juiced 2: Hot Import Nights
        "BLUS30215", // Karaoke Revolution Presents: American Idol Encore 2
        "BCAS20066", "BCES00081", "BCUS98116", "NPUA98116", "NPUA70034", "NPJA90092", "NPEA90034", "NPUA70034", // Killzone 2 / demo
        "BCES01007", "BCUS98234", "BCAS25008", "BCJS30066", "NPUA70167", "NPEA00321", "NPJA00071", "NPEA90084", "NPUA70133", "NPJA90176", "NPHA80140", "NPEA90085", "NPJA90178", "NPUA70134", "NPHA80141", "BCET01007", "NPEA90086", "BCET70024", "NPUA70118", "NPUA70138", // Killzone 3 / Demo / Beta / MP
        "BLES01251", "BLUS30710", "NPEB01055", "BLJS10191", "NPUB90707", // Kingdoms of Amalur: Reckoning
        "BCES00141", "BCUS98148", "NPEA00241", "NPUA80472", "BCAS20058", "BCJS30018", "BCUS98199", "BCJS70009", "BCKS10059", "BCES00611", "BCUS98208", "BCAS20078", "NPEA00147", "NPUA70045", "NPJA90097", "NPHA80067", "BCUS70030", "BCET70002", "BCET70011", // LittleBigPlanet
        "BCES01422", "BCUS98254", "NPUA80848", "NPEA00421", "NPHA80239", "NPJA90244", "NPEA90117", "NPUA70249", // LittleBigPlanet Karting / demo
        "BLES01525", "BLUS30917", "BLJS10125", "NPJB00273", "BLJS10168", // Lollipop Chainsaw
        "BLES00710", "BLUS30434", "BLJM60177", "BLAS50173", "NPEB90189", "NPUB90321", "NPHB00138", "NPEB90181", "NPUB90300", // Lost Planet 2
        "BLJS10184", // Macross 30 Ginga o Tsunagu Utagoe
        "BLES00285", "BLUS30146", "BLJM60088", "BLUS30170", "NPUB90138", // Madden NFL 09
        "BLUS31178", "NPUB31183", "BLES01850", "BLAS50622", "NPEB90493", "NPUB90953", // Madden NFL 25
        "BLES00546", "BLUS30294", // Marvel: Ultimate Alliance 2
        "BLES00867", "BLUS30518", // Megamind
        "BLES00246", "BLUS30109", "BLJM67001", "NPEB02182", "NPUB31633", "BLAS55002", "BLAS55004", "BLKS25002", "NPJB00698", "NPEB90116", "NPUB90176", "NPJB90149", "NPHB00065", "NPHB00067", // Metal Gear Solid 4 / Demo
        "BLES02102", "BLUS31491", "BLJM61247", "NPEB02140", "NPUB31594", "BLAS50815", "NPJB00673", "NPHB00731", // Metal Gear Solid V / Demo
        "BLES00362", "BLUS30190", "BLJS10046", "BLJM60368", "BLES00652", "BLUS30442", "NPEB00546", "NPUB30471", "NPJB00503", "NPHB00411", // Midnight Club: Los Angeles
        "BCUS98167", "BCJS30041", "BCES00701", "NPUA80535", "NPEA00291", "NPUA70096", "NPEA90062", "NPUA70074", // Modnation Racers / demo / beta
        "BCES00006", "BCUS98137", "NPEA00333", "NPUA80499", // Motorstorm
        "NPEA00333", "NPUA80678", "NPJA00077", "NPHA80190", // MotorStorm RC
        "BCES00484", "BCUS98242", "NPEA00315", "NPUA80661", // Motorstorm Apocalypse
        "BCES00129", "BCUS98155", "NPEA90090", "NPUA70140", "NPEA90033", // Motorstorm Pacific Rift / Demo
        "BLES02032", "BLUS31455", "NPUB31530", // MX vs. ATV Supercross
        "BLAS50266", "BLES00949", "BLUS30566", "NPEB00587", "NPUB30521", "BLES00950", "NPEB90293", "NPUB90488", // Need for Speed Hot Pursuit / Demo
        "BLES01659", "BLES01660", "BLUS31010", "NPEB01042", "NPUB30789", "BLJM60519", "BLAS50482", "NPJB00228", "NPHB00494", "NPEB90472", "NPUB90927", // Need for Speed Most Wanted
        "BLUS30391", "BLES00681", "BLES00682", "BLAS50137", "NPUB90325", "NPHB00153", "NPEB90194", // Need for Speed Shift
        "BLUS30481", "BLES00826", "BLJM60223", // Nier
        "BLJM60467", "NPEB00900", "NPUB30720", "NPJB00195", "NPHB00495", "BLAS50523", // Okami HD
        "BLJS10221", // Onechanbara Z: Kagura with NoNoNo!
        "BLES01090", // PDC World Championship Darts: Pro Tour
        "BLUS30852", "NPEA00271", "NPUA30059", // Plants vs. Zombies
        "BLES00680", "BLUS30418", "NPEB00833", "NPUB30638", "BLJM60265", "BLKS20202", "NPHB00465", "BLES01294", "BLUS30758", "BLJM60403", "BLAS50382", "BLKS20315", "NPJB00504", // Red Dead Redemption
        "BLES00485", "BLUS30270", "BLES00816", "BLUS30491", "NPEB00687", "NPUB30564", "BLJM60199", "NPEB90124", "NPJB90152", "NPHB00070", // Resident Evil 5
        "BLES01465", "BLJM60405", "BLUS30855", "NPEB01150", "NPUB30984", "BLES01683", "NPJB00319", "NPEB90464", "NPUB90772", "NPUB90864", "NPEB90426", "NPJB90541", "NPHB00514", // Resident Evil 6
        "BLES02133", "NPUB31720", "BLJM61294", // Ride
        "NPEB02103", "NPUB31577", // Risk
        "NPEB02269", // Risk Urban Assault
        "BLES00385", "BLUS30147", // Rock Band 2
        "BLES00986", "BLUS30463", "NPUB90505", "NPEB90291", // Rock Band 3
        "BLUS30327", "BLUS30623", "BLUS30351", "BLUS30352", // Rock Band Track Packs
        "BLES00777", // Rugby League Live
        "BLES01472", "NPEB01197", "NPEB01846", // Rugby League Live 2
        "BLES01889", "BLES01954", "BLUS31205", "NPEB01404", "NPUB31257", "BLJS10246", "NPJB00551", "BLES02019", "BLUS31416", "NPEB90502", "NPUB90965", // Saints Row IV
        "BLES02095", "NPEB02121", "NPUB31604", "BLUS31496", // Saints Row: Gat out of Hell
        "BLES01342", "BLUS30666", "NPEB00888", "NPEB00890", "NPUB30700", "BLES01343", "BLKS20333", "BLES01747", "BLES01748", "BLUS31062", "NPEB90361", "NPUB90632", // Saints Row: The Third
        "BLUS30580", "BLES01066", "NPEB00618", "NPUB30539", // Shift 2 Unleashed
        "BLES00124", "BLES00125", "BLUS30059", "BLJM60070", "NPUB90057", // Skate
        "BLUS30253", "BLES00461", "NPEB90131", "NPUB90196", "NPHB00080", // Skate 2
        "BLUS30464", "BLES00760", "BLJM60296", "NPEB90226", "NPUB90375", "NPHB00200", // Skate 3
        "BLES01981", "BLUS31401", "NPEB01905", "BLES02145", "BLUS31532", // Sniper Elite 3
        "BLES01290", "BLUS30798", "NPEB01009", "BLJM60503", "NPUB31291", "BLES01812", "NPEB90404", "NPUB90749", // Sniper Elite V2
        "BLES01646", "BLUS30839", "NPEB01232", "BLJM61145", "NPUB31090", "NPJB00535", "NPEB90471", "NPUB90928", // Sonic & All-Stars Racing Transformed / demo
        "BLES00750", "BLUS30342", "NPEB90229", "NPUB90275", // Sonic & SEGA All-Stars Racing
        "BLES00296", "BLUS30160", "BLJS10026", "NPEB90099", "NPJB90130", // SoulCalibur IV / Demo
        "BLUS30736", "BLES01250", "NPEB01363", "NPUB31195", // SoulCalibur V
        "BLES00055", "BLES00056", "BLUS30031", "BLUS30030", // Spider-Man 3
        "BLES01702", "BLJS10187", "BLUS31002", "NPEB01140", "NPJB00236", "NPUB30899", // Tekken Tag Tournament 2
        "BLUS30527", "BLES00884", // Test Drive Unlimited 2 - Needs RCB Also
        "BLES01987", "BLUS30964", "BLJS10160", // The Witch and the Hundred Knight
        "BLES01329", "BLES01330", "BLUS30778", "BLJM60413", "BLAS50546", "BLES01885", "BLES01886", "BLUS31202", "BLJM61086", "BLAS50624", // The Elder Scrolls V: Skyrim
        "BCES00057", "BLUS30093", // Time Crisis 4
        "BCES01070", "BLUS30528", "BLJS10091", "NPEA90078", "NPUB90464", // Time Crisis: Razing Storm
        "BLES00326", "BLUS30180", "NPEB01170", "NPUB30913", "BLES00487", "NPEB90118", "NPUB90171", // Tom Clancy's EndWar
        "BLUS30186", "BLES00330", "NPUB31101", "NPUB90167", // Tom Clancy's H.A.W.X
        "BLES01766", "BLES01878", "BLUS31025", "BLJM61057", "NPEB01379", "NPUB31248", "BLES01879", "BLAS50612", "NPJB00410", "NPHB00604", "BLET70047", // Tom Clancy's Splinter Cell: Blacklist
        "BCAS20087", "BCJS30034", // Toro! Let's Party!
        "NPHA80058", "NPUA80247", // Trash Panic
        "BLES01231", "BLUS30738", // UFC Undisputed 3
        "BCES00065", "BCUS98103", "BCJS30015", "BCAS20024", "NPEA00363", "NPUA80697", "NPHA80193", "NPEA90018", "NPUA98103", "NPJA90063", // Uncharted: Drake's Fortune / Demo / Beta
        "BCES00509", "BCES00727", "BCES00757", "BCUS98123", "BCJS30035", "BCAS20097", "NPEA00364", "NPEA00365", "NPUA80698", "NPHA80194", "BCKS10086", "NPEA00369", "NPEA90055", "NPUA70092", "NPJA90127", "BCET70015", "NPUA70051", "NPUA70049", // Uncharted 2: Among Thieves
        "BCES01175", "BCES01176", "BCUS98233", "BCJS37004", "BCAS25009", "BCES01670", "BCES01692", "BCUS99086", "BCAS25014", "NPEA00422", "NPUA80858", "NPUA70183", "BCET70034", "BCET70043", "NPUA70153", "NPUA70180", "NPHA80158", // Uncharted 3: Drake's Deception
        "NPUA80137", "BLUS30923", // Wheel of Fortune
        "BCAS20100", "BCES00664", "NPEA00057", "NPJA00031", "NPUA80105", "NPHA80039", // WipEout HD / Fury
        "BLES01909", "NPEB01789", "NPUB31297", "BLES01910", "BLUS31220", "BLJM61201", // Wolfenstein: The New Order
        "BLES01721", "BLUS31168", "NPEB01072", "NPUB31153", "BLJM60575", "NPEB90467", // WRC 3: FIA World Rally Championship
        "BLES01874", "BLUS31509", "NPEB01381", "NPUB31452", "NPJB00624", "BLJM61195", "NPEB90523", // WRC 4: FIA World Rally Championship
        "BLES01937", "NPEB01815", "BLUS31277", // WWE 2K14
        "BLES00694", "BLUS30378", // Karaoke Revolution
    ];

    private static readonly HashSet<string> KnownResScaleThresholdIds =
    [
        "BCAS20270", "BCES01584", "BCES01585", "BCJS37010", "BCUS98174", // The Last of Us
        "NPEA00435", "NPEA90122", "NPHA80243", "NPHA80279", "NPJA00096", "NPJA00129", "NPUA70257", "NPUA80960", "NPUA81175",
    ];

    private static readonly HashSet<string> KnownMotionControlsIds =
    [
        "BCES00797", "BCES00802", "BCUS98164", "BCJS30040", "NPEA90053", "NPEA90076", "NPUA70088", "NPUA70112", // heavy rain
        "BCAS25017", "BCES01121", "BCES01122", "BCES01123", "BCUS98298", "NPEA00513", "NPUA81087", "NPEA90127", "NPJA90259", "NPUA72074", "NPJA00097", // beyond two souls
        "NPEA00094", "NPEA00250", "NPJA00039", "NPUA80083", // flower
        "NPEA00036", "NPUA80069", "NPJA00004", // locoroco
        "BCES01284", "BCUS98247", "BCUS99142", "NPEA00429", "NPUA80875", "NPEA90120", "NPUA70250", // sly cooper 4
        "BCAS20112", "BCAS20189", "BCKS10112", "BLES01101", "BLJS10072", "BLJS10114", "BLJS50026", "BLUS30652", "NPEB90321", // no more heroes
        "BCUS98116", "BCES00081", "BCAS20066", "BCJS30032", // killzone 2
    ];

    private static readonly HashSet<string> KnownGamesThatRequireInterpreter =
    [
        "NPEB02126", "NPUB31590", // don't starve
        "BLES00023", "BLUS30006", "BLJS10007", // Blazing Angels: Squadrons of WWII
        "NPEB00220", "NPUB30254", "NPHB00297", // Landit Bandit
        "BLES00189", "BLES00190", "BLUS30105", // Soldier of Fortune: Payback
        "BLES02185", "NPEB02302", "NPUB31776", // Rugby League Live 3
        "NPEB01386", "NPJB00562", "NPUB31244", "NPUB31569", // Assassin's Creed lib
        "BCUS98144", "BCES00112", "NPEA90021", "NPUA80112", // NBA08
        "BLUS31152", "BLES01793", "BLJM60486", "NPUB31115", "NPEB01262", // Atelier Ayesha: The Alchemist of Dusk
        "BLES00103", "BLUS30052", "BLJM60072", "NPEB90043", "NPUB90063", // Blazing Angels 2: Secret Missions of WWII
        "BLES00249", "BLES00250", "BLES00263", "BLUS30137", "NPEB90066", "NPUB90051", // robert ludlum remove after #14845 is fixed
        "BLES01796", "BLUS31381", "BLJM61068", "NPEB01332", "NPUB31231", "NPJB00415", // farming simulator
        "BLUS31465", "BLES02061", "BLES02062", "BLJM61208", "NPEB02064", "NPUB31547", // Assassin's Creed Rogue
        "BLES01667", "BLES01668", "BLES01669", "BLES01968", "BLUS30991", "BLJM61174", "BLJM60516", "NPEB01099", "NPUB30826", // Assassin's Creed 3
        "BLES01882", "BLES01883", "BLES01884", "BLUS31193", "BLJM61056", "BLES02085", "BLUS31483", "NPEB01396", "NPUB31246", // Assassin's Creed 4
        "BLES01466", "BLES01467", "BLUS30905", "BLUS31145", "BLKS20325", "NPEB00880", "NPUB30707", "BLET70017", "NPUB90674", // Assassin's Creed rev
        "BLES00909", "BLES00910", "BLES00911", "BLUS30537", "BLJM60250", "BLKS20231", "NPEB00600", "NPUB30522", "BLET70013", "NPUB90483", // Assassin's Creed bro
    ];

    private static readonly HashSet<string> KnownGamesThatRequireAccurateXfloat =
    [
        "BLES00229", "BLES00258", "BLES00887", "BLES01128", // gta4 + efls
        "BLJM55011", "BLJM60235", "BLJM60459", "BLJM60525", "BLJM61180", "BLKS20073", "BLKS20198", // gta4 + efls
        "BLUS30127", "BLUS30149", "BLUS30524", "BLUS30682", // gta4 + efls
        "NPEB00882", "NPUB30702", "NPUB30704", "NPEB00511", // gta4 + efls
        "BLES01867", "BLUS31184", "BLJS10218", "NPEB01369", "NPUB31219", "BLES01999", "NPEB01955", "NPUB50339", // metro LL
    ];

    private static readonly HashSet<string> KnownGamesThatWorkWithRelaxedZcull =
    [
        "BLAS50296", "BLES00680", "BLES01179", "BLES01294", "BLUS30418", "BLUS30711", "BLUS30758", //rdr
        "BLJM60314", "BLJM60403", "BLJM61181", "BLKS20315",
        "NPEB00833", "NPHB00465", "NPHB00466", "NPUB30638", "NPUB30639",
        "NPUB50139", // max payne 3 / rdr bundle???
        "BLAS55005", "BLES00246", "BLJM57001", "BLJM67001", "BLKS25001", "BLUS30109", "BLUS30148", //mgs4
        "NPEB00027", "NPEB02182", "NPEB90116", "NPJB00698", "NPJB90149", "NPUB31633",
        "NPHB00065", "NPHB00067",
        "BCAS20100", "BCES00664", "NPEA00057", "NPJA00031", "NPUA80105", // wipeout hd
        "BCES01584", "BCES01585", "BCUS98174", "BCJS37010", "BCAS20270", "NPEA00435", "NPUA80960", "NPJA00096", "NPHA80243", "NPEA00521", "NPUA81175", "NPJA00129", "NPHA80279", "NPEA90122", "NPUA70257", "NPHA80246", // tlou
        "BCES01175", "BCES01176", "BCUS98233", "BCJS37004", "BCAS25009", "BCES01670", "BCES01692", "BCUS99086", "BCAS25014", "NPEA00422", "NPUA80858", "NPUA70183", "BCET70034", "BCET70043", "NPUA70153", "NPUA70180", "NPHA80158", // uc3
        "BLJM61211", "BLJM55085", "BLAS50763", "NPEB02076", "NPUB31552", "NPJB00653", // Resident Evil HD
        "BLJM61272", "NPEB02226", "NPUB31689", "NPJB00726", // Resident Evil 0
        "BLES01773", "BLJM60518", "BLUS31051", "NPEB01187", "NPUB30991", "NPJB00310", "NPEB90478", "NPUB90938", "NPJB90584", "NPHB00545", // Resident Evil: Revelations
        "BLJM61249", "BLAS50796", "NPJB00684", "NPHB00720", "NPJB90764", "NPHB00719", // Yakuza 0
        "BLJM61149", "NPJB00532", "NPHB00654", "NPJB90690", // Yakuza Ishin!
        "BLJM61313", "NPJB00772", // Yakuza Kiwami
        "BLES00148", "BLES00149", "BLES00154", "BLES00155", "BLES00156", "BLUS30072", "BLJS10013", "BLKS20048", "NPEB00740", "NPUB30588", // Call of Duty 4: Modern Warfare
        "BLES01356", "BLUS30720", "BLJM60379", "BLES01794", "BLUS31155", "BLJM61012", "NPEB01268", "NPUB31117", "NPJB00335", "NPEB90366", "NPEB90439", "NPJB90524", "NPJB90581", "NPUB90813", // Dragon's Dogma
        "BLUS30089", "BLES00199", "BLES00158", "BLJM60050", "BLKS20049", "NPUB30451", "NPEB00393", // Assassin's Creed
        "BLES01898", "BLUS31194", "BLJM61014", "NPUB31245", "NPEB01428", // Armored Core: Verdict Day
        "BCES01007", "BCUS98234", "BCAS25008", "BCJS30066", "NPUA70167", "NPEA00321", "NPJA00071", "NPEA90084", "NPUA70133", "NPJA90176", "NPHA80140", "NPEA90085", "NPJA90178", "NPUA70134", "NPHA80141", "BCET01007", "NPEA90086", "BCET70024", "NPUA70118", "NPUA70138", // Killzone 3
        "BLES00362", "BLUS30190", "BLJS10046", "BLJM60368", "BLES00652", "BLUS30442", "NPEB00546", "NPUB30471", "NPJB00503", "NPHB00411", // Midnight Club: Los Angeles
        "BCUS98167", "BCJS30041", "BCES00701", "NPUA80535", "NPEA00291", "NPUA70096", "NPEA90062", "NPUA70074", // Modnation Racers
        "BCES00484", "BCUS98242", "NPEA00315", "NPUA80661", // Motorstorm Apocalypse
    ];

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

    private static readonly char[] PrioritySeparator = [' '];
    private static readonly string[] EmojiPriority = new[]{ "😱", "💢", "‼️", "❗",  "❌", "⁉️", "⚠️", "❔", "✅", "ℹ️" }
        .Select(e => e.TrimEnd('\ufe0f'))
        .ToArray();
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
                && ldrGameSerial.StartsWith("NP", StringComparison.OrdinalIgnoreCase)
                && ldrGameSerial.Equals(psnSerial, StringComparison.OrdinalIgnoreCase))
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
                var hwUpdateTask = HwInfoProvider.AddOrUpdateSystemAsync(client, message, collection, Config.Cts.Token);
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
            builder = new()
            {
                Description = "Log analysis failed. Please try again.",
                Color = Config.Colors.LogResultFailed,
            };
            if (state.TotalBytes < 2048 || state.ReadBytes < 2048)
                builder.Description = "Log analysis failed, most likely cause is an empty log. Please try again.";
        }
        return await builder.AddAuthorAsync(client, message, source, state).ConfigureAwait(false);
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
        if (items["fw_version_installed"] is { Length: >1 } fwVer
            && fwVer.Split('.') is [_, {Length: 1}])
        {
            // fix x.y0 reported as x.y for some reason in logs; x.0y is properly logged, so there's no confusion
            items["fw_version_installed"] += "0";
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
        else if (items["disable_async_shaders"] is DisabledMark or "false")
            items["shader_mode"] = "Async";
        else if (items["disable_async_shaders"] is EnabledMark or "true")
            items["shader_mode"] = "Recompiler only";
        if (items["rsx_fifo_mode"] is string rsxfm && rsxfm.StartsWith('"') && rsxfm.EndsWith('"'))
            items["rsx_fifo_mode"] = rsxfm[1..^1];
        if (items["cpu_preempt_count"] is "0")
            items["cpu_preempt_count"] = "Disabled";

        if (items["xfloat_mode"] is null) // accurate, approximate, relaxed, inaccurate
        {
            if (items["relaxed_xfloat"] is null)
            {
                items["xfloat_mode"] = (items["accurate_xfloat"], items["approximate_xfloat"]) switch
                {
                    ("true", _) => "Accurate",
                    (_, "true") => "Approximate",
                    (_, _) v => $"[{(v.Item1 == "true" ? "a" : "-")}{(v.Item2 == "true" ? "x" : "-")}]",
                };
            }
            else
            {
                items["xfloat_mode"] = (items["accurate_xfloat"], items["approximate_xfloat"], items["relaxed_xfloat"]) switch
                    {
                        ("true", "false", "true") => "Accurate",
                        ("false", "true", "true") => "Approximate",
                        ("false", "false", "true") => "Relaxed",
                        ("false", "false", "false") => "Inaccurate",
                        (_, _, _) v => $"[{(v.Item1 == "true" ? "a" : "-")}{(v.Item2 == "true" ? "x" : "-")}{(v.Item3 == "true" ? "r" : "-")}]",
                    };
            }
        }

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

    private static async ValueTask<UpdateInfo?> CheckForUpdateAsync(NameValueCollection items)
    {
        if (string.IsNullOrEmpty(items["build_and_specs"]))
            return null;

        var currentBuildCommit = items["build_commit"];
        if (string.IsNullOrEmpty(currentBuildCommit))
            currentBuildCommit = null;
        var updateInfo = await CompatClient.GetUpdateAsync(Config.Cts.Token, currentBuildCommit).ConfigureAwait(false);
        if (updateInfo.ReturnCode != StatusCode.UpdatesAvailable && currentBuildCommit is not null)
            updateInfo = await CompatClient.GetUpdateAsync(Config.Cts.Token).ConfigureAwait(false);
        var link = updateInfo.X64?.LatestBuild.Windows?.Download
                   ?? updateInfo.X64?.LatestBuild.Linux?.Download
                   ?? updateInfo.X64?.LatestBuild.Mac?.Download
                   ??updateInfo.Arm?.LatestBuild.Windows?.Download
                   ?? updateInfo.Arm?.LatestBuild.Linux?.Download
                   ?? updateInfo.Arm?.LatestBuild.Mac?.Download;
        if (updateInfo.ReturnCode is not StatusCode.UpdatesAvailable || link is null)
            return null;

        var latestBuildInfo = BuildInfoInUpdate().Match(link.ToLowerInvariant());
        if (latestBuildInfo.Success && VersionIsTooOld(items, latestBuildInfo, updateInfo))
            return updateInfo;

        return null;
    }

    private static bool VersionIsTooOld(NameValueCollection items, Match update, UpdateInfo updateInfo)
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
        if (string.IsNullOrEmpty(gpu) || foundDevices.Length is 0)
            return null;

        var info = (from line in foundDevices
                let m = VulkanDeviceInfo().Match(line)
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
            && ver is { Minor: > 400, Build: >= 0 })
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
        // see https://learn.microsoft.com/en-us/windows-hardware/drivers/display/wddm-2-1-features#driver-versioning
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
                // and https://learn.microsoft.com/en-us/windows-hardware/drivers/display/windows-vista-display-driver-model-design-guide
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
                32 => "11 24H2",
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
                19044 => "10 21H2", // deprecated
                19045 => "10 22H2",
                
                < 21390 => "10 Dev Build " + windowsVersion.Build,
                21390 => "10 21H2 Insider",
                < 22000 => "11 Internal Build " + windowsVersion.Build,
                22000 => "11 21H2", // deprecated
                < 22621 => "11 22H2 Insider Build " + windowsVersion.Build,
                22621 => "11 22H2",
                22631 => "11 23H2",
                < 23000 => "11 Beta Build " + windowsVersion.Build, // 22k series
                < 24000 => "11 Dev Build " + windowsVersion.Build, // 23k series
                < 25000 => "11 ??? Build " + windowsVersion.Build,
                < 26052 => "11 Canary Build " + windowsVersion.Build, // 25k series
                26100 => "11 24H2",
                < 26120 => "11 Dev/Canary Build " + windowsVersion.Build, // dev/canary merge branch before 24H2
                26120 => "11 24H2 Beta Build " + windowsVersion.Build,
                26200 => "11 24H2 Dev Build " + windowsVersion.Build,
                <27000 => "11 Canary Build " + windowsVersion.Build,
                _ => "11 ??? Build " + windowsVersion.Build,
            },
            _ => null,
        };

    private static string? GetLinuxVersion(string? osType, string? release, string version)
    {
        if (string.IsNullOrEmpty(release))
            return null;
            
        var kernelVersion = release;
        if (LinuxKernelVersion().Match(release) is {Success: true} m)
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
            return "SteamOS " + kernelVersion;

        if (release.Contains("-artix", StringComparison.OrdinalIgnoreCase))
            return "Artix " + kernelVersion;

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
                _ => "Unknown Apple OS",
            },
            11 => "macOS Big Sur",
            12 => "macOS Monterey",
            13 => "macOS Ventura",
            14 => "macOS Sonoma",
            15 => "macOS Sequoia",
            26 => "macOS Tahoe",
            _ => "Unknown Apple OS",
        };

    internal static bool IsAmd(string gpuInfo)
        => gpuInfo.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
           gpuInfo.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
           gpuInfo.Contains("ATI ", StringComparison.OrdinalIgnoreCase);

    internal static bool IsNvidia(string gpuInfo)
        => gpuInfo.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
           gpuInfo.Contains("nVidia", StringComparison.OrdinalIgnoreCase) ||
           gpuInfo.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
           gpuInfo.Contains("GTX", StringComparison.OrdinalIgnoreCase);

    internal static bool IsIntel(string gpuInfo)
        => gpuInfo.Contains("Intel", StringComparison.OrdinalIgnoreCase);

    private static string GetTimeFormat(long microseconds)
        => microseconds switch
        {
            <1000 => $"{microseconds} µs",
            <1_000_000 => $"{microseconds / 1000.0:0.##} ms",
            _ => $"{microseconds / 1_000_000.0:0.##} s"
        };

    private static List<string> SortLines(List<string> notes, DiscordEmoji? piracyEmoji = null)
    {
        if (notes.Count < 2)
            return notes;

        var priorityList = new List<string>(EmojiPriority);
        if (piracyEmoji is not null)
            priorityList.Insert(0, piracyEmoji.ToString());
        return notes
            .Select(s =>
            {
                var prioritySymbol = s.Split(PrioritySeparator, 2)[0].TrimEnd('\ufe0f');
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
            var match = ProgramHashPatch().Match(patch);
            if (match.Success)
            {
                _ = int.TryParse(match.Groups["patch_count"].Value, out var pCount);
                if (!onlyApplied || pCount > 0)
                    result[match.Groups["hash"].Value] = pCount;
            }
        }
        return result;
    }

    internal static async ValueTask<DiscordEmbedBuilder> AddAuthorAsync(this DiscordEmbedBuilder builder, DiscordClient client, DiscordMessage? message, ISource source, LogParseState? state = null)
    {
        if (message == null || state?.Error == LogParseState.ErrorCode.PiracyDetected)
            return builder;

        var author = message.Author;
        var member = await client.GetMemberAsync(message.Channel?.Guild, author).ConfigureAwait(false);
        string msg;
        if (member == null)
            msg = $"Log from {author.Username.Sanitize()} | {author.Id}\n";
        else
            msg = $"Log from {member.DisplayName.Sanitize()} | {member.Id}\n";
        msg += " | " + source.SourceType;
        if (state?.ReadBytes > 0 && source.LogFileSize is >0 and <2L*1024*1024*1024 && state.ReadBytes <= source.LogFileSize)
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

    private static (int numerator, int denominator) Reduce(int numerator, int denominator)
    {
        var gcd = Gcd(numerator, denominator);
        return (numerator / gcd, denominator / gcd);
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

    private static bool TryGetRpcs3Version(NameValueCollection items, out Version? version)
    {
        version = null;
        return items["build_branch"] is "HEAD" or "master" && Version.TryParse(items["build_full_version"], out version);
    }
}
