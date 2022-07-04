using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompatBot.Database.Migrations
{
    public partial class AddStatsBucketColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "stats_category_key",
                table: "stats");

            migrationBuilder.AddColumn<string>(
                name: "bucket",
                table: "stats",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "stats_category_bucket_key",
                table: "stats",
                columns: new[] { "category", "bucket", "key" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "stats_category_bucket_key",
                table: "stats");

            migrationBuilder.DropColumn(
                name: "bucket",
                table: "stats");

            migrationBuilder.CreateIndex(
                name: "stats_category_key",
                table: "stats",
                columns: new[] { "category", "key" },
                unique: true);
        }
    }
}
