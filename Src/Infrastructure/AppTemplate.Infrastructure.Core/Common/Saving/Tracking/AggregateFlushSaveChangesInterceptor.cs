using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Tracking;

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
public sealed class AggregateFlushSaveChangesInterceptor(IEnumerable<IAggregateFlusher> flushers)
    : SaveChangesInterceptor
{
    private readonly IAggregateFlusher[] _flushers = [.. flushers];

    /// <summary>Flushes every tracked aggregate onto its row before EF computes its diff.</summary>
    /// <param name="eventData">EF's description of the save.</param>
    /// <param name="result">What an earlier interceptor decided; passed through.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Flush(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <summary>The asynchronous half of <see cref="SavingChanges"/>, doing the same flush.</summary>
    /// <param name="eventData">EF's description of the save.</param>
    /// <param name="result">What an earlier interceptor decided; passed through.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Flush(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Tells each tracker the version and audit stamps the store just decided, once the save landed.
    /// </summary>
    /// <param name="eventData">EF's description of the completed save.</param>
    /// <param name="result">The number of rows written; passed through.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        RefreshFromStore();

        return base.SavedChanges(eventData, result);
    }

    /// <summary>The asynchronous half of <see cref="SavedChanges"/>.</summary>
    /// <param name="eventData">EF's description of the completed save.</param>
    /// <param name="result">The number of rows written; passed through.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
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
