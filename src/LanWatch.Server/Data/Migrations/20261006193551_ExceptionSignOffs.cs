using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LanWatch.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExceptionSignOffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExceptionSignOffs",
                columns: table => new
                {
                    Kind = table.Column<byte>(type: "INTEGER", nullable: false),
                    Domain = table.Column<string>(type: "TEXT", nullable: false),
                    SignedOffUnix = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExceptionSignOffs", x => new { x.Kind, x.Domain });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExceptionSignOffs");
        }
    }
}
