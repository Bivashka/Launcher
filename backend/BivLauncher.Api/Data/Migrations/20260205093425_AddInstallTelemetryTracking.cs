using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstallTelemetryTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstallTelemetryConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallTelemetryConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectInstallStats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProjectName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastLauncherVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SeenCount = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectInstallStats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstallTelemetryConfigs_UpdatedAtUtc",
                table: "InstallTelemetryConfigs",
                column: "UpdatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInstallStats_LastSeenAtUtc",
                table: "ProjectInstallStats",
                column: "LastSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectInstallStats_ProjectKey",
                table: "ProjectInstallStats",
                column: "ProjectKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstallTelemetryConfigs");

            migrationBuilder.DropTable(
                name: "ProjectInstallStats");
        }
    }
}
