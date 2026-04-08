using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistDeliverySettingsState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeliverySettingsStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    PublicBaseUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    AssetBaseUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    FallbackApiBaseUrlsJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    LauncherApiBaseUrlRu = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    LauncherApiBaseUrlEu = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PublicBaseUrlRu = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    PublicBaseUrlEu = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    AssetBaseUrlRu = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    AssetBaseUrlEu = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    FallbackApiBaseUrlsRuJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    FallbackApiBaseUrlsEuJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliverySettingsStates", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeliverySettingsStates");
        }
    }
}
