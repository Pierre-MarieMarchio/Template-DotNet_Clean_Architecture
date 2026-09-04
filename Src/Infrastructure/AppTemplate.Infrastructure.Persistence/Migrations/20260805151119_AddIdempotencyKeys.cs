using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTemplate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "IdempotencyKeys",
                schema: "platform",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Fingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseBody = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    Location = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKeys", x => new { x.UserId, x.Key });
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_ExpiresAt",
                schema: "platform",
                table: "IdempotencyKeys",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyKeys",
                schema: "platform");
        }
    }
}
