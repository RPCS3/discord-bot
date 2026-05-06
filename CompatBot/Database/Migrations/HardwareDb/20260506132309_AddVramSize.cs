using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompatBot.Database.Migrations.HardwareDbMigrations
{
    /// <inheritdoc />
    public partial class AddVramSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "vram_in_mb",
                table: "hw_info",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "vram_in_mb",
                table: "hw_info");
        }
    }
}
