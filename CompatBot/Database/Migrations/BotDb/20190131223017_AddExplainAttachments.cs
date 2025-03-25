using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Database.Migrations
{
    public partial class AddExplainAttachments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "attachment",
                table: "explanation",
                maxLength: 7340032,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "attachment_filename",
                table: "explanation",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachment",
                table: "explanation");

            migrationBuilder.DropColumn(
                name: "attachment_filename",
                table: "explanation");
        }
    }
}
