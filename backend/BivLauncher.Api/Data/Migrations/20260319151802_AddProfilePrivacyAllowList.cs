using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePrivacyAllowList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedPlayerUsernames",
                table: "Profiles",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsPrivate",
                table: "Profiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedPlayerUsernames",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "IsPrivate",
                table: "Profiles");
        }
    }
}
