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

            migrationBuilder.AlterColumn<byte>(
                name: "context",
                table: "piracystring",
                nullable: false,
                defaultValue: (byte)3,
                oldClrType: typeof(byte),
                oldDefaultValue: (byte)3)
                .OldAnnotation("Sqlite:Autoincrement", true);

            migrationBuilder.AlterColumn<int>(
                name: "actions",
                table: "piracystring",
                nullable: false,
                defaultValue: 11,
                oldClrType: typeof(int),
                oldDefaultValue: 11)
                .OldAnnotation("Sqlite:Autoincrement", true);

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

            migrationBuilder.AlterColumn<byte>(
                name: "context",
                table: "piracystring",
                nullable: false,
                defaultValue: (byte)3,
                oldClrType: typeof(byte),
                oldDefaultValue: (byte)3)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.AlterColumn<int>(
                name: "actions",
                table: "piracystring",
                nullable: false,
                defaultValue: 11,
                oldClrType: typeof(int),
                oldDefaultValue: 11)
                .Annotation("Sqlite:Autoincrement", true);

            migrationBuilder.CreateIndex(
                name: "piracystring_string",
                table: "piracystring",
                column: "string",
                unique: true);
        }
    }
}
