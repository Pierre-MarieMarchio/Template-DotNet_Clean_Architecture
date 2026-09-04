using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using AppTemplate.Infrastructure.Persistence.Features.Files.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.Files.Models;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.Reminders.Models;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Configurations;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Models;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Common.Contexts;

/// <summary>
/// The one <see cref="DbContext"/> in the system: ASP.NET Identity's tables and the domain's
/// persistence models, in one model, on one connection, with one migrations history.
/// <para>
/// <b>Why one and not two.</b> The previous layout had a context per capability so that each could
/// be migrated independently from its own project. Once all persistence lives in a single project
/// that premise is gone, and a single context buys something the split could not: a real
/// transaction spanning an identity write and a domain write. The features stay separate where
/// separation is worth having — one PostgreSQL schema each, declared in their own configuration
/// classes — rather than by owning different connections to the same database.
/// </para>
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
/// <see cref="Application.Common.Abstractions.IUnitOfWork"/> on behalf of a use case. Repositories,
/// query classes and stores borrow the context and never commit: ownership of the transaction never
/// transfers to them.
/// </para>
/// <para>
/// Two things it deliberately does not do. It does not take <c>ICurrentUser</c>: that would make
/// every instance depend on an HTTP request, so any use outside one — a background worker, a
/// migration, a seeding routine — would stamp audit columns with an empty caller. And it does not
/// override <c>SaveChangesAsync</c> to stamp, flush or dispatch: cross-cutting save behaviour lives
/// in <see cref="Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor"/>
/// implementations, which are separately testable, individually replaceable, and apply to the
/// synchronous overload too — an override does not.
/// </para>
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>The schema ASP.NET Identity's tables live in.</summary>
    public const string IdentitySchema = "identity";

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
    /// key store is the first of these. Neither <see cref="IdentitySchema"/> nor
    /// <see cref="TodoSchema"/> would be honest: the table belongs to no feature.
    /// </summary>
    public const string PlatformSchema = "platform";

    /// <summary>
    /// One history table for one context. It is left in the connection's default schema rather than
    /// inside a feature's, because it belongs to neither: naming it here and in the design-time
    /// factory keeps the tool and the runtime from disagreeing about where applied migrations are
    /// recorded.
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
    /// Refresh-token grants. Internal, like <see cref="RefreshToken"/> itself: the grant table is
    /// reached only through <see cref="Features.Identity.Tables.IRefreshTokenTable"/>, and the
    /// policy for how a grant is hashed, rotated and revoked lives in the identity module.
    /// </summary>
    internal DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Claimed idempotency keys. Internal for the same reason as every other row type here: the
    /// rules for claiming, completing and releasing one live in
    /// <see cref="Common.Idempotency.IdempotencyStore"/>, reached only through
    /// <see cref="Application.Common.Idempotency.IIdempotencyStore"/>.
    /// </summary>
    internal DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

    /// <summary>
    /// The key ring <see cref="IDataProtectionKeyContext"/> requires. Public, unlike every other
    /// set here: the ASP.NET Core data-protection system reads and writes it directly through this
    /// interface, so nothing about it can be internal to this assembly.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        base.OnModelCreating(builder);

        // Named one by one rather than discovered by scanning the assembly: a dozen lines the
        // compiler checks, instead of a reflection call that silently maps nothing when a
        // configuration class is renamed or moved. No default schema is set — every table names its
        // own, so a table cannot drift into the wrong one by omission.
        builder.ApplyConfiguration(new AppUserConfiguration());
        builder.ApplyConfiguration(new AppRoleConfiguration());
        builder.ApplyConfiguration(new RefreshTokenConfiguration());
        builder.ApplyConfiguration(new UserRoleConfiguration());
        builder.ApplyConfiguration(new UserClaimConfiguration());
        builder.ApplyConfiguration(new UserLoginConfiguration());
        builder.ApplyConfiguration(new RoleClaimConfiguration());
        builder.ApplyConfiguration(new UserTokenConfiguration());
        builder.ApplyConfiguration(new DataProtectionKeyConfiguration());

        builder.ApplyConfiguration(new TodoListRecordConfiguration());
        builder.ApplyConfiguration(new TodoItemRecordConfiguration());
        builder.ApplyConfiguration(new TodoItemTagRecordConfiguration());

        builder.ApplyConfiguration(new ReminderRecordConfiguration());

        builder.ApplyConfiguration(new StoredFileRecordConfiguration());

        builder.ApplyConfiguration(new IdempotencyRecordConfiguration());
    }
}
