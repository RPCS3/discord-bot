using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Database.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "moderator",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                         .Annotation("Sqlite:Autoincrement", true)
                    ,
                    discord_id = table.Column<ulong>(nullable: false),
                    sudoer = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "piracystring",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                    ,
                    @string = table.Column<string>(name: "string", type: "varchar(255)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "warning",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true)
                    ,
                    discord_id = table.Column<ulong>(nullable: false),
                    reason = table.Column<string>(nullable: false),
                    full_reason = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "moderator_discord_id",
                table: "moderator",
                column: "discord_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "piracystring_string",
                table: "piracystring",
                column: "string",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "moderator");

            migrationBuilder.DropTable(
                name: "piracystring");

            migrationBuilder.DropTable(
                name: "warning");
        }
    }
}
