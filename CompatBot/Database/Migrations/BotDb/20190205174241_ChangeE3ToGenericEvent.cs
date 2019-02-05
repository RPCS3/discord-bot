using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Database.Migrations
{
    public partial class ChangeE3ToGenericEvent : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_schedule",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    year = table.Column<int>(nullable: false),
                    start = table.Column<long>(nullable: false),
                    end = table.Column<long>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    event_name = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "event_schedule_year_event_name",
                table: "event_schedule",
                columns: new[] { "year", "event_name" });

            migrationBuilder.Sql("INSERT INTO event_schedule SELECT id, year, start, end, name, 'E3' AS event_name FROM e3_schedule");

            migrationBuilder.DropTable(
                name: "e3_schedule");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_schedule");

            migrationBuilder.CreateTable(
                name: "e3_schedule",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    end = table.Column<long>(nullable: false),
                    name = table.Column<string>(nullable: true),
                    start = table.Column<long>(nullable: false),
                    year = table.Column<int>(nullable: false)
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
    }
}
