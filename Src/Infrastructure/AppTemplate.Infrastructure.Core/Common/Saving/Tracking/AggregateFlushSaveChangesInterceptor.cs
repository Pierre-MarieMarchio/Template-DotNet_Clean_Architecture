using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Tracking;

/// <summary>
/// Runs every feature's <see cref="IAggregateFlusher"/> immediately before EF computes its diff, and
/// reads the store's own values back immediately after a successful save. <b>Only the flushers
/// staged on the context being saved:</b> mapping an aggregate onto another context would at best
/// write nothing and at worst throw for a row its model does not have. <b>This must be the first
/// interceptor added:</b> audit stamping only sees an entry that is already <c>Added</c> or
/// <c>Modified</c>, and event collection only sees aggregates whose state has been settled;
/// interceptors run in the order they are added, so the order in <c>PersistenceModule</c> is
/// load-bearing. <c>DetectChanges</c> is called explicitly after flushing, because its ordering
/// relative to save-changes interception is a provider implementation detail and a mapping that
/// landed after the last detection would simply not be written. It exists as an interceptor rather
/// than a step inside the unit of work so that a stray <c>context.SaveChanges()</c> still flushes —
/// a flush that only happened on the blessed path would be a silent data-loss bug.
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

    /// <summary>Tells each tracker the version and audit stamps the store just decided, once the save landed.</summary>
    /// <param name="eventData">EF's description of the completed save.</param>
    /// <param name="result">The number of rows written; passed through.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        RefreshFromStore(eventData.Context);

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
        ArgumentNullException.ThrowIfNull(eventData);

        RefreshFromStore(eventData.Context);

        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    private void Flush(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var staged = StagedOn(context);

        foreach (var flusher in staged)
        {
            flusher.FlushTo(context);
        }

        if (staged.Length > 0)
        {
            context.ChangeTracker.DetectChanges();
        }
    }

    private void RefreshFromStore(DbContext? context)
    {
        foreach (var flusher in StagedOn(context))
        {
            flusher.RefreshFromStore();
        }
    }

    private IAggregateFlusher[] StagedOn(DbContext? context) =>
        context is null ? [] : [.. _flushers.Where(flusher => flusher.IsStagedOn(context))];
}
