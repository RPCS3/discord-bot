using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Migrations
{
    public partial class CompatFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "compatibility_change_date",
                table: "thumbnail",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "compatibility_status",
                table: "thumbnail",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "compatibility_change_date",
                table: "thumbnail");

            migrationBuilder.DropColumn(
                name: "compatibility_status",
                table: "thumbnail");
        }
    }
}
