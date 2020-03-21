using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Migrations
{
    public partial class RemoveSyscallModules : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //disable constraints
            migrationBuilder.Sql("PRAGMA foreign_keys=off;", true);

            //drop old indices
            migrationBuilder.DropIndex(
                name: "syscall_info_module",
                table: "syscall_info");

            migrationBuilder.DropIndex(
                name: "syscall_info_function",
                table: "syscall_info");

            //create new table
            migrationBuilder.CreateTable(
                name: "syscall_info_tmp",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                            .Annotation("Sqlite:Autoincrement", true),
                    function = table.Column<string>(nullable: false)
                },
                constraints: table =>
                             {
                                 table.PrimaryKey("id", x => x.id);
                             });

            //copy data to the new table
            migrationBuilder.Sql("INSERT INTO syscall_info_tmp(id, function) SELECT id, function FROM syscall_info;");

            //drop old table
            migrationBuilder.DropTable("syscall_info");

            //rename new table to the old table
            migrationBuilder.RenameTable(
                name: "syscall_info_tmp",
                newName: "syscall_info");

            //re-create index
            migrationBuilder.CreateIndex(
                name: "syscall_info_function",
                table: "syscall_info",
                column: "function");

            //re-enable constraints
            migrationBuilder.Sql("PRAGMA foreign_keys=on;", true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "module",
                table: "syscall_info",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "syscall_info_module",
                table: "syscall_info",
                column: "module");
        }
    }
}
