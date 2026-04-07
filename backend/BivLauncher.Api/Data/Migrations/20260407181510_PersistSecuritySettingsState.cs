using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistSecuritySettingsState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecuritySettingsStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    MaxConcurrentGameAccountsPerDevice = table.Column<int>(type: "integer", nullable: false),
                    LauncherAdminUsernamesJson = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    SiteCosmeticsUploadSecret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    GameSessionHeartbeatIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    GameSessionExpirationSeconds = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecuritySettingsStates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecuritySettingsStates");
        }
    }
}
