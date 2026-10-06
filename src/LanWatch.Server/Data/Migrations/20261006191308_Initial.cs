using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LanWatch.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Ip = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    FirstSeenUnix = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSeenUnix = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Ip);
                });

            migrationBuilder.CreateTable(
                name: "Downloads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClientIp = table.Column<string>(type: "TEXT", nullable: false),
                    Service = table.Column<string>(type: "TEXT", nullable: false),
                    ContentId = table.Column<string>(type: "TEXT", nullable: false),
                    StartUnix = table.Column<long>(type: "INTEGER", nullable: false),
                    LastUnix = table.Column<long>(type: "INTEGER", nullable: false),
                    HitBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MissBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Requests = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Downloads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Errors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Unix = table.Column<long>(type: "INTEGER", nullable: false),
                    Source = table.Column<byte>(type: "INTEGER", nullable: false),
                    Kind = table.Column<byte>(type: "INTEGER", nullable: false),
                    ClientIp = table.Column<string>(type: "TEXT", nullable: true),
                    Host = table.Column<string>(type: "TEXT", nullable: true),
                    Upstream = table.Column<string>(type: "TEXT", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Errors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    StartUnix = table.Column<long>(type: "INTEGER", nullable: false),
                    EndUnix = table.Column<long>(type: "INTEGER", nullable: false),
                    IsAuto = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IngestCursors",
                columns: table => new
                {
                    File = table.Column<string>(type: "TEXT", nullable: false),
                    Offset = table.Column<long>(type: "INTEGER", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedUnix = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IngestCursors", x => x.File);
                });

            migrationBuilder.CreateTable(
                name: "StatusMinutes",
                columns: table => new
                {
                    Minute = table.Column<long>(type: "INTEGER", nullable: false),
                    Service = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Count = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusMinutes", x => new { x.Minute, x.Service, x.Status });
                });

            migrationBuilder.CreateTable(
                name: "StreamMinutes",
                columns: table => new
                {
                    Minute = table.Column<long>(type: "INTEGER", nullable: false),
                    ClientIp = table.Column<string>(type: "TEXT", nullable: false),
                    SniHost = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Connections = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesSent = table.Column<long>(type: "INTEGER", nullable: false),
                    BytesReceived = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamMinutes", x => new { x.Minute, x.ClientIp, x.SniHost, x.Status });
                });

            migrationBuilder.CreateTable(
                name: "TrafficMinutes",
                columns: table => new
                {
                    Minute = table.Column<long>(type: "INTEGER", nullable: false),
                    ClientIp = table.Column<string>(type: "TEXT", nullable: false),
                    Service = table.Column<string>(type: "TEXT", nullable: false),
                    HitBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MissBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    HitRequests = table.Column<long>(type: "INTEGER", nullable: false),
                    MissRequests = table.Column<long>(type: "INTEGER", nullable: false),
                    ErrorRequests = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrafficMinutes", x => new { x.Minute, x.ClientIp, x.Service });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_ClientIp_Service_ContentId_StartUnix",
                table: "Downloads",
                columns: new[] { "ClientIp", "Service", "ContentId", "StartUnix" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_LastUnix",
                table: "Downloads",
                column: "LastUnix");

            migrationBuilder.CreateIndex(
                name: "IX_Downloads_Service_ContentId",
                table: "Downloads",
                columns: new[] { "Service", "ContentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Errors_Kind_Unix",
                table: "Errors",
                columns: new[] { "Kind", "Unix" });

            migrationBuilder.CreateIndex(
                name: "IX_Errors_Unix",
                table: "Errors",
                column: "Unix");

            migrationBuilder.CreateIndex(
                name: "IX_Events_StartUnix",
                table: "Events",
                column: "StartUnix");

            migrationBuilder.CreateIndex(
                name: "IX_TrafficMinutes_ClientIp_Minute",
                table: "TrafficMinutes",
                columns: new[] { "ClientIp", "Minute" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Downloads");

            migrationBuilder.DropTable(
                name: "Errors");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "IngestCursors");

            migrationBuilder.DropTable(
                name: "StatusMinutes");

            migrationBuilder.DropTable(
                name: "StreamMinutes");

            migrationBuilder.DropTable(
                name: "TrafficMinutes");
        }
    }
}
