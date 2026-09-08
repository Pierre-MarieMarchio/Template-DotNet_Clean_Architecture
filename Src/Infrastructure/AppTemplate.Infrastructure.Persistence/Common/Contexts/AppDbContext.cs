using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using AppTemplate.Infrastructure.Persistence.Features.Files.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Models;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Common.Contexts;

/// <summary>
/// The business half's <see cref="DbContext"/>: the domain's persistence models and the tables no
/// feature owns, in one model with one migrations history. Authentication stores through a context
/// of its own on the same connection — why two rather than one, and why one database rather than
/// two, is argued in docs/ARCHITECTURE.md.
/// <para>
/// <b>This class is the model's composition root.</b> It is the one type in <c>Common/</c> allowed
/// to name a feature, exactly as <c>Program.cs</c> is the one place allowed to name every module.
/// The cross-cutting mechanisms beside it — auditing, event dispatch, the unit of work, the flush
/// pipeline — name no feature at all, and an architecture test asserts that.
/// </para>
/// <para>
/// <b>Resource ownership.</b> A context instance is a unit of work. The DI container opens it,
/// scoped to one request, and disposes it when the request ends; nothing else may dispose it.
/// Staged changes are committed by exactly one call to <c>SaveChangesAsync</c>, made by
/// <see cref="Application.Core.Common.Ports.IUnitOfWork"/> on behalf of a use case. Repositories,
/// query classes and stores borrow the context and never commit: ownership of the transaction never
/// transfers to them.
/// </para>
/// <para>
/// Two things it deliberately does not do. It does not take <c>ICurrentUser</c>: that would make
/// every instance depend on an HTTP request, so any use outside one — a background worker, a
/// migration, a seeding routine — would stamp audit columns with an empty caller. And it does not
/// override <c>SaveChangesAsync</c> to stamp, flush or dispatch: that behaviour belongs in
/// <see cref="Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor"/> implementations,
/// which also apply to the synchronous overload an override would miss.
/// </para>
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    /// <summary>The schema the to-do list feature's tables live in.</summary>
    public const string TodoSchema = "todo";

    /// <summary>The schema the reminders feature's tables live in.</summary>
    public const string RemindersSchema = "reminders";

    /// <summary>
    /// The schema the file feature's tables live in. Its own, like every other feature's, rather than
    /// <see cref="PlatformSchema"/>: the table belongs to a feature, and a feature that owns a schema
    /// is one whose removal is a deleted migration file rather than a drop.
    /// </summary>
    public const string FilesSchema = "files";

    /// <summary>
    /// The schema for tables that are cross-cutting rather than owned by a feature — the idempotency
    /// key store is the first of these. <see cref="TodoSchema"/> would not be honest: the table
    /// belongs to no feature.
    /// </summary>
    public const string PlatformSchema = "platform";

    /// <summary>
    /// This context's history table. It is left in the connection's default schema rather than
    /// inside a feature's, because it belongs to none of them; the other context's sits in the
    /// schema its own tables live in. Naming it here and in the design-time factory keeps the tool
    /// and the runtime from disagreeing about where applied migrations are recorded.
    /// </summary>
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    /// <summary>The schema the migrations history table lives in.</summary>
    public const string MigrationsHistorySchema = "public";

    /// <summary>
    /// The to-do list aggregate's root table. This is a <em>persistence model</em>, not the domain
    /// aggregate: the domain type is mapped onto it by
    /// <see cref="Features.TodoLists.Mapping.ITodoListMapper"/> and is never tracked by EF. Internal,
    /// because nothing outside this assembly has any business naming a storage shape.
    /// </summary>
    internal DbSet<TodoListRecord> TodoLists => Set<TodoListRecord>();

    /// <summary>
    /// The reminder aggregate's table. Also a persistence model rather than the domain aggregate,
    /// mapped by <see cref="Features.Reminders.Mapping.IReminderMapper"/> and never tracked by EF.
    /// </summary>
    internal DbSet<ReminderRecord> Reminders => Set<ReminderRecord>();

    /// <summary>
    /// The stored-file aggregate's table: everything about a file except its content, which never
    /// passes through this application at all. Also a persistence model rather than the domain
    /// aggregate, mapped by <see cref="Features.Files.Mapping.IStoredFileMapper"/> and never tracked by
    /// EF.
    /// </summary>
    internal DbSet<StoredFileRecord> StoredFiles => Set<StoredFileRecord>();

    /// <summary>
    /// Claimed idempotency keys. Internal for the same reason as every other row type here: the
    /// rules for claiming, completing and releasing one live in
    /// <see cref="Common.Idempotency.IdempotencyStore"/>, reached only through
    /// <see cref="Application.Core.Common.Idempotency.IIdempotencyStore"/>.
    /// </summary>
    internal DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        // Named one by one rather than discovered by scanning the assembly: seven lines the
        // compiler checks, instead of a reflection call that silently maps nothing when a
        // configuration class is renamed or moved. No default schema is set — every table names its
        // own, so a table cannot drift into the wrong one by omission.
        modelBuilder.ApplyConfiguration(new TodoListRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TodoItemRecordConfiguration());
        modelBuilder.ApplyConfiguration(new TodoItemTagRecordConfiguration());

        modelBuilder.ApplyConfiguration(new ReminderRecordConfiguration());

        modelBuilder.ApplyConfiguration(new StoredFileRecordConfiguration());
        modelBuilder.ApplyConfiguration(new StoredFileTagRecordConfiguration());

        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
    }
}
