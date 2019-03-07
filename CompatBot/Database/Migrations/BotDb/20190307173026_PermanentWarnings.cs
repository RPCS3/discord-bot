using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Database.Migrations
{
    public partial class PermanentWarnings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "retracted",
                table: "warning",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<ulong>(
                name: "retracted_by",
                table: "warning",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retraction_reason",
                table: "warning",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "retraction_timestamp",
                table: "warning",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "retracted",
                table: "warning");

            migrationBuilder.DropColumn(
                name: "retracted_by",
                table: "warning");

            migrationBuilder.DropColumn(
                name: "retraction_reason",
                table: "warning");

            migrationBuilder.DropColumn(
                name: "retraction_timestamp",
                table: "warning");
        }
    }
}
