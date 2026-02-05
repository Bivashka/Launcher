using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWizardPreflightRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WizardPreflightRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PassedCount = table.Column<int>(type: "integer", nullable: false),
                    TotalCount = table.Column<int>(type: "integer", nullable: false),
                    ChecksJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    RanAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WizardPreflightRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WizardPreflightRuns_Actor_RanAtUtc",
                table: "WizardPreflightRuns",
                columns: new[] { "Actor", "RanAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WizardPreflightRuns_RanAtUtc",
                table: "WizardPreflightRuns",
                column: "RanAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WizardPreflightRuns");
        }
    }
}
