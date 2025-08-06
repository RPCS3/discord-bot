﻿using System.ComponentModel.DataAnnotations;
using CompatApiClient;
using Microsoft.EntityFrameworkCore;
using Nito.AsyncEx;

namespace CompatBot.Database;

internal class ThumbnailDb : DbContext
{
    private static readonly AsyncReaderWriterLock DbLockSource = new();
    private static int openReadCount, openWriteCount;
    private readonly IDisposable readWriteLock;
    private readonly bool canWrite;

    public DbSet<State> State { get; set; } = null!;
    public DbSet<Thumbnail> Thumbnail { get; set; } = null!;
    public DbSet<GameUpdateInfo> GameUpdateInfo { get; set; } = null!;
    public DbSet<SyscallInfo> SyscallInfo { get; set; } = null!;
    public DbSet<SyscallToProductMap> SyscallToProductMap { get; set; } = null!;
    public DbSet<Metacritic> Metacritic { get; set; } = null!;
    public DbSet<Fortune> Fortune { get; set; } = null!;
    public DbSet<NamePool> NamePool { get; set; } = null!;

    [Obsolete("For migrations only")]
    public ThumbnailDb(): this(DbLockSource.WriterLock(Config.Cts.Token))
    {
    }

    private ThumbnailDb(IDisposable readWriteLock, bool canWrite = false)
    {
        this.readWriteLock = readWriteLock;
        this.canWrite = canWrite;
#if DEBUG
        if (canWrite)
            Interlocked.Increment(ref openWriteCount);
        else
            Interlocked.Increment(ref openReadCount);
        //var st = new System.Diagnostics.StackTrace().GetCaller<ThumbnailDb>();
        //Config.Log.Debug($"{nameof(ThumbnailDb)}>>>{(canWrite ? "Write" : "Read")} (r/w: {openReadCount}/{openWriteCount}) #{readWriteLock.GetHashCode():x8} from {st}");
#endif
    }

    public static async ValueTask<ThumbnailDb> OpenReadAsync()
        => new(await DbLockSource.ReaderLockAsync(Config.Cts.Token).ConfigureAwait(false));

    public static async ValueTask<ThumbnailDb> OpenWriteAsync()
        => new(await DbLockSource.WriterLockAsync(Config.Cts.Token).ConfigureAwait(false));

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = DbImporter.GetDbPath("thumbs.db", Environment.SpecialFolder.LocalApplicationData);
        if (Config.EnableEfDebugLogging)
            optionsBuilder.UseLoggerFactory(Config.LoggerFactory);
        optionsBuilder.UseSqlite($""" Data Source="{dbPath}" """);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        //configure indices
        modelBuilder.Entity<State>().HasIndex(s => s.Locale).IsUnique().HasDatabaseName("state_locale");
        modelBuilder.Entity<State>().HasIndex(s => s.Timestamp).HasDatabaseName("state_timestamp");
        modelBuilder.Entity<Thumbnail>().HasIndex(m => m.ProductCode).IsUnique().HasDatabaseName("thumbnail_product_code");
        modelBuilder.Entity<Thumbnail>().HasIndex(m => m.ContentId).IsUnique().HasDatabaseName("thumbnail_content_id");
        modelBuilder.Entity<Thumbnail>().HasIndex(m => m.Timestamp).HasDatabaseName("thumbnail_timestamp");
        modelBuilder.Entity<Thumbnail>().Property(m => m.Name).UseCollation("NOCASE");
        modelBuilder.Entity<GameUpdateInfo>().HasIndex(ui => ui.ProductCode).IsUnique().HasDatabaseName("game_update_info_product_code");
        modelBuilder.Entity<SyscallInfo>().HasIndex(sci => sci.Function).HasDatabaseName("syscall_info_function");
        modelBuilder.Entity<SyscallToProductMap>().HasKey(m => new {m.ProductId, m.SyscallInfoId});
        modelBuilder.Entity<Fortune>();
        modelBuilder.Entity<NamePool>();

        //configure default policy of Id being the primary key
        modelBuilder.ConfigureDefaultPkConvention();

        //configure name conversion for all configured entities from CamelCase to snake_case
        modelBuilder.ConfigureMapping(NamingStyles.Underscore);
    }

    public override void Dispose()
    {
        base.Dispose();
        readWriteLock.Dispose();
#if DEBUG
        if (canWrite)
            Interlocked.Decrement(ref openWriteCount);
        else
            Interlocked.Decrement(ref openReadCount);
        Config.Log.Debug($"{nameof(ThumbnailDb)}<<<{(canWrite ? "Write" : "Read")} (r/w: {openReadCount}/{openWriteCount}) #{readWriteLock.GetHashCode():x8}");
#endif
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        readWriteLock.Dispose();
#if DEBUG
        if (canWrite)
            Interlocked.Decrement(ref openWriteCount);
        else
            Interlocked.Decrement(ref openReadCount);
        Config.Log.Debug($"{nameof(ThumbnailDb)}<<<{(canWrite ? "Write" : "Read")} (r/w: {openReadCount}/{openWriteCount}) #{readWriteLock.GetHashCode():x8}");
#endif
    }
}

internal class State
{
    public int Id { get; set; }
    public string? Locale { get; set; }
    public long Timestamp { get; set; }
}

internal class Thumbnail
{
    public int Id { get; set; }
    [Required]
    public string ProductCode { get; set; } = null!;
    public string? ContentId { get; set; }
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? EmbeddableUrl { get; set; }
    public long Timestamp { get; set; }
    public int? EmbedColor { get; set; }
    public CompatStatus? CompatibilityStatus { get; set; }
    public long? CompatibilityChangeDate { get; set; }

    public int? MetacriticId { get; set; }
    public Metacritic? Metacritic { get; set; }

    public List<SyscallToProductMap> SyscallToProductMap { get; set; } = null!;
}

internal class GameUpdateInfo
{
    public int Id { get; set; }
    [Required]
    public string ProductCode { get; set; } = null!;
    public int MetaHash { get; set; }
    [Required]
    public string MetaXml { get; set; } = null!;
    public long Timestamp { get; set; }
}

public enum CompatStatus : byte
{
    Unknown = 0,
    Nothing = 10,
    Loadable = 20,
    Intro = 30,
    Ingame = 40,
    Playable = 50,
}

internal class SyscallInfo
{
    public int Id { get; set; }
    [Required]
    public string Function { get; set; } = null!;

    public List<SyscallToProductMap> SyscallToProductMap { get; set; } = null!;
}

internal class SyscallToProductMap
{
    public int ProductId { get; set; }
    public Thumbnail Product { get; set; } = null!;

    public int SyscallInfoId { get; set; }
    public SyscallInfo SyscallInfo { get; set; } = null!;
}

internal class Metacritic
{
    public int Id { get; set; }
    [Required]
    public string Title { get; set; } = null!;
    public byte? CriticScore { get; set; }
    public byte? UserScore { get; set; }
    public string? Notes { get; set; }

    public Metacritic WithTitle(string title)
    {
        return new()
        {
            Title = title,
            CriticScore = CriticScore,
            UserScore = UserScore,
            Notes = Notes,
        };
    }
}

internal class Fortune
{
    public int Id { get; set; }
    [Required]
    public string Content { get; set; } = null!;
}
    
internal class NamePool
{
    public int Id { get; set; }
    [Required]
    public string Name { get; set; } = null!;
}