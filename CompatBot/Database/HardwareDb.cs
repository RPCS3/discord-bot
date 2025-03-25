using System.ComponentModel.DataAnnotations;
using CompatApiClient;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database;

internal class HardwareDb : DbContext
{
    public DbSet<HwInfo> HwInfo { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = DbImporter.GetDbPath("hw.db", Environment.SpecialFolder.LocalApplicationData);
        if (Config.EnableEfDebugLogging)
            optionsBuilder.UseLoggerFactory(Config.LoggerFactory);
        optionsBuilder.UseSqlite($""" Data Source="{dbPath}" """);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation("NOCASE");
        modelBuilder.Entity<HwInfo>().HasIndex(m => m.Timestamp).HasDatabaseName("hardware_timestamp");

        //configure name conversion for all configured entities from CamelCase to snake_case
        modelBuilder.ConfigureMapping(NamingStyles.Underscore);
    }
}

[Flags]
internal enum CpuFeatures
{
    None     = 0b_00000000_00000000_00000000_00000000,
    Avx      = 0b_00000000_00000000_00000000_00000001,
    Avx2     = 0b_00000000_00000000_00000000_00000010,
    Avx512   = 0b_00000000_00000000_00000000_00000100,
    Avx512IL = 0b_00000000_00000000_00000000_00001000,
    Fma3     = 0b_00000000_00000000_00000000_00010000,
    Fma4     = 0b_00000000_00000000_00000000_00100000,
    Tsx      = 0b_00000000_00000000_00000000_01000000,
    TsxFa    = 0b_00000000_00000000_00000000_10000000,
    Xop      = 0b_00000000_00000000_00000001_00000000,
}

internal enum OsType : byte
{
    Unknown = 0,
    Windows = 1,
    Linux = 2,
    MacOs = 3,
    Bsd = 4,
}

internal class HwInfo
{
    public long Timestamp { get; set; }
    [Required, Key, MinLength(128/8), MaxLength(512/8)]
    public byte[] InstallId { get; set; } = null!; // this should be either a guid or a hash of somewhat unique data (discord user id, user profile name from logs, etc)

    [Required]
    public string CpuMaker { get; set; } = null!;
    [Required]
    public string CpuModel { get; set; } = null!;
    public int ThreadCount { get; set; }
    public CpuFeatures CpuFeatures { get; set; }

    public long RamInMb { get; set; }
    
    [Required]
    public string GpuMaker { get; set; } = null!;
    [Required]
    public string GpuModel { get; set; } = null!;

    public OsType OsType { get; set; }
    public string? OsName { get; set; }
    public string? OsVersion { get; set; }
}