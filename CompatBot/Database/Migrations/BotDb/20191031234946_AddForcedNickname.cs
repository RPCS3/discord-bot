using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Database.Migrations
{
    public partial class AddForcedNickname : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forced_nicknames",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<ulong>(nullable: false),
                    guild_id = table.Column<ulong>(nullable: false),
                    nickname = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "forced_nickname_user_id_guild_id",
                table: "forced_nicknames",
                columns: new[] { "user_id", "guild_id" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forced_nicknames");
        }
    }
}
