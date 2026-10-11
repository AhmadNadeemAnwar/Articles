using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Articles.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class VisitorReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "claps");

            // The old total counted clicks from signed-in accounts (up to 50 each). From here on it
            // counts readers, one each, so the old numbers would be misleading next to the new ones.
            migrationBuilder.Sql("UPDATE posts SET \"ClapCount\" = 0;");

            migrationBuilder.AddColumn<int>(
                name: "InsightfulCount",
                table: "posts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "post_views",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitorKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_post_views", x => new { x.PostId, x.VisitorKey, x.Day });
                    table.ForeignKey(
                        name: "FK_post_views_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reactions",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitorKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reactions", x => new { x.PostId, x.VisitorKey, x.Kind });
                    table.ForeignKey(
                        name: "FK_reactions_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_post_views_Day",
                table: "post_views",
                column: "Day");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "post_views");

            migrationBuilder.DropTable(
                name: "reactions");

            migrationBuilder.DropColumn(
                name: "InsightfulCount",
                table: "posts");

            migrationBuilder.CreateTable(
                name: "claps",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_claps", x => new { x.PostId, x.UserId });
                    table.ForeignKey(
                        name: "FK_claps_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_claps_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_claps_UserId",
                table: "claps",
                column: "UserId");
        }
    }
}
