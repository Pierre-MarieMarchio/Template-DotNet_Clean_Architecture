using System.Data.Common;
using AppTemplate.Domain.Core.Common.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

public sealed class DomainEventDispatchSaveChangesInterceptor(
    IDomainEventDispatcher dispatcher,
    IEnumerable<IDomainEventSource> sources,
    ILogger<DomainEventDispatchSaveChangesInterceptor> logger) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly IDomainEventSource[] _sources = [.. sources];
    private readonly List<PendingEvent> _pendingEvents = [];
    private readonly List<HeldEvent> _heldEvents = [];

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
        InterceptionResult<int> result) =>
        throw new InvalidOperationException(
            "SaveChanges is refused on a context that publishes domain events: publishing is asynchronous, "
            + "and blocking on it would hold the thread until every consumer has run. Call SaveChangesAsync.");

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var saved = TakePending(eventData.Context);

        if (eventData.Context?.Database.CurrentTransaction is { } transaction)
        {
            Hold(eventData.Context, transaction.TransactionId, saved);
        }
        else
        {
            await PublishAsync(saved);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        RestorePending(eventData.Context);

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public InterceptionResult TransactionCommitting(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (IsHolding(eventData.Context, eventData.TransactionId))
        {
            throw new InvalidOperationException(
                "Commit is refused on a transaction that holds domain events: publishing them is asynchronous. "
                + "Call CommitAsync.");
        }

        return result;
    }

    public async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var committed = Release(eventData.Context, eventData.TransactionId);

        if (committed.Length == 0)
        {
            return;
        }

        await DetachCommittedTransactionAsync(eventData.Context, eventData.TransactionId, cancellationToken);
        await PublishAsync(committed);
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Release(eventData.Context, eventData.TransactionId);
    }

    public Task TransactionRolledBackAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        TransactionRolledBack(transaction, eventData);

        return Task.CompletedTask;
    }

    public void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Release(eventData.Context, eventData.TransactionId);
    }

    public Task TransactionFailedAsync(
        DbTransaction transaction,
        TransactionErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        TransactionFailed(transaction, eventData);

        return Task.CompletedTask;
    }

    private static async Task DetachCommittedTransactionAsync(
        DbContext? context,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        if (context?.Database.CurrentTransaction?.TransactionId == transactionId)
        {
            await context.Database.UseTransactionAsync(null, cancellationToken);
        }
    }

    private async Task PublishAsync(IDomainEvent[] domainEvents)
    {
        foreach (var domainEvent in domainEvents)
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

    private void Hold(DbContext context, Guid transactionId, IDomainEvent[] domainEvents) =>
        _heldEvents.AddRange(domainEvents.Select(domainEvent => new HeldEvent(context, transactionId, domainEvent)));

    private bool IsHolding(DbContext? context, Guid transactionId) =>
        _heldEvents.Exists(held => held.IsFor(context, transactionId));

    private IDomainEvent[] Release(DbContext? context, Guid transactionId)
    {
        IDomainEvent[] released = [.. _heldEvents
            .Where(held => held.IsFor(context, transactionId))
            .Select(held => held.Event)];
        _heldEvents.RemoveAll(held => held.IsFor(context, transactionId));

        return released;
    }

    private void LogDispatchFailure(IDomainEvent domainEvent, Exception exception) =>
        logger.LogError(
            exception,
            "A consumer of domain event {DomainEventType} threw after the transaction had already "
            + "committed. The commit stands and the remaining events are still published, but this "
            + "event did not reach that consumer and will not be retried.",
            domainEvent.GetType().Name);

    private sealed record PendingEvent(DbContext Context, IDomainEventSource Source, IDomainEvent Event);

    private sealed record HeldEvent(DbContext Context, Guid TransactionId, IDomainEvent Event)
    {
        public bool IsFor(DbContext? context, Guid transactionId) =>
            ReferenceEquals(Context, context) && TransactionId == transactionId;
    }
}
