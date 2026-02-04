using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscordRpcConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiscordRpcConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    AppId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DetailsText = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    StateText = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LargeImageKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LargeImageText = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SmallImageKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SmallImageText = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscordRpcConfigs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscordRpcConfigs_ScopeType_ScopeId",
                table: "DiscordRpcConfigs",
                columns: new[] { "ScopeType", "ScopeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscordRpcConfigs");
        }
    }
}
