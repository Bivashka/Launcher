using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAuditRequestMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RemoteIp",
                table: "AdminAuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RequestId",
                table: "AdminAuditLogs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UserAgent",
                table: "AdminAuditLogs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_RequestId",
                table: "AdminAuditLogs",
                column: "RequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminAuditLogs_RequestId",
                table: "AdminAuditLogs");

            migrationBuilder.DropColumn(
                name: "RemoteIp",
                table: "AdminAuditLogs");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "AdminAuditLogs");

            migrationBuilder.DropColumn(
                name: "UserAgent",
                table: "AdminAuditLogs");
        }
    }
}
