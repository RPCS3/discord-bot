using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CompatApiClient;
using CompatBot.Utils.Extensions;
using Microsoft.EntityFrameworkCore;
using Nito.AsyncEx;

namespace CompatBot.Database;

internal class BotDb: DbContext
{
    private static readonly AsyncReaderWriterLock DbLockSource = new();
    private static int openReadCount, openWriteCount;
    private readonly IDisposable readWriteLock;
    private readonly bool canWrite;
    
    public DbSet<BotState> BotState { get; set; } = null!;
    public DbSet<Moderator> Moderator { get; set; } = null!;
    public DbSet<Piracystring> Piracystring { get; set; } = null!;
    public DbSet<SuspiciousString> SuspiciousString { get; set; } = null!;
    public DbSet<Warning> Warning { get; set; } = null!;
    public DbSet<Explanation> Explanation { get; set; } = null!;
    public DbSet<DisabledCommand> DisabledCommands { get; set; } = null!;
    public DbSet<WhitelistedInvite> WhitelistedInvites { get; set; } = null!;
    public DbSet<EventSchedule> EventSchedule { get; set; } = null!;
    public DbSet<Stats> Stats { get; set; } = null!;
    public DbSet<Kot> Kot { get; set; } = null!;
    public DbSet<Doggo> Doggo { get; set; } = null!;
    public DbSet<ForcedNickname> ForcedNicknames { get; set; } = null!;

    private BotDb(IDisposable readWriteLock, bool canWrite = false)
    {
        this.readWriteLock = readWriteLock;
        this.canWrite = canWrite;
//#if DEBUG
        if (canWrite)
            Interlocked.Increment(ref openWriteCount);
        else
            Interlocked.Increment(ref openReadCount);
        var st = new System.Diagnostics.StackTrace().GetCaller<BotDb>();
        Config.Log.Debug($"{nameof(BotDb)}>>>{(canWrite ? "Write" : "Read")} (r/w: {openReadCount}/{openWriteCount}) #{readWriteLock.GetHashCode():x8} from {st}");
//#endif
    }

    public static BotDb OpenRead()
        => new(DbLockSource.ReaderLock(Config.Cts.Token), canWrite: false);

    public static async ValueTask<BotDb> OpenReadAsync()
        => new(await DbLockSource.ReaderLockAsync(Config.Cts.Token).ConfigureAwait(false), canWrite: false);

    public static BotDb OpenWrite()
        => new(DbLockSource.WriterLock(Config.Cts.Token), canWrite: true);
    
    public static async ValueTask<BotDb> OpenWriteAsync()
        => new(await DbLockSource.WriterLockAsync(Config.Cts.Token).ConfigureAwait(false), canWrite: true);
    
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = DbImporter.GetDbPath("bot.db", Environment.SpecialFolder.ApplicationData);
        if (Config.EnableEfDebugLogging)
            optionsBuilder.UseLoggerFactory(Config.LoggerFactory);
        optionsBuilder.UseSqlite($""" Data Source="{dbPath}" """);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        //configure indices
        modelBuilder.Entity<BotState>().HasIndex(m => m.Key).IsUnique().HasDatabaseName("bot_state_key");
        modelBuilder.Entity<Moderator>().HasIndex(m => m.DiscordId).IsUnique().HasDatabaseName("moderator_discord_id");
        modelBuilder.Entity<Piracystring>().Property(ps => ps.Context).HasDefaultValue(FilterContext.Chat | FilterContext.Log);
        modelBuilder.Entity<Piracystring>().Property(ps => ps.Actions).HasDefaultValue(FilterAction.RemoveContent | FilterAction.IssueWarning | FilterAction.SendMessage);
        modelBuilder.Entity<Piracystring>().HasIndex(ps => ps.String).HasDatabaseName("piracystring_string");
        modelBuilder.Entity<SuspiciousString>().HasIndex(ss => ss.String).HasDatabaseName("suspicious_string_string");
        modelBuilder.Entity<Warning>().HasIndex(w => w.DiscordId).HasDatabaseName("warning_discord_id");
        modelBuilder.Entity<Explanation>().HasIndex(e => e.Keyword).IsUnique().HasDatabaseName("explanation_keyword");
        modelBuilder.Entity<DisabledCommand>().HasIndex(c => c.Command).IsUnique().HasDatabaseName("disabled_command_command");
        modelBuilder.Entity<WhitelistedInvite>().HasIndex(i => i.GuildId).IsUnique().HasDatabaseName("whitelisted_invite_guild_id");
        modelBuilder.Entity<EventSchedule>().HasIndex(e => new {e.Year, e.EventName}).HasDatabaseName("event_schedule_year_event_name");
        modelBuilder.Entity<Stats>().HasIndex(s => new { s.Category, s.Bucket, s.Key }).IsUnique().HasDatabaseName("stats_category_bucket_key");
        modelBuilder.Entity<Kot>().HasIndex(k => k.UserId).IsUnique().HasDatabaseName("kot_user_id");
        modelBuilder.Entity<Doggo>().HasIndex(d => d.UserId).IsUnique().HasDatabaseName("doggo_user_id");
        modelBuilder.Entity<ForcedNickname>().HasIndex(d => new { d.GuildId, d.UserId }).IsUnique().HasDatabaseName("forced_nickname_guild_id_user_id");

