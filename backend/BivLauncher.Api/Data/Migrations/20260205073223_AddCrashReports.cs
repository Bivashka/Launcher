using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrashReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrashReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CrashId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ProfileSlug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ServerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RouteCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LauncherVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OsVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    JavaVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ErrorType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LogExcerpt = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    MetadataJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrashReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrashReports_CrashId",
                table: "CrashReports",
                column: "CrashId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrashReports_CreatedAtUtc",
                table: "CrashReports",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CrashReports_ProfileSlug",
                table: "CrashReports",
                column: "ProfileSlug");

            migrationBuilder.CreateIndex(
                name: "IX_CrashReports_Status_CreatedAtUtc",
                table: "CrashReports",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrashReports");
        }
    }
}
