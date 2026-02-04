using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAuditQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_Action_CreatedAtUtc",
                table: "AdminAuditLogs",
                columns: new[] { "Action", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_Actor_CreatedAtUtc",
                table: "AdminAuditLogs",
                columns: new[] { "Actor", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLogs_RemoteIp_CreatedAtUtc",
                table: "AdminAuditLogs",
                columns: new[] { "RemoteIp", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminAuditLogs_Action_CreatedAtUtc",
                table: "AdminAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AdminAuditLogs_Actor_CreatedAtUtc",
                table: "AdminAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AdminAuditLogs_RemoteIp_CreatedAtUtc",
                table: "AdminAuditLogs");
        }
    }
}
