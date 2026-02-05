using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNewsSourceRateLimitAndCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CacheEtag",
                table: "NewsSourceConfigs",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CacheLastModified",
                table: "NewsSourceConfigs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastContentChangeAtUtc",
                table: "NewsSourceConfigs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFetchAttemptAtUtc",
                table: "NewsSourceConfigs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinFetchIntervalMinutes",
                table: "NewsSourceConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheEtag",
                table: "NewsSourceConfigs");

            migrationBuilder.DropColumn(
                name: "CacheLastModified",
                table: "NewsSourceConfigs");

            migrationBuilder.DropColumn(
                name: "LastContentChangeAtUtc",
                table: "NewsSourceConfigs");

            migrationBuilder.DropColumn(
                name: "LastFetchAttemptAtUtc",
                table: "NewsSourceConfigs");

            migrationBuilder.DropColumn(
                name: "MinFetchIntervalMinutes",
                table: "NewsSourceConfigs");
        }
    }
}
