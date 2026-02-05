using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactorSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorEnrolledAtUtc",
                table: "AuthAccounts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TwoFactorRequired",
                table: "AuthAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TwoFactorSecret",
                table: "AuthAccounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "TwoFactorConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TwoFactorConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TwoFactorConfigs_UpdatedAtUtc",
                table: "TwoFactorConfigs",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TwoFactorConfigs");

            migrationBuilder.DropColumn(
                name: "TwoFactorEnrolledAtUtc",
                table: "AuthAccounts");

            migrationBuilder.DropColumn(
                name: "TwoFactorRequired",
                table: "AuthAccounts");

            migrationBuilder.DropColumn(
                name: "TwoFactorSecret",
                table: "AuthAccounts");
        }
    }
}
