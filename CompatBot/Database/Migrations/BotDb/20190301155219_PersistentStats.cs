using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Database.Migrations
{
    public partial class PersistentStats : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stats",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    category = table.Column<string>(nullable: false),
                    key = table.Column<string>(nullable: false),
                    value = table.Column<int>(nullable: false),
                    expiration_timestamp = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "stats_category_key",
                table: "stats",
                columns: new[] { "category", "key" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stats");
        }
    }
}
