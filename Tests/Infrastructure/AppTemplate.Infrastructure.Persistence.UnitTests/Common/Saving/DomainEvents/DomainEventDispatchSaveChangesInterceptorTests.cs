using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Model.Events;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Mapping;
using AppTemplate.Infrastructure.Persistence.Features.TodoLists.Rows.Tracking;
using AppTemplate.Infrastructure.Persistence.UnitTests.Features.TodoLists;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Saving.DomainEvents;

/// <summary>
/// Publishing after the commit is only half the guarantee. The other half is what happens when the
/// commit does not arrive, and when a consumer misbehaves after it has.
/// </summary>
/// <remarks>
/// The contexts are real and never connect: a save is described to the interceptor rather than run,
/// and the context only has to say which rows it is tracking.
/// </remarks>
public sealed class DomainEventDispatchSaveChangesInterceptorTests : IDisposable
{
    private const string _neverOpened = "Host=nowhere;Database=never-opened;Username=none;Password=none";

    private static readonly DateTimeOffset _now = new(2026, 6, 7, 8, 9, 10, TimeSpan.Zero);

    private readonly TodoListMapper _mapper = new();
    private readonly TodoListTracker _tracker;
    private readonly RecordingLogger<DomainEventDispatchSaveChangesInterceptor> _logger = new();

    /// <summary>The context the tracked rows are staged on.</summary>
    private readonly AppDbContext _context = AnAppDbContext();

    public DomainEventDispatchSaveChangesInterceptorTests() => _tracker = new TodoListTracker(_mapper);

    public void Dispose() => _context.Dispose();

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

        interceptor.SavingChanges(ASaveChangesEvent.Saving(_context), default);
        interceptor.SaveChangesFailed(
            ASaveChangesEvent.Failed(_context, new InvalidOperationException("nope")));

        dispatcher.Dispatched.ShouldBeEmpty();

        interceptor.SavingChanges(ASaveChangesEvent.Saving(_context), default);
        interceptor.SavedChanges(ASaveChangesEvent.Saved(_context, rowsAffected: 1), result: 1);

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

    /// <summary>
    /// The idempotency store commits through a second instance of the same context in the same
    /// request. Its save is not the business commit, so it takes none of the business events.
    /// </summary>
    [Fact]
    public async Task ASaveOfAnotherInstanceOfTheContext_TakesNoneOfTheEvents()
    {
        var dispatcher = new RecordingDomainEventDispatcher();
        var interceptor = AnInterceptor(dispatcher);

        Track(ANewList("Groceries"));

        await using (var other = AnAppDbContext())
        {
            await SaveSucceedsAsync(interceptor, other);
        }

        dispatcher.Dispatched.ShouldBeEmpty("that commit did not write the list");

        await SaveSucceedsAsync(interceptor);

        dispatcher.Dispatched.ShouldHaveSingleItem().ShouldBeOfType<TodoListCreatedDomainEvent>();
    }

    /// <summary>
    /// A context whose model has no such row, as the identity module's has none of the business
    /// rows, is answered without being asked about a row it cannot map.
    /// </summary>
    [Fact]
    public async Task ASaveOfAContextThatDoesNotMapTheRows_TakesNoneOfTheEvents()
    {
        var dispatcher = new RecordingDomainEventDispatcher();
        var interceptor = AnInterceptor(dispatcher);

        Track(ANewList("Groceries"));

        await using (var unrelated = new UnrelatedContext(
            new DbContextOptionsBuilder<UnrelatedContext>().UseNpgsql(_neverOpened).Options))
        {
            await SaveSucceedsAsync(interceptor, unrelated);
        }

        dispatcher.Dispatched.ShouldBeEmpty();

        await SaveSucceedsAsync(interceptor);

        dispatcher.Dispatched.ShouldHaveSingleItem();
    }

    /// <summary>
    /// What a save collected belongs to that save: another context's failure hands nothing back, and
    /// does not stop the save that collected the events from publishing them.
    /// </summary>
    [Fact]
    public async Task AnotherContextsFailure_LeavesThisSavesEventsToIt()
    {
        var dispatcher = new RecordingDomainEventDispatcher();
        var interceptor = AnInterceptor(dispatcher);

        Track(ANewList("Groceries"));

        await interceptor.SavingChangesAsync(
            ASaveChangesEvent.Saving(_context),
            default,
            TestContext.Current.CancellationToken);

        await using (var other = AnAppDbContext())
        {
            await interceptor.SaveChangesFailedAsync(
                ASaveChangesEvent.Failed(other, new InvalidOperationException("the claim lost its race")),
                TestContext.Current.CancellationToken);
        }

        await interceptor.SavedChangesAsync(
            ASaveChangesEvent.Saved(_context, rowsAffected: 1),
            1,
            TestContext.Current.CancellationToken);

        dispatcher.Dispatched.ShouldHaveSingleItem().ShouldBeOfType<TodoListCreatedDomainEvent>();
    }

    // ---- Fixture -------------------------------------------------------------------------------

    private DomainEventDispatchSaveChangesInterceptor AnInterceptor(IDomainEventDispatcher dispatcher) =>
        new(dispatcher, [_tracker], _logger);

    private Task<int> SaveSucceedsAsync(
        DomainEventDispatchSaveChangesInterceptor interceptor,
        int rowsAffected = 1) => SaveSucceedsAsync(interceptor, _context, rowsAffected);

    private static async Task<int> SaveSucceedsAsync(
        DomainEventDispatchSaveChangesInterceptor interceptor,
        DbContext context,
        int rowsAffected = 1)
    {
        await interceptor.SavingChangesAsync(
            ASaveChangesEvent.Saving(context),
            default,
            TestContext.Current.CancellationToken);

        return await interceptor.SavedChangesAsync(
            ASaveChangesEvent.Saved(context, rowsAffected),
            rowsAffected,
            TestContext.Current.CancellationToken);
    }

    private async Task SaveFailsAsync(DomainEventDispatchSaveChangesInterceptor interceptor)
    {
        await interceptor.SavingChangesAsync(
            ASaveChangesEvent.Saving(_context),
            default,
            TestContext.Current.CancellationToken);

        await interceptor.SaveChangesFailedAsync(
            ASaveChangesEvent.Failed(_context, new InvalidOperationException("the row was gone")),
            TestContext.Current.CancellationToken);
    }

    private static TodoList ANewList(string name) =>
        TodoList.Create(ATodoListAggregate.OwnerId, name, _now);

    /// <summary>Stages the list's row on <see cref="_context"/>, as the repository does.</summary>
    private void Track(TodoList aggregate)
    {
        var record = _mapper.ToNewRecord(aggregate);

        _context.Add(record);
        _tracker.Track(aggregate, record);
    }

    private static AppDbContext AnAppDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_neverOpened).Options);

    /// <summary>A context with a provider and a model that maps none of the business rows.</summary>
    private sealed class UnrelatedContext(DbContextOptions<UnrelatedContext> options) : DbContext(options);
}
