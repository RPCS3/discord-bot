using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Migrations
{
    public partial class RemoveTitleInfoTable : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "title_info");

            migrationBuilder.AddColumn<int>(
                name: "embed_color",
                table: "thumbnail",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "embed_color",
                table: "thumbnail");

            migrationBuilder.CreateTable(
                name: "title_info",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    content_id = table.Column<string>(type: "TEXT", nullable: false),
                    embed_color = table.Column<int>(type: "INTEGER", nullable: true),
                    thumbnail_embeddable_url = table.Column<string>(type: "TEXT", nullable: true),
                    thumbnail_url = table.Column<string>(type: "TEXT", nullable: true),
                    timestamp = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "title_info_content_id",
                table: "title_info",
                column: "content_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "title_info_timestamp",
                table: "title_info",
                column: "timestamp");
        }
    }
}
