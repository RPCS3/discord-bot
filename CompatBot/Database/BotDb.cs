using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CompatApiClient;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database
{
    internal class BotDb: DbContext
    {
        public DbSet<BotState> BotState { get; set; }
        public DbSet<Moderator> Moderator { get; set; }
        public DbSet<Piracystring> Piracystring { get; set; }
        public DbSet<Warning> Warning { get; set; }
        public DbSet<Explanation> Explanation { get; set; }
        public DbSet<DisabledCommand> DisabledCommands { get; set; }
        public DbSet<WhitelistedInvite> WhitelistedInvites { get; set; }
        public DbSet<EventSchedule> EventSchedule { get; set; }
        public DbSet<Stats> Stats { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbPath = DbImporter.GetDbPath("bot.db", Environment.SpecialFolder.ApplicationData);
#if DEBUG
            optionsBuilder.UseLoggerFactory(Config.LoggerFactory);
#endif
            optionsBuilder.UseSqlite($"Data Source=\"{dbPath}\"");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //configure indices
            modelBuilder.Entity<BotState>().HasIndex(m => m.Key).IsUnique().HasName("bot_state_key");
            modelBuilder.Entity<Moderator>().HasIndex(m => m.DiscordId).IsUnique().HasName("moderator_discord_id");
            modelBuilder.Entity<Piracystring>().Property(ps => ps.Context).HasDefaultValue(FilterContext.Chat | FilterContext.Log);
            modelBuilder.Entity<Piracystring>().Property(ps => ps.Actions).HasDefaultValue(FilterAction.RemoveContent | FilterAction.IssueWarning | FilterAction.SendMessage);
            modelBuilder.Entity<Piracystring>().HasIndex(ps => ps.String).HasName("piracystring_string");
            modelBuilder.Entity<Warning>().HasIndex(w => w.DiscordId).HasName("warning_discord_id");
            modelBuilder.Entity<Explanation>().HasIndex(e => e.Keyword).IsUnique().HasName("explanation_keyword");
            modelBuilder.Entity<DisabledCommand>().HasIndex(c => c.Command).IsUnique().HasName("disabled_command_command");
            modelBuilder.Entity<WhitelistedInvite>().HasIndex(i => i.GuildId).IsUnique().HasName("whitelisted_invite_guild_id");
            modelBuilder.Entity<EventSchedule>().HasIndex(e => new {e.Year, e.EventName}).HasName("event_schedule_year_event_name");
            modelBuilder.Entity<Stats>().HasIndex(s => new { s.Category, s.Key }).IsUnique().HasName("stats_category_key");

            //configure default policy of Id being the primary key
            modelBuilder.ConfigureDefaultPkConvention();

            //configure name conversion for all configured entities from CamelCase to snake_case
            modelBuilder.ConfigureMapping(NamingStyles.Underscore);
        }
    }

    internal class BotState
    {
        public int Id { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
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
        public string String { get; set; }
        public string ValidatingRegex { get; set; }
        public FilterContext Context { get; set; }
        public FilterAction Actions { get; set; }
        public string ExplainTerm { get; set; }
        public string CustomMessage { get; set; }
        public bool Disabled { get; set; }
    }

    [Flags]
    public enum FilterContext: byte
    {
        Chat = 0b_0000_0001,
        Log  = 0b_0000_0010,
    }

    [Flags]
    public enum FilterAction
    {
        RemoveContent = 0b_0000_0001,
        IssueWarning  = 0b_0000_0010,
        ShowExplain   = 0b_0000_0100,
        SendMessage   = 0b_0000_1000,
    }

    internal class Warning
    {
        public int Id { get; set; }
        public ulong DiscordId { get; set; }
        public ulong IssuerId { get; set; }
        [Required]
        public string Reason { get; set; }
        [Required]
        public string FullReason { get; set; }
        public long? Timestamp { get; set; }
        public bool Retracted { get; set; }
        public ulong? RetractedBy { get; set; }
        public string RetractionReason { get; set; }
        public long? RetractionTimestamp { get; set; }
    }

    internal class Explanation
    {
        public int Id { get; set; }
        [Required]
        public string Keyword { get; set; }
        [Required]
        public string Text { get; set; }
        [MaxLength(7*1024*1024)]
        public byte[] Attachment { get; set; }
        public string AttachmentFilename { get; set; }
    }

    internal class DisabledCommand
    {
        public int Id { get; set; }
        [Required]
        public string Command { get; set; }
    }

    internal class WhitelistedInvite
    {
        public int Id { get; set; }
        public ulong GuildId { get; set; }
        public string Name { get; set; }
        public string InviteCode { get; set; }
    }

    internal class EventSchedule
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public long Start { get; set; }
        public long End { get; set; }
        public string Name { get; set; }
        public string EventName { get; set; }
    }

    internal class Stats
    {
        public int Id { get; set; }
        [Required]
        public string Category { get; set; }
        [Required]
        public string Key { get; set; }
        public int Value { get; set; }
        public long ExpirationTimestamp { get; set; }
    }
}
