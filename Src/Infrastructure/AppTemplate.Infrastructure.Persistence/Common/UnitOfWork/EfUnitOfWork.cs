using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Common.UnitOfWork;

/// <summary>
/// The transactional boundary, which for a single <see cref="AppDbContext"/> is exactly one call to
/// <c>SaveChangesAsync</c>: EF wraps a save in a transaction of its own, so everything a use case
/// staged either lands or none of it does.
/// <para>
/// <b>Why it is named <c>EfUnitOfWork</c> and not <c>UnitOfWork</c>.</b> The folder is
/// <c>UnitOfWork/</c>, so the namespace is <c>…Common.UnitOfWork</c>; a type of the same name inside
/// it makes every mention of <c>UnitOfWork</c> ambiguous between the namespace and the type (CS0307).
/// The prefix says which technology implements the port and costs nothing.
/// </para>
/// <para>
/// <b>Resource ownership, stated once and in full.</b>
/// </para>
/// <list type="bullet">
/// <item><description><b>Who opens it.</b> The DI container. The context, this unit of
/// work and the interceptors are all scoped to the same request, so the use case never
/// creates or opens anything itself and there is no second unit of work to reconcile.</description></item>
/// <item><description><b>Who commits it.</b> The use case, and only the use case, by
/// calling <see cref="SaveChangesAsync"/> once. Repositories and stores stage; they never save. That
/// is the whole reason this indirection exists — not the one line of code, but the fact that a
/// repository or store has no way to commit on its own, so a use case cannot commit by accident
/// through one.</description></item>
/// <item><description><b>When ownership transfers.</b> It does not. A repository, a store or a query
/// class borrows the context for the duration of a call and hands nothing back that keeps
/// the transaction open. Ownership ends when the request scope is disposed, which is the
/// container's job; no adapter may dispose the context, and no adapter may hold it beyond
/// the scope. An ambiguously owned transaction is the API analogue of a double free.</description></item>
/// <item><description><b>Who else may be involved.</b> ASP.NET Identity's own stores, unavoidably:
/// <c>UserManager.CreateAsync</c> commits through the same context on its own. That is a framework
/// behaviour, not a choice, and it is why a use case must not assume that an account creation and a
/// domain write share a transaction — everything <em>this</em> type commits does.</description></item>
/// </list>
/// <para>
/// <b>It is also the boundary where a lost update stops being an EF concept.</b>
/// <see cref="DbUpdateConcurrencyException"/> is provider-shaped: it names EF and carries EF's
/// <c>Entries</c>, so letting it travel outward would put an EF type into the Application layer's
/// vocabulary and into the transport's exception filter. It is translated here, at the one place a
/// commit happens, into <see cref="ConcurrencyConflictException"/> — which the API answers as 409 with
/// the stable code <c>concurrency.conflict</c>. The original is kept as the inner exception, so the log
/// still says exactly which rows lost.
/// </para>
/// </summary>
internal sealed class EfUnitOfWork(AppDbContext context) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Translated, not swallowed and not retried. Retrying here would re-apply a decision the
            // caller made against state that no longer exists, which is the lost update the token
            // exists to prevent. The caller is told instead, and the staged changes stay uncommitted.
            throw new ConcurrencyConflictException(
                "A concurrent modification of the same aggregate was detected; this write was rejected.",
                exception);
        }
    }
}
