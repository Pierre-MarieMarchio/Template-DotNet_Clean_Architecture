using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
//#if (externalAuth)
using AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Rows.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.ExternalIdentity.Rows.Models;
//#endif
//#if (files)
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.Files.Rows.Models;
//#endif
//#if (notes)
using AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.Notes.Rows.Models;
//#endif
//#if (reminders)
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Rows.Models;
//#endif
//#if (todoLists)
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Models;
//#endif
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Common.Contexts;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    //#if (todoLists)
    public const string TodoSchema = "todo";

    //#endif
    //#if (reminders)
    public const string RemindersSchema = "reminders";

    //#endif
    //#if (files)
    public const string FilesSchema = "files";

    //#endif
    //#if (notes)
    public const string NotesSchema = "notes";

    //#endif
    //#if (externalAuth)
    public const string ExternalIdentitySchema = "external_identity";

    //#endif
    public const string PlatformSchema = "platform";

    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public const string MigrationsHistorySchema = "public";

    //#if (todoLists)
    internal DbSet<TodoListRecord> TodoLists => Set<TodoListRecord>();

    //#endif
    //#if (reminders)
    internal DbSet<ReminderRecord> Reminders => Set<ReminderRecord>();

    //#endif
    //#if (files)
    internal DbSet<StoredFileRecord> StoredFiles => Set<StoredFileRecord>();

    internal DbSet<ContentSweepCheckpointRecord> ContentSweepCheckpoints => Set<ContentSweepCheckpointRecord>();

    //#endif
    //#if (notes)
    internal DbSet<NoteRecord> Notes => Set<NoteRecord>();

    //#endif
    //#if (externalAuth)
    internal DbSet<ExternalIdentityRecord> ExternalIdentities => Set<ExternalIdentityRecord>();

    internal DbSet<RoleAssignmentRecord> RoleAssignments => Set<RoleAssignmentRecord>();

    //#endif
    internal DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        //#if (todoLists)
        modelBuilder.ApplyConfiguration(new TodoListRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TodoItemRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TodoItemTagRecordConfiguration());

        //#endif
        //#if (reminders)
        modelBuilder.ApplyConfiguration(new ReminderRecordConfiguration());

        //#endif
        //#if (files)
        modelBuilder.ApplyConfiguration(new StoredFileRecordConfiguration());
        modelBuilder.ApplyConfiguration(new StoredFileTagRecordConfiguration());
        modelBuilder.ApplyConfiguration(new ContentSweepCheckpointRecordConfiguration());

        //#endif
        //#if (notes)
        modelBuilder.ApplyConfiguration(new NoteRecordConfiguration());

        //#endif
        //#if (externalAuth)
        modelBuilder.ApplyConfiguration(new ExternalIdentityRecordConfiguration());
        modelBuilder.ApplyConfiguration(new RoleAssignmentRecordConfiguration());
        //#endif
        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
    }
}
