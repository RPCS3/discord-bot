using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Migrations
{
    public partial class ThumbsColorCache : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "title_info",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    content_id = table.Column<string>(nullable: false),
                    thumbnail_url = table.Column<string>(nullable: true),
                    thumbnail_embeddable_url = table.Column<string>(nullable: true),
                    embed_color = table.Column<int>(nullable: true),
                    timestamp = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "thumbnail_content_id",
                table: "thumbnail",
                column: "content_id",
                unique: true);

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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "title_info");

            migrationBuilder.DropIndex(
                name: "thumbnail_content_id",
                table: "thumbnail");
        }
    }
}
