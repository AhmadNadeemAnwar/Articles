using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Articles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Subscribers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "NotifiedAt",
                table: "posts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ConfirmedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ConfirmationSentAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastNotifiedPostId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscribers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscribers_Email",
                table: "subscribers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscribers_Status",
                table: "subscribers",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscribers");

            migrationBuilder.DropColumn(
                name: "NotifiedAt",
                table: "posts");
        }
    }
}
