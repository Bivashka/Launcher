using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileBundledRuntimeMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BundledRuntimeContentType",
                table: "Profiles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BundledRuntimeSha256",
                table: "Profiles",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "BundledRuntimeSizeBytes",
                table: "Profiles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BundledRuntimeContentType",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "BundledRuntimeSha256",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "BundledRuntimeSizeBytes",
                table: "Profiles");
        }
    }
}
