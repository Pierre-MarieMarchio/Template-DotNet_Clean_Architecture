using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Infrastructure.Persistence.Common.Saving;

/// <summary>
/// The transactional boundary, which for a single <see cref="AppDbContext"/> is exactly one call to
/// <c>SaveChangesAsync</c>: EF wraps a save in a transaction of its own, so everything a use case
/// staged either lands or none of it does. Who owns that boundary, and where it does not reach, is
/// argued in docs/ARCHITECTURE.md under "The transaction boundary, and who owns it".
/// <para>
/// Two constraints a caller cannot read off the signature.
/// </para>
/// <list type="bullet">
/// <item><description>ASP.NET Identity's own stores commit through the same context by themselves —
/// <c>UserManager.CreateAsync</c> saves before it returns — so a use case must not assume that an
/// account creation and a domain write share a transaction. Everything <em>this</em> type commits
/// does.</description></item>
/// <item><description><see cref="DbUpdateConcurrencyException"/> is translated here rather than
/// allowed to travel outward, because it names EF and carries EF's <c>Entries</c>: letting it
/// through would put an EF type into the Application layer's vocabulary and into the transport's
/// exception filter. <see cref="ConcurrencyConflictException"/> takes its place, and the original is
/// kept as the inner exception so the log still says exactly which rows lost.</description></item>
/// </list>
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
