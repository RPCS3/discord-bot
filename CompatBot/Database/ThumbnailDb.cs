using System.ComponentModel.DataAnnotations;
using CompatApiClient;
using Microsoft.EntityFrameworkCore;

namespace CompatBot.Database
{
    internal class ThumbnailDb: DbContext
    {
        public DbSet<State> State { get; set; }
        public DbSet<Thumbnail> Thumbnail { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite("Data Source=thumbs.db");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            //configure indices
            modelBuilder.Entity<State>().HasIndex(s => s.Locale).IsUnique().HasName("state_locale");
            modelBuilder.Entity<State>().HasIndex(s => s.Timestamp).HasName("state_timestamp");
            modelBuilder.Entity<Thumbnail>().HasIndex(m => m.ProductCode).IsUnique().HasName("thumbnail_product_code");
            modelBuilder.Entity<Thumbnail>().HasIndex(m => m.Timestamp).HasName("thumbnail_timestamp");

            //configure default policy of Id being the primary key
            modelBuilder.ConfigureDefaultPkConvention();

            //configure name conversion for all configured entities from CamelCase to snake_case
            modelBuilder.ConfigureMapping(NamingStyles.Underscore);
        }
    }

    internal class State
    {
        public int Id { get; set; }
        public string Locale { get; set; }
        public long Timestamp { get; set; }
    }

    internal class Thumbnail
    {
        public int Id { get; set; }
        [Required]
        public string ProductCode { get; set; }
        public string ContentId { get; set; }
        public string Url { get; set; }
        public string EmbeddableUrl { get; set; }
        public long Timestamp { get; set; }
    }
}
