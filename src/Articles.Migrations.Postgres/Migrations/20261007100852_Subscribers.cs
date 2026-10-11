using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Articles.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Subscribers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NotifiedAt",
                table: "posts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subscribers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmationSentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastNotifiedPostId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
