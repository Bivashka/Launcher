using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceUserNameTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceUserName",
                table: "HardwareBans",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DeviceUserName",
                table: "AuthAccounts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_HardwareBans_DeviceUserName",
                table: "HardwareBans",
                column: "DeviceUserName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HardwareBans_DeviceUserName",
                table: "HardwareBans");

            migrationBuilder.DropColumn(
                name: "DeviceUserName",
                table: "HardwareBans");

            migrationBuilder.DropColumn(
                name: "DeviceUserName",
                table: "AuthAccounts");
        }
    }
}
