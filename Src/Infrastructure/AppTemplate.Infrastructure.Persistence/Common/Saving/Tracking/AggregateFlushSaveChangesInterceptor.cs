using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Persistence.Common.Saving.Tracking;

/// <summary>
/// Runs every feature's <see cref="IAggregateFlusher"/> immediately before EF computes its diff, and
/// reads the store's own values back immediately after a successful save.
/// <para>
/// <b>This must be the first interceptor added.</b> Audit stamping only sees an entry that is already
/// <c>Added</c> or <c>Modified</c>, and event collection only sees aggregates whose state has been
/// settled; both depend on the flush having happened. Interceptors run in the order they are added,
/// so the order in <c>PersistenceModule</c> is load-bearing rather than cosmetic.
/// </para>
/// <para>
/// <c>DetectChanges</c> is called explicitly after flushing. EF calls it too, but the ordering
/// relative to save-changes interception is an implementation detail of the provider, and a mapping
/// that landed after the last detection would simply not be written. Calling it here is idempotent
/// and makes the guarantee independent of that detail.
/// </para>
/// <para>
/// It exists as an interceptor rather than as a step inside the unit of work so that a stray
/// <c>context.SaveChanges()</c> — from a test, from ASP.NET Identity's own stores, from a future
/// caller who forgets — still flushes. A flush that only happened on the blessed path would be a
/// silent data-loss bug the first time somebody left it.
/// </para>
/// </summary>
internal sealed class AggregateFlushSaveChangesInterceptor(IEnumerable<IAggregateFlusher> flushers)
    : SaveChangesInterceptor
{
    private readonly IAggregateFlusher[] _flushers = [.. flushers];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Flush(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Flush(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        RefreshFromStore();

        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        RefreshFromStore();

        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void Flush(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var flusher in _flushers)
        {
            flusher.FlushTo(context);
        }

        context.ChangeTracker.DetectChanges();
    }

    private void RefreshFromStore()
    {
        foreach (var flusher in _flushers)
        {
            flusher.RefreshFromStore();
        }
    }
}
