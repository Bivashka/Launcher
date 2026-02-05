using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthProviderModeAndFieldMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthMode",
                table: "AuthProviderConfigs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "external");

            migrationBuilder.AddColumn<string>(
                name: "LoginFieldKey",
                table: "AuthProviderConfigs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "username");

            migrationBuilder.AddColumn<string>(
                name: "PasswordFieldKey",
                table: "AuthProviderConfigs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "password");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthMode",
                table: "AuthProviderConfigs");

            migrationBuilder.DropColumn(
                name: "LoginFieldKey",
                table: "AuthProviderConfigs");

            migrationBuilder.DropColumn(
                name: "PasswordFieldKey",
                table: "AuthProviderConfigs");
        }
    }
}
