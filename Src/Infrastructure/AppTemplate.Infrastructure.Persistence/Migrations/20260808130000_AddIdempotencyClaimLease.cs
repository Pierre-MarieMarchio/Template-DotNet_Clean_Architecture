using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTemplate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdempotencyClaimLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfilled to "now" rather than left null: any row already sitting in IsCompleted ==
            // false when this ships is, by definition, older than this column ever existed to protect
            // it, so it becomes immediately reclaimable instead of blocking a retry for a lease it
            // never actually held.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimedUntil",
                schema: "platform",
                table: "IdempotencyKeys",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClaimedUntil",
                schema: "platform",
                table: "IdempotencyKeys");
        }
    }
}
