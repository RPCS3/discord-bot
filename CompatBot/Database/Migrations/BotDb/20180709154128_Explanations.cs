using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Database.Migrations
{
    public partial class Explanations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "issuer_id",
                table: "warning",
                nullable: false,
                defaultValue: 0ul);

            migrationBuilder.CreateTable(
                name: "explanation",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                    ,
                    keyword = table.Column<string>(nullable: false),
                    text = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "warning_discord_id",
                table: "warning",
                column: "discord_id");

            migrationBuilder.CreateIndex(
                name: "explanation_keyword",
                table: "explanation",
                column: "keyword",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "explanation");

            migrationBuilder.DropIndex(
                name: "warning_discord_id",
                table: "warning");

            migrationBuilder.DropColumn(
                name: "issuer_id",
                table: "warning");
        }
    }
}
