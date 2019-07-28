using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Database.Migrations
{
    public partial class AllowDuplicateTriggers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "piracystring_string",
                table: "piracystring");

            migrationBuilder.CreateIndex(
                name: "piracystring_string",
                table: "piracystring",
                column: "string");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "piracystring_string",
                table: "piracystring");

            migrationBuilder.CreateIndex(
                name: "piracystring_string",
                table: "piracystring",
                column: "string",
                unique: true);
        }
    }
}
