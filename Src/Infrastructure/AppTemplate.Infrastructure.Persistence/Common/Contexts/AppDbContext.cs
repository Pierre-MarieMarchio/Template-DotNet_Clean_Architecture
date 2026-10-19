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

/// <summary>The business half's <see cref="DbContext"/>: the domain's persistence models and the
/// tables no feature owns, in one model with one migrations history; authentication stores through a
/// context of its own on the same connection (docs/ARCHITECTURE.md argues why two rather than one).
/// <b>This class is the model's composition root</b> — the one type in <c>Common/</c> allowed to name
/// a feature, as <c>Program.cs</c> is the one place allowed to name every module. A context instance
/// is a unit of work, scoped to one request; staged changes are committed by exactly one
/// <c>SaveChangesAsync</c>, made by <see cref="Application.Core.Common.Ports.IUnitOfWork"/> on
/// behalf of a use case — repositories and stores borrow the context and never commit. It takes no
/// <c>ICurrentUser</c>, which would make every instance depend on an HTTP request, and it does not
/// override <c>SaveChangesAsync</c> to stamp, flush or dispatch — that belongs in
/// <see cref="Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor"/> implementations,
/// which also apply to the synchronous overload an override would miss.</summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    //#if (todoLists)
    /// <summary>The schema the to-do list feature's tables live in.</summary>
    public const string TodoSchema = "todo";

    //#endif
    //#if (reminders)
    /// <summary>The schema the reminders feature's tables live in.</summary>
    public const string RemindersSchema = "reminders";

    //#endif
    //#if (files)
    /// <summary>
    /// The schema the file feature's tables live in. Its own, like every other feature's, rather than
    /// <see cref="PlatformSchema"/>: the table belongs to a feature, and a feature that owns a schema
    /// is one whose removal is a deleted migration file rather than a drop.
    /// </summary>
    public const string FilesSchema = "files";

    //#endif
    //#if (notes)
    /// <summary>The schema the notes feature's table lives in.</summary>
    public const string NotesSchema = "notes";

    //#endif
    //#if (externalAuth)
    /// <summary>
    /// The schema of the mapping from an external identity provider's subjects to internal owners,
    /// and of the role assignments read beside it. Present only where callers are authenticated by
    /// such a provider; local accounts keep theirs in the identity context's own schema.
    /// </summary>
    public const string ExternalIdentitySchema = "external_identity";

    //#endif
    /// <summary>
    /// The schema for tables that are cross-cutting rather than owned by a feature — the idempotency
    /// key store is the first of these. A feature's schema would not be honest: the table belongs to
    /// no feature.
    /// </summary>
    public const string PlatformSchema = "platform";

    /// <summary>This context's history table, left in the connection's default schema because it
    /// belongs to no feature. Naming it here and in the design-time factory keeps the tool and the
    /// runtime from disagreeing about where applied migrations are recorded.</summary>
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    /// <summary>The schema the migrations history table lives in.</summary>
    public const string MigrationsHistorySchema = "public";

    //#if (todoLists)
    /// <summary>The to-do list aggregate's root table — a <em>persistence model</em>, not the domain
    /// aggregate, which <see cref="Features.TodoLists.Rows.Mapping.ITodoListMapper"/> maps onto it and EF
    /// never tracks. Internal: nothing outside this assembly has any business naming a storage
    /// shape.</summary>
    internal DbSet<TodoListRecord> TodoLists => Set<TodoListRecord>();

    //#endif
    //#if (reminders)
    /// <summary>
    /// The reminder aggregate's table. Also a persistence model rather than the domain aggregate,
    /// mapped by <see cref="Features.Reminders.Rows.Mapping.IReminderMapper"/> and never tracked by EF.
    /// </summary>
    internal DbSet<ReminderRecord> Reminders => Set<ReminderRecord>();

    //#endif
    //#if (files)
    /// <summary>The stored-file aggregate's table: everything about a file except its content,
    /// which never passes through this application. Also a persistence model, mapped by
    /// <see cref="Features.Files.Rows.Mapping.IStoredFileMapper"/> and never tracked by EF.</summary>
    internal DbSet<StoredFileRecord> StoredFiles => Set<StoredFileRecord>();

    /// <summary>Where the orphan sweep's walk of the object store has reached.</summary>
    internal DbSet<ContentSweepCheckpointRecord> ContentSweepCheckpoints => Set<ContentSweepCheckpointRecord>();

    //#endif
    //#if (notes)
    /// <summary>
    /// The notes table. Unlike an aggregate's table it is the whole model of its feature: no aggregate is
    /// mapped onto it, and the store reads and writes the row directly. Internal for the same reason.
    /// </summary>
    internal DbSet<NoteRecord> Notes => Set<NoteRecord>();

    //#endif
    //#if (externalAuth)
    /// <summary>
    /// Which internal owner each external provider subject stands for. Read and written only through
    /// <see cref="Application.Features.ExternalIdentity.Ports.ExternalIdentityStore.IExternalIdentityStore"/>
    /// and the queries beside it.
    /// </summary>
    internal DbSet<ExternalIdentityRecord> ExternalIdentities => Set<ExternalIdentityRecord>();

    /// <summary>The roles held by owners, for a deployment that does not take them from the provider.</summary>
    internal DbSet<RoleAssignmentRecord> RoleAssignments => Set<RoleAssignmentRecord>();

    //#endif
    /// <summary>Claimed idempotency keys. Internal like every other row type here: the rules for
    /// claiming, completing and releasing one live in
    /// <see cref="Common.Idempotency.IdempotencyStore"/>, reached only through
    /// <see cref="Application.Core.Common.Idempotency.IIdempotencyStore"/>.</summary>
    internal DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // Named one by one rather than discovered by scanning the assembly: lines the
        // compiler checks, instead of a reflection call that silently maps nothing when a
        // configuration class is renamed or moved. No default schema is set — every table names its
        // own, so a table cannot drift into the wrong one by omission.
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
