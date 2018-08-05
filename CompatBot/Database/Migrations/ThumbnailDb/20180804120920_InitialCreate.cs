using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "state",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    locale = table.Column<string>(nullable: true),
                    timestamp = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "thumbnail",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    product_code = table.Column<string>(nullable: false),
                    content_id = table.Column<string>(nullable: true),
                    name = table.Column<string>(nullable: true),
                    url = table.Column<string>(nullable: true),
                    embeddable_url = table.Column<string>(nullable: true),
                    timestamp = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "state_locale",
                table: "state",
                column: "locale",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "state_timestamp",
                table: "state",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "thumbnail_product_code",
                table: "thumbnail",
                column: "product_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "thumbnail_timestamp",
                table: "thumbnail",
                column: "timestamp");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "state");

            migrationBuilder.DropTable(
                name: "thumbnail");
        }
    }
}
