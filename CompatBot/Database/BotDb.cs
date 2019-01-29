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
        public DbSet<E3Schedule> E3Schedule { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=bot.db");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //configure indices
            modelBuilder.Entity<BotState>().HasIndex(m => m.Key).IsUnique().HasName("bot_state_key");
            modelBuilder.Entity<Moderator>().HasIndex(m => m.DiscordId).IsUnique().HasName("moderator_discord_id");
            modelBuilder.Entity<Piracystring>().HasIndex(ps => ps.String).IsUnique().HasName("piracystring_string");
            modelBuilder.Entity<Warning>().HasIndex(w => w.DiscordId).HasName("warning_discord_id");
            modelBuilder.Entity<Explanation>().HasIndex(e => e.Keyword).IsUnique().HasName("explanation_keyword");
            modelBuilder.Entity<DisabledCommand>().HasIndex(c => c.Command).IsUnique().HasName("disabled_command_command");
            modelBuilder.Entity<WhitelistedInvite>().HasIndex(i => i.GuildId).IsUnique().HasName("whitelisted_invite_guild_id");
            modelBuilder.Entity<E3Schedule>().HasIndex(e3s => e3s.Year).HasName("e3schedule_year");

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

    internal class Piracystring
    {
        public int Id { get; set; }
        [Required, Column(TypeName = "varchar(255)")]
        public string String { get; set; }
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
    }

    internal class Explanation
    {
        public int Id { get; set; }
        [Required]
        public string Keyword { get; set; }
        [Required]
        public string Text { get; set; }
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

    internal class E3Schedule
    {
        public int Id { get; set; }
        public int Year { get; set; }
        public long Start { get; set; }
        public long End { get; set; }
        public string Name { get; set; }
    }
}
