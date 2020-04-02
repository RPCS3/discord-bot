using Microsoft.EntityFrameworkCore.Migrations;

namespace CompatBot.Migrations
{
    public partial class Metacritic : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.CreateTable(
                name: "metacritic",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    title = table.Column<string>(nullable: false),
                    critic_score = table.Column<byte>(nullable: true),
                    user_score = table.Column<byte>(nullable: true),
                    notes = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("id", x => x.id);
                });


            //disable constraints
            migrationBuilder.Sql("PRAGMA foreign_keys=off;", true);

            //drop old indices
            migrationBuilder.DropIndex(
                name: "thumbnail_product_code",
                table: "thumbnail");

            migrationBuilder.DropIndex(
                name: "thumbnail_timestamp",
                table: "thumbnail");

            migrationBuilder.DropIndex(
                name: "thumbnail_content_id",
                table: "thumbnail");

            //create new table
            migrationBuilder.CreateTable(
                name: "thumbnail_new",
                columns: table => new
                {
                    id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    product_code = table.Column<string>(nullable: false),
                    content_id = table.Column<string>(nullable: true),
                    name = table.Column<string>(nullable: true),
                    url = table.Column<string>(nullable: true),
                    embeddable_url = table.Column<string>(nullable: true),
                    timestamp = table.Column<long>(nullable: false),
                    embed_color = table.Column<int>(nullable: true),
                    compatibility_status = table.Column<byte>(nullable: true),
                    compatibility_change_date = table.Column<long>(nullable: true),
                    metacritic_id = table.Column<int>(nullable: true)
                },
                constraints: table =>
                             {
                                 table.PrimaryKey("id", x => x.id);
                                 table.ForeignKey(
                                     name: "fk_thumbnail_metacritic_metacritic_id",
                                     column: x => x.metacritic_id,
                                     principalTable: "metacritic",
                                     principalColumn: "id",
                                     onDelete: ReferentialAction.Restrict);
                             });

            //copy data to the new table
            migrationBuilder.Sql("INSERT INTO thumbnail_new(id, product_code, content_id, name, url, embeddable_url, timestamp, embed_color, compatibility_status, compatibility_change_date) " +
                                 "SELECT id, product_code, content_id, name, url, embeddable_url, timestamp, embed_color, compatibility_status, compatibility_change_date FROM thumbnail;");

            //drop old table
            migrationBuilder.DropTable("thumbnail");

            //rename new table to the old table
            migrationBuilder.RenameTable(
                name: "thumbnail_new",
                newName: "thumbnail");

            //re-create index
            migrationBuilder.CreateIndex(
                name: "thumbnail_product_code",
                table: "thumbnail",
                column: "product_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "thumbnail_timestamp",
                table: "thumbnail",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "thumbnail_content_id",
                table: "thumbnail",
                column: "content_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_thumbnail_metacritic_id",
                table: "thumbnail",
                column: "metacritic_id");

            //re-enable constraints
            migrationBuilder.Sql("PRAGMA foreign_keys=on;", true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_thumbnail_metacritic_metacritic_id",
                table: "thumbnail");

            migrationBuilder.DropTable(
                name: "metacritic");

            migrationBuilder.DropIndex(
                name: "ix_thumbnail_metacritic_id",
                table: "thumbnail");

            migrationBuilder.DropColumn(
                name: "metacritic_id",
                table: "thumbnail");
        }
    }
}
