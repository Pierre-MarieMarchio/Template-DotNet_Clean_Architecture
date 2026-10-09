using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppTemplate.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //#if (files)
            migrationBuilder.EnsureSchema(
                name: "files");

            //#endif
            migrationBuilder.EnsureSchema(
                name: "platform");

            //#if (notes)
            migrationBuilder.EnsureSchema(
                name: "notes");

            //#endif
            //#if (reminders)
            migrationBuilder.EnsureSchema(
                name: "reminders");

            //#endif
            //#if (todoLists)
            migrationBuilder.EnsureSchema(
                name: "todo");

            //#endif
            //#if (files)
            migrationBuilder.CreateTable(
                name: "ContentSweepCheckpoints",
                schema: "files",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentSweepCheckpoints", x => x.Name);
                });

            //#endif
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
                    ClaimedUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseBody = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: true),
                    Location = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ETag = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyKeys", x => new { x.UserId, x.Key });
                });

            //#if (notes)
            migrationBuilder.CreateTable(
                name: "Notes",
                schema: "notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                });

            //#endif
            //#if (reminders)
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
                    NotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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

            //#endif
            //#if (files)
            migrationBuilder.CreateTable(
                name: "StoredFiles",
                schema: "files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    DeclaredMediaType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    SizeInBytes = table.Column<long>(type: "bigint", nullable: false),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InspectionAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextInspectionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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

            //#endif
            //#if (todoLists)
            migrationBuilder.CreateTable(
                name: "TodoLists",
                schema: "todo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoLists", x => x.Id);
                });

            //#endif
            //#if (files)
            migrationBuilder.CreateTable(
                name: "StoredFileTags",
                schema: "files",
                columns: table => new
                {
                    StoredFileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFileTags", x => new { x.StoredFileId, x.Value });
                    table.ForeignKey(
                        name: "FK_StoredFileTags_StoredFiles_StoredFileId",
                        column: x => x.StoredFileId,
                        principalSchema: "files",
                        principalTable: "StoredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            //#endif
            //#if (todoLists)
            migrationBuilder.CreateTable(
                name: "TodoItems",
                schema: "todo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TodoListId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TodoItems_TodoLists_TodoListId",
                        column: x => x.TodoListId,
                        principalSchema: "todo",
                        principalTable: "TodoLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TodoItemTags",
                schema: "todo",
                columns: table => new
                {
                    TodoItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoItemTags", x => new { x.TodoItemId, x.Value });
                    table.ForeignKey(
                        name: "FK_TodoItemTags_TodoItems_TodoItemId",
                        column: x => x.TodoItemId,
                        principalSchema: "todo",
                        principalTable: "TodoItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            //#endif
            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyKeys_ExpiresAt",
                schema: "platform",
                table: "IdempotencyKeys",
                column: "ExpiresAt");

            //#if (notes)
            migrationBuilder.CreateIndex(
                name: "IX_Notes_OwnerId_CreatedAt_Id",
                schema: "notes",
                table: "Notes",
                columns: new[] { "OwnerId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_OwnerId_Title_Id",
                schema: "notes",
                table: "Notes",
                columns: new[] { "OwnerId", "Title", "Id" });

            //#endif
            //#if (reminders)
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

            //#endif
            //#if (files)
            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_Deposited_NextInspectionAt_RegisteredAt_Id",
                schema: "files",
                table: "StoredFiles",
                columns: new[] { "NextInspectionAt", "RegisteredAt", "Id" },
                filter: "\"State\" = 2");

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
                name: "IX_StoredFiles_OwnerId_Category_State",
                schema: "files",
                table: "StoredFiles",
                columns: new[] { "OwnerId", "Category", "State" })
                .Annotation("Npgsql:IndexInclude", new[] { "SizeInBytes" });

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

            //#endif
            //#if (todoLists)
            migrationBuilder.CreateIndex(
                name: "IX_TodoItems_TodoListId",
                schema: "todo",
                table: "TodoItems",
                column: "TodoListId");

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
            //#endif
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            //#if (files)
            migrationBuilder.DropTable(
                name: "ContentSweepCheckpoints",
                schema: "files");

            //#endif
            migrationBuilder.DropTable(
                name: "IdempotencyKeys",
                schema: "platform");

            //#if (notes)
            migrationBuilder.DropTable(
                name: "Notes",
                schema: "notes");

            //#endif
            //#if (reminders)
            migrationBuilder.DropTable(
                name: "Reminders",
                schema: "reminders");

            //#endif
            //#if (files)
            migrationBuilder.DropTable(
                name: "StoredFileTags",
                schema: "files");

            //#endif
            //#if (todoLists)
            migrationBuilder.DropTable(
                name: "TodoItemTags",
                schema: "todo");

            //#endif
            //#if (files)
            migrationBuilder.DropTable(
                name: "StoredFiles",
                schema: "files");

            //#endif
            //#if (todoLists)
            migrationBuilder.DropTable(
                name: "TodoItems",
                schema: "todo");

            migrationBuilder.DropTable(
                name: "TodoLists",
                schema: "todo");
            //#endif
        }
    }
}
