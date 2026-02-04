using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServerRouteConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MainJarPath",
                table: "Servers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "minecraft_main.jar");

            migrationBuilder.AddColumn<string>(
                name: "RuJarPath",
                table: "Servers",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "minecraft_ru.jar");

            migrationBuilder.AddColumn<string>(
                name: "RuProxyAddress",
                table: "Servers",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RuProxyPort",
                table: "Servers",
                type: "integer",
                nullable: false,
                defaultValue: 25565);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MainJarPath",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "RuJarPath",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "RuProxyAddress",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "RuProxyPort",
                table: "Servers");
        }
    }
}
