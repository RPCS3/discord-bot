using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Database.Migrations
{
    public partial class ContentFilter20 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "actions",
                table: "piracystring",
                nullable: false,
                defaultValue: 11);

            migrationBuilder.AddColumn<byte>(
                name: "context",
                table: "piracystring",
                nullable: false,
                defaultValue: (byte)3);

            migrationBuilder.AddColumn<string>(
                name: "custom_message",
                table: "piracystring",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "disabled",
                table: "piracystring",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "explain_term",
                table: "piracystring",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "validating_regex",
                table: "piracystring",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "actions",
                table: "piracystring");

            migrationBuilder.DropColumn(
                name: "context",
                table: "piracystring");

            migrationBuilder.DropColumn(
                name: "custom_message",
                table: "piracystring");

            migrationBuilder.DropColumn(
                name: "disabled",
                table: "piracystring");

            migrationBuilder.DropColumn(
                name: "explain_term",
                table: "piracystring");

            migrationBuilder.DropColumn(
                name: "validating_regex",
                table: "piracystring");
        }
    }
}
