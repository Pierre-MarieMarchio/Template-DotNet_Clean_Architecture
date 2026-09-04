using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTemplate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTodoListSortIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TodoLists_OwnerId",
                schema: "todo",
                table: "TodoLists");

            migrationBuilder.CreateIndex(
                name: "IX_TodoLists_OwnerId_CreatedAt_Id",
                schema: "todo",
                table: "TodoLists",
                columns: new[] { "OwnerId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TodoLists_OwnerId_LastModifiedAt_Id",
                schema: "todo",
                table: "TodoLists",
                columns: new[] { "OwnerId", "LastModifiedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_TodoLists_OwnerId_Name_Id",
                schema: "todo",
                table: "TodoLists",
                columns: new[] { "OwnerId", "Name", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TodoLists_OwnerId_CreatedAt_Id",
                schema: "todo",
                table: "TodoLists");

            migrationBuilder.DropIndex(
                name: "IX_TodoLists_OwnerId_LastModifiedAt_Id",
                schema: "todo",
                table: "TodoLists");

            migrationBuilder.DropIndex(
                name: "IX_TodoLists_OwnerId_Name_Id",
                schema: "todo",
                table: "TodoLists");

            migrationBuilder.CreateIndex(
                name: "IX_TodoLists_OwnerId",
                schema: "todo",
                table: "TodoLists",
                column: "OwnerId");
        }
    }
}
