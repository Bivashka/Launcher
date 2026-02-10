using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthAccountSessionVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SessionVersion",
                table: "AuthAccounts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "AuthAccounts");
        }
    }
}
