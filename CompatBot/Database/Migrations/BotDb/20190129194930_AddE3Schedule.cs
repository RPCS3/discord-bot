using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Database.Migrations
{
    public partial class AddE3Schedule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "e3_schedule",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    year = table.Column<int>(nullable: false),
                    start = table.Column<long>(nullable: false),
                    end = table.Column<long>(nullable: false),
                    name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "e3schedule_year",
                table: "e3_schedule",
                column: "year");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "e3_schedule");
        }
    }
}
