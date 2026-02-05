using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStorageModeSwitching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocalRootPath",
                table: "S3StorageConfigs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: false,
                defaultValue: "Storage");

            migrationBuilder.AddColumn<bool>(
                name: "UseS3",
                table: "S3StorageConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocalRootPath",
                table: "S3StorageConfigs");

            migrationBuilder.DropColumn(
                name: "UseS3",
                table: "S3StorageConfigs");
        }
    }
}
