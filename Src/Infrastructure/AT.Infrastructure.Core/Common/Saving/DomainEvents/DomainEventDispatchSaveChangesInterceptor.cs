using AppTemplate.Domain.Core.Common.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

public sealed class DomainEventDispatchSaveChangesInterceptor(
    IDomainEventDispatcher dispatcher,
    IEnumerable<IDomainEventSource> sources,
    ILogger<DomainEventDispatchSaveChangesInterceptor> logger) : SaveChangesInterceptor
{
    private readonly IDomainEventSource[] _sources = [.. sources];
    private readonly List<PendingEvent> _pendingEvents = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Collect(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Collect(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

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
                await dispatcher.DispatchAsync(domainEvent, CancellationToken.None);
            }
            catch (Exception exception)
            {
                LogDispatchFailure(domainEvent, exception);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

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

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        RestorePending(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

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
