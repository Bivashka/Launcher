using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BivLauncher.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentationArticles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentationArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    BodyMarkdown = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Published = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentationArticles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationArticles_Category_Order",
                table: "DocumentationArticles",
                columns: new[] { "Category", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationArticles_Published_UpdatedAtUtc",
                table: "DocumentationArticles",
                columns: new[] { "Published", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentationArticles_Slug",
                table: "DocumentationArticles",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentationArticles");
        }
    }
}
