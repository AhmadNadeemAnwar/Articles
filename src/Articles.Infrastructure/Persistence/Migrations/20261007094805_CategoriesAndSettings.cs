using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Articles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CategoriesAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "posts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "site_settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_settings", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_posts_CategoryId",
                table: "posts",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_categories_Slug",
                table: "categories",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_posts_categories_CategoryId",
                table: "posts",
                column: "CategoryId",
                principalTable: "categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_posts_categories_CategoryId",
                table: "posts");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "site_settings");

            migrationBuilder.DropIndex(
                name: "IX_posts_CategoryId",
                table: "posts");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "posts");
        }
    }
}
