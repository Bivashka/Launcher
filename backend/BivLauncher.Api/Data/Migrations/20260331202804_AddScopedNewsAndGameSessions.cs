using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScopedNewsAndGameSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScopeId",
                table: "NewsItems",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScopeType",
                table: "NewsItems",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ActiveGameSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    HwidHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeviceUserName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ServerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServerName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveGameSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActiveGameSessions_AuthAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AuthAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_ScopeType_ScopeId_CreatedAtUtc",
                table: "NewsItems",
                columns: new[] { "ScopeType", "ScopeId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveGameSessions_AccountId",
                table: "ActiveGameSessions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveGameSessions_DeviceUserName",
                table: "ActiveGameSessions",
                column: "DeviceUserName");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveGameSessions_ExpiresAtUtc",
                table: "ActiveGameSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ActiveGameSessions_HwidHash",
                table: "ActiveGameSessions",
                column: "HwidHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveGameSessions");

            migrationBuilder.DropIndex(
                name: "IX_NewsItems_ScopeType_ScopeId_CreatedAtUtc",
                table: "NewsItems");

            migrationBuilder.DropColumn(
                name: "ScopeId",
                table: "NewsItems");

            migrationBuilder.DropColumn(
                name: "ScopeType",
                table: "NewsItems");
        }
    }
}
