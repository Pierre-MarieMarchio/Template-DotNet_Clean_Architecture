using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTemplate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reminders");

            migrationBuilder.CreateTable(
                name: "Reminders",
                schema: "reminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TodoListId = table.Column<Guid>(type: "uuid", nullable: false),
                    TodoItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reminders", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_State_DueAt",
                schema: "reminders",
                table: "Reminders",
                columns: new[] { "State", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Reminders_TodoItemId",
                schema: "reminders",
                table: "Reminders",
                column: "TodoItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reminders",
                schema: "reminders");
        }
    }
}
