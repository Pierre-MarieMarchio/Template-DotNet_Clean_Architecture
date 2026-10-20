using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AppTemplate.Infrastructure.Core.Common.Saving.Tracking;

public sealed class AggregateFlushSaveChangesInterceptor(IEnumerable<IAggregateFlusher> flushers)
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
        ArgumentNullException.ThrowIfNull(eventData);

        RefreshFromStore(eventData.Context);

        return base.SavedChanges(eventData, result);
    }

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
