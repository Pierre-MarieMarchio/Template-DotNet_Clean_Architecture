using AppTemplate.Domain.Common.Events;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Persistence.Common.Saving.DomainEvents;

/// <summary>
/// Collects the domain events raised during a save, and publishes them only once that save has
/// committed.
/// <para>
/// The ordering is the entire point. Publishing before the commit means a handler can
/// observe — or email about, or bill for — a change that then rolls back. Collecting before
/// and publishing after also drains the aggregates exactly once, so an event cannot be
/// delivered twice by a later save in the same request.
/// </para>
/// <para>
/// <b>Where the events come from.</b> Every <see cref="IDomainEventSource"/> in the request scope is
/// drained, because EF does not map the domain types and no aggregate is ever in its change tracker.
/// Which source each event came from is remembered, so a failed save can hand it back.
/// </para>
/// <para>
/// <b>A publish failure is not a commit failure.</b> By the time dispatch runs the transaction is
/// already committed, so an exception escaping here would tell the caller a write failed that in fact
/// landed — and the caller would retry it. Each event is therefore dispatched on its own, and a
/// consumer that throws is logged and stepped over.
/// </para>
/// <para>
/// Handlers run in-process and are not retried: this is the deliberately small version. A
/// system that cannot lose a notification needs the events written to an outbox table
/// inside the same transaction, which is a different mechanism, not a bigger one.
/// </para>
/// </summary>
internal sealed class DomainEventDispatchSaveChangesInterceptor(
    IDomainEventDispatcher dispatcher,
    IEnumerable<IDomainEventSource> sources,
    ILogger<DomainEventDispatchSaveChangesInterceptor> logger) : SaveChangesInterceptor
{
    private readonly IDomainEventSource[] _sources = [.. sources];
    private readonly List<(IDomainEventSource Source, IDomainEvent Event)> _pendingEvents = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Collect();

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Collect();

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in TakePending())
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
        foreach (var domainEvent in TakePending())
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
        RestorePending();
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        RestorePending();

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    private void Collect()
    {
        foreach (var source in _sources)
        {
            foreach (var domainEvent in source.DrainDomainEvents())
            {
                _pendingEvents.Add((source, domainEvent));
            }
        }
    }

    private IDomainEvent[] TakePending()
    {
        IDomainEvent[] events = [.. _pendingEvents.Select(pending => pending.Event)];
        _pendingEvents.Clear();

        return events;
    }

    private void RestorePending()
    {
        foreach (var bySource in _pendingEvents.GroupBy(pending => pending.Source))
        {
            bySource.Key.Restore([.. bySource.Select(pending => pending.Event)]);
        }

        _pendingEvents.Clear();
    }

    private void LogDispatchFailure(IDomainEvent domainEvent, Exception exception) =>
        logger.LogError(
            exception,
            "A consumer of domain event {DomainEventType} threw after the transaction had already "
            + "committed. The commit stands and the remaining events are still published, but this "
            + "event did not reach that consumer and will not be retried.",
            domainEvent.GetType().Name);
}
