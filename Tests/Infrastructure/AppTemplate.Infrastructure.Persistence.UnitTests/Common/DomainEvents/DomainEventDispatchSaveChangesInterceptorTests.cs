using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Infrastructure.Persistence.Common.DomainEvents;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Tracking;
using AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.DomainEvents;

/// <summary>
/// Publishing after the commit is only half the guarantee. The other half is what happens when the
/// commit does not arrive, and when a consumer misbehaves after it has.
/// </summary>
public sealed class DomainEventDispatchSaveChangesInterceptorTests
{
    private static readonly DateTimeOffset _now = new(2026, 6, 7, 8, 9, 10, TimeSpan.Zero);

    private readonly TodoListMapper _mapper = new();
    private readonly TodoListTracker _tracker;
    private readonly RecordingLogger<DomainEventDispatchSaveChangesInterceptor> _logger = new();

    public DomainEventDispatchSaveChangesInterceptorTests() => _tracker = new TodoListTracker(_mapper);

    [Fact]
    public async Task ACommittedSave_PublishesEveryCollectedEventOnce()
    {
        var dispatcher = new RecordingDomainEventDispatcher();
        var interceptor = AnInterceptor(dispatcher);

        Track(ANewList("Groceries"));

        await SaveSucceedsAsync(interceptor);

        dispatcher.Dispatched.ShouldHaveSingleItem().ShouldBeOfType<TodoListCreatedDomainEvent>();

        await SaveSucceedsAsync(interceptor);

        dispatcher.Dispatched.Count.ShouldBe(1, "a committed save must not re-publish what it published");
    }

    /// <summary>
    /// A failed save commits nothing, so nothing may be published — and nothing may be lost either. The
    /// documented recovery for a lost update is to reload and save again, and the events were taken out
    /// of the aggregates before the failure: discarding them would leave the retry publishing silence.
    /// </summary>
    [Fact]
    public async Task AFailedSave_PublishesNothingButKeepsTheEventsForTheRetry()
    {
        var dispatcher = new RecordingDomainEventDispatcher();
        var interceptor = AnInterceptor(dispatcher);

        Track(ANewList("Groceries"));

        await SaveFailsAsync(interceptor);

        dispatcher.Dispatched.ShouldBeEmpty("the transaction rolled back; nothing happened");

        await SaveSucceedsAsync(interceptor);

        dispatcher.Dispatched.ShouldHaveSingleItem().ShouldBeOfType<TodoListCreatedDomainEvent>();

        await SaveSucceedsAsync(interceptor);

        dispatcher.Dispatched.Count.ShouldBe(
            1,
            "exactly once: given back after the failure, and taken for good by the retry.");
    }

    [Fact]
    public void AFailedSynchronousSave_KeepsTheEventsToo()
    {
        var dispatcher = new RecordingDomainEventDispatcher();
        var interceptor = AnInterceptor(dispatcher);

        Track(ANewList("Groceries"));

        interceptor.SavingChanges(ASaveChangesEvent.Saving(), default);
        interceptor.SaveChangesFailed(ASaveChangesEvent.Failed(new InvalidOperationException("nope")));

        dispatcher.Dispatched.ShouldBeEmpty();

        interceptor.SavingChanges(ASaveChangesEvent.Saving(), default);
        interceptor.SavedChanges(ASaveChangesEvent.Saved(rowsAffected: 1), result: 1);

        dispatcher.Dispatched.ShouldHaveSingleItem();
    }

    /// <summary>
    /// The worst failure shape available, and the one this guards: by the time dispatch runs the
    /// transaction has committed, so an exception escaping here would tell the caller a write failed that
    /// in fact landed — and the caller would retry an already-applied write.
    /// </summary>
    [Fact]
    public async Task AThrowingConsumer_LeavesTheCommitAlone_AndTheOtherEventsPublished()
    {
        var failure = new InvalidOperationException("the consumer could not reach the mail relay");
        var dispatcher = new RecordingDomainEventDispatcher(
            domainEvent => domainEvent is TodoListCreatedDomainEvent ? failure : null);
        var interceptor = AnInterceptor(dispatcher);

        var aggregate = ANewList("Groceries");
        var itemId = aggregate.AddItem("Buy milk", description: null);
        aggregate.CompleteItem(itemId, _now);
        Track(aggregate);

        int rowsAffected = await SaveSucceedsAsync(interceptor, rowsAffected: 7);

        rowsAffected.ShouldBe(7, "a publish failure is not a commit failure");

        dispatcher.Dispatched.ShouldHaveSingleItem().ShouldBeOfType<TodoItemCompletedDomainEvent>();
    }

    [Fact]
    public async Task AThrowingConsumer_IsReportedWithTheEventItFailedOn()
    {
        var failure = new InvalidOperationException("the consumer could not reach the mail relay");
        var interceptor = AnInterceptor(new RecordingDomainEventDispatcher(_ => failure));

        Track(ANewList("Groceries"));

        await SaveSucceedsAsync(interceptor);

        var reported = _logger.Entries.ShouldHaveSingleItem();
        reported.Level.ShouldBe(LogLevel.Error);
        reported.Exception.ShouldBeSameAs(failure);
        // A swallowed failure that does not say which event it lost is not diagnosable.
        reported.Message.ShouldContain(nameof(TodoListCreatedDomainEvent));
    }

    /// <summary>
    /// A failed dispatch is not a failed commit, and therefore not something the next save retries: the
    /// event was taken and is gone. Publishing it again on the next save would make delivery at-least-once
    /// for consumers that happened to fail, which is a different contract from the one documented.
    /// </summary>
    [Fact]
    public async Task AThrownAwayEvent_IsNotPublishedAgainByTheNextSave()
    {
        var dispatched = new List<IDomainEvent>();
        var interceptor = AnInterceptor(new RecordingDomainEventDispatcher(domainEvent =>
        {
            dispatched.Add(domainEvent);

            return new InvalidOperationException("still failing");
        }));

        Track(ANewList("Groceries"));

        await SaveSucceedsAsync(interceptor);
        await SaveSucceedsAsync(interceptor);

        dispatched.Count.ShouldBe(1);
    }

    // ---- Fixture -------------------------------------------------------------------------------

    private DomainEventDispatchSaveChangesInterceptor AnInterceptor(IDomainEventDispatcher dispatcher) =>
        new(dispatcher, [_tracker], _logger);

    private static async Task<int> SaveSucceedsAsync(
        DomainEventDispatchSaveChangesInterceptor interceptor,
        int rowsAffected = 1)
    {
        await interceptor.SavingChangesAsync(
            ASaveChangesEvent.Saving(),
            default,
            TestContext.Current.CancellationToken);

        return await interceptor.SavedChangesAsync(
            ASaveChangesEvent.Saved(rowsAffected),
            rowsAffected,
            TestContext.Current.CancellationToken);
    }

    private static async Task SaveFailsAsync(DomainEventDispatchSaveChangesInterceptor interceptor)
    {
        await interceptor.SavingChangesAsync(
            ASaveChangesEvent.Saving(),
            default,
            TestContext.Current.CancellationToken);

        await interceptor.SaveChangesFailedAsync(
            ASaveChangesEvent.Failed(new InvalidOperationException("the row was gone")),
            TestContext.Current.CancellationToken);
    }

    private static TodoList ANewList(string name) =>
        TodoList.Create(ATodoListAggregate.OwnerId, name, _now);

    private void Track(TodoList aggregate) => _tracker.Track(aggregate, _mapper.ToNewRecord(aggregate));
}