        //configure default policy of Id being the primary key
        modelBuilder.ConfigureDefaultPkConvention();

        //configure name conversion for all configured entities from CamelCase to snake_case
        modelBuilder.ConfigureMapping(NamingStyles.Underscore);
    }

    public override void Dispose()
    {
        base.Dispose();
        readWriteLock.Dispose();
//#if DEBUG
        if (canWrite)
            Interlocked.Decrement(ref openWriteCount);
        else
            Interlocked.Decrement(ref openReadCount);
        Config.Log.Debug($"{nameof(BotDb)}<<<{(canWrite ? "Write" : "Read")} (r/w: {openReadCount}/{openWriteCount}) #{readWriteLock.GetHashCode():x8}");
//#endif
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        readWriteLock.Dispose();
//#if DEBUG
        if (canWrite)
            Interlocked.Decrement(ref openWriteCount);
        else
            Interlocked.Decrement(ref openReadCount);
        Config.Log.Debug($"{nameof(BotDb)}<<<{(canWrite ? "Write" : "Read")} (r/w: {openReadCount}/{openWriteCount}) #{readWriteLock.GetHashCode():x8}");
//#endif
    }
}

internal class BotState
{
    public int Id { get; set; }
    [Required]
    public string Key { get; set; } = null!;
    public string? Value { get; set; }
}

internal class Moderator
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public bool Sudoer { get; set; }
}

public class Piracystring
{
    public int Id { get; set; }
    [Required, Column(TypeName = "varchar(255)")]
    public string String { get; set; } = null!;
    public string? ValidatingRegex { get; set; }
    public FilterContext Context { get; set; }
    public FilterAction Actions { get; set; }
    public string? ExplainTerm { get; set; }
    public string? CustomMessage { get; set; }
    public bool Disabled { get; set; }
}

public class SuspiciousString
{
    public int Id { get; set; }
    [Required]
    public string String { get; set; } = null!;
}

[Flags]
public enum FilterContext: byte
{
    //None = 0b_0000_0000, do NOT add this
    Chat = 0b_0000_0001,
    Log  = 0b_0000_0010,
}

[Flags]
public enum FilterAction
{
    //None          = 0b_0000_0000, do NOT add this
    RemoveContent = 0b_0000_0001,
    IssueWarning  = 0b_0000_0010,
    ShowExplain   = 0b_0000_0100,
    SendMessage   = 0b_0000_1000,
    MuteModQueue  = 0b_0001_0000,
    Kick          = 0b_0010_0000,
}

internal class Warning
{
    public int Id { get; set; }
    public ulong DiscordId { get; set; }
    public ulong IssuerId { get; set; }
    [Required]
    public string Reason { get; set; } = null!;
    [Required]
    public string FullReason { get; set; } = null!;
    public long? Timestamp { get; set; }
    public bool Retracted { get; set; }
    public ulong? RetractedBy { get; set; }
    public string? RetractionReason { get; set; }
    public long? RetractionTimestamp { get; set; }
}

internal class Explanation
{
    public int Id { get; set; }
    [Required]
    public string Keyword { get; set; } = null!;
    [Required]
    public string? Text { get; set; } = null!;
    [MaxLength(7*1024*1024)]
    public byte[]? Attachment { get; set; }
    public string? AttachmentFilename { get; set; }
}

internal class DisabledCommand
{
    public int Id { get; set; }
    [Required]
    public string Command { get; set; } = null!;
}

internal class WhitelistedInvite
{
    public int Id { get; set; }
    public ulong GuildId { get; set; }
    public string? Name { get; set; }
    public string? InviteCode { get; set; }
}

internal class EventSchedule
{
    public int Id { get; set; }
    public int Year { get; set; }
    public long Start { get; set; }
    public long End { get; set; }
    public string? Name { get; set; }
    public string? EventName { get; set; }
}

internal class Stats
{
    public int Id { get; set; }
    [Required]
    public string Category { get; set; } = null!;
    public string? Bucket { get; set; }
    [Required]
    public string Key { get; set; } = null!;
    public int Value { get; set; }
    public long ExpirationTimestamp { get; set; }
}

internal class Kot
{
    public int Id { get; set; }
    public ulong UserId { get; set; }
}

internal class Doggo
{
    public int Id { get; set; }
    public ulong UserId { get; set; }
}

internal class ForcedNickname
{
    public int Id { get; set; }
    public ulong GuildId { set; get; }
    public ulong UserId { set; get; }
    [Required]
    public string Nickname { get; set; } = null!;
}