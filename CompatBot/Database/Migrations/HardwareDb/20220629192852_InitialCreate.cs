using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompatBot.Database.Migrations.HardwareDbMigrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hw_info",
                columns: table => new
                {
                    install_id = table.Column<byte[]>(type: "BLOB", maxLength: 64, nullable: false),
                    timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    cpu_maker = table.Column<string>(type: "TEXT", nullable: false),
                    cpu_model = table.Column<string>(type: "TEXT", nullable: false),
                    thread_count = table.Column<int>(type: "INTEGER", nullable: false),
                    cpu_features = table.Column<int>(type: "INTEGER", nullable: false),
                    ram_in_mb = table.Column<long>(type: "INTEGER", nullable: false),
                    gpu_maker = table.Column<string>(type: "TEXT", nullable: false),
                    gpu_model = table.Column<string>(type: "TEXT", nullable: false),
                    os_type = table.Column<byte>(type: "INTEGER", nullable: false),
                    os_name = table.Column<string>(type: "TEXT", nullable: true),
                    os_version = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hw_info", x => x.install_id);
                });

            migrationBuilder.CreateIndex(
                name: "hardware_timestamp",
                table: "hw_info",
                column: "timestamp");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hw_info");
        }
    }
}
