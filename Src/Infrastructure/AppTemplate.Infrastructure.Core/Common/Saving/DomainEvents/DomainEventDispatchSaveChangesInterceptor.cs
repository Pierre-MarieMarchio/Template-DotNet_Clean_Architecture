using AppTemplate.Domain.Core.Common.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

/// <summary>
/// Collects the domain events raised during a save, and publishes them only once that save has
/// committed. The ordering is the entire point: publishing before the commit means a handler can
/// observe — or email about, or bill for — a change that then rolls back, and collecting before,
/// publishing after also drains the aggregates exactly once. Every
/// <see cref="IDomainEventSource"/> in the request scope whose rows are staged on the context
/// being saved is drained (EF does not map the domain types, so no aggregate is ever in its change
/// tracker); a source staged elsewhere is left alone, since a request also commits other contexts
/// and none of those commits is the write its events describe. <b>A publish failure is not a
/// commit failure:</b> by the time dispatch runs the transaction has already committed, and an
/// exception escaping here would tell the caller a write failed that in fact landed — so each
/// event is dispatched on its own and a consumer that throws is logged and stepped over.
/// Handlers run in-process, in the caller's own request, and are not retried; one that must reach
/// another system belongs behind an outbox.
/// </summary>
public sealed class DomainEventDispatchSaveChangesInterceptor(
    IDomainEventDispatcher dispatcher,
    IEnumerable<IDomainEventSource> sources,
    ILogger<DomainEventDispatchSaveChangesInterceptor> logger) : SaveChangesInterceptor
{
    private readonly IDomainEventSource[] _sources = [.. sources];
    // Keyed by the context whose save took them: one scoped instance serves every context instance
    // the scope saves, and each save publishes, or hands back, only what it collected itself.
    private readonly List<PendingEvent> _pendingEvents = [];

    /// <summary>Drains the tracked aggregates' events, to be dispatched once the save lands.</summary>
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

        Collect(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>Drains the tracked aggregates' events, so a synchronous save cannot lose them.</summary>
    /// <param name="eventData">EF's description of the save.</param>
    /// <param name="result">What an earlier interceptor decided; passed through.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Collect(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    /// <summary>Dispatches the drained events, in order, after the transaction committed.</summary>
    /// <param name="eventData">EF's description of the completed save.</param>
    /// <param name="result">The number of rows written; passed through.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns><paramref name="result"/>, unchanged.</returns>
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        foreach (var domainEvent in TakePending(eventData.Context))
        {
            try
            {
                // Not the save's token: the transaction has committed, so abandoning publication
                // because the caller walked away would drop a side effect for a write that landed.
                await dispatcher.DispatchAsync(domainEvent, CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogDispatchFailure(domainEvent, exception);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>The synchronous path exists so that a stray <c>SaveChanges()</c> still
    /// dispatches; it blocks, which is why application code always uses the async one.</summary>
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        foreach (var domainEvent in TakePending(eventData.Context))
        {
            try
            {
                dispatcher.DispatchAsync(domainEvent).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                LogDispatchFailure(domainEvent, exception);
            }
        }

        return base.SavedChanges(eventData, result);
    }

    /// <summary>A failed save committed nothing, so nothing is published and the events go back to
    /// the sources they were drained from, ready for the retry.</summary>
    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        RestorePending(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    /// <summary>Hands the drained events back to their trackers, so a retried save still publishes them.</summary>
    /// <param name="eventData">EF's description of the failure.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        RestorePending(eventData.Context);

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Collect(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var source in _sources.Where(source => source.IsStagedOn(context)))
        {
            foreach (var domainEvent in source.DrainDomainEvents())
            {
                _pendingEvents.Add(new PendingEvent(context, source, domainEvent));
            }
        }
    }

    private IDomainEvent[] TakePending(DbContext? context) =>
        [.. RemovePending(context).Select(pending => pending.Event)];

    private void RestorePending(DbContext? context)
    {
        foreach (var bySource in RemovePending(context).GroupBy(pending => pending.Source))
        {
            bySource.Key.Restore([.. bySource.Select(pending => pending.Event)]);
        }
    }

    private List<PendingEvent> RemovePending(DbContext? context)
    {
        var taken = _pendingEvents.FindAll(pending => ReferenceEquals(pending.Context, context));
        _pendingEvents.RemoveAll(pending => ReferenceEquals(pending.Context, context));

        return taken;
    }

    private void LogDispatchFailure(IDomainEvent domainEvent, Exception exception) =>
        logger.LogError(
            exception,
            "A consumer of domain event {DomainEventType} threw after the transaction had already "
            + "committed. The commit stands and the remaining events are still published, but this "
            + "event did not reach that consumer and will not be retried.",
            domainEvent.GetType().Name);

    private sealed record PendingEvent(DbContext Context, IDomainEventSource Source, IDomainEvent Event);
}
