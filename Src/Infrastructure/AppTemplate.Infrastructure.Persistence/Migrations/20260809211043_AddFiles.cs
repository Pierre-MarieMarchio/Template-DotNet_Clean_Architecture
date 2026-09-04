using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTemplate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "files");

            migrationBuilder.CreateTable(
                name: "StoredFiles",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DeclaredMediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SizeInBytes = table.Column<long>(type: "bigint", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_ObjectKey",
                schema: "files",
                table: "StoredFiles",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_OwnerId_AvailableAt_Id",
                schema: "files",
                table: "StoredFiles",
                columns: new[] { "OwnerId", "AvailableAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_OwnerId_Name_Id",
                schema: "files",
                table: "StoredFiles",
                columns: new[] { "OwnerId", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_OwnerId_RegisteredAt_Id",
                schema: "files",
                table: "StoredFiles",
                columns: new[] { "OwnerId", "RegisteredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_State_RegisteredAt",
                schema: "files",
                table: "StoredFiles",
                columns: new[] { "State", "RegisteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StoredFiles",
                schema: "files");
        }
    }
}
