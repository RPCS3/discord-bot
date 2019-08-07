using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Migrations
{
    public partial class SyscallInfo : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "syscall_info",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    module = table.Column<string>(nullable: false),
                    function = table.Column<string>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "syscall_to_product_map",
                columns: table => new
                {
                    product_id = table.Column<int>(nullable: false),
                    syscall_info_id = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => new { x.product_id, x.syscall_info_id });
                    table.ForeignKey(
                        name: "fk_syscall_to_product_map__thumbnail_product_id",
                        column: x => x.product_id,
                        principalTable: "thumbnail",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_syscall_to_product_map_syscall_info_syscall_info_id",
                        column: x => x.syscall_info_id,
                        principalTable: "syscall_info",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "syscall_info_function",
                table: "syscall_info",
                column: "function");

            migrationBuilder.CreateIndex(
                name: "syscall_info_module",
                table: "syscall_info",
                column: "module");

            migrationBuilder.CreateIndex(
                name: "ix_syscall_to_product_map_syscall_info_id",
                table: "syscall_to_product_map",
                column: "syscall_info_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "syscall_to_product_map");

            migrationBuilder.DropTable(
                name: "syscall_info");
        }
    }
}
