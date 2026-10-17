using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Articles.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ReaderFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reader_feedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Liked = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reader_feedback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reader_feedback_posts_PostId",
                        column: x => x.PostId,
                        principalTable: "posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reader_feedback_CreatedAt",
                table: "reader_feedback",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_reader_feedback_PostId",
                table: "reader_feedback",
                column: "PostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reader_feedback");
        }
    }
}
