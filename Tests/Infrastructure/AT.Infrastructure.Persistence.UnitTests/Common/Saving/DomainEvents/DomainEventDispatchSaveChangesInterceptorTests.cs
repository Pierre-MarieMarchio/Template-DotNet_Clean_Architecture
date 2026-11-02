using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Model.Events;
using AppTemplate.Infrastructure.Core.Common.Contexts;
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

public sealed class DomainEventDispatchSaveChangesInterceptorTests : IDisposable
{
    private const string _neverOpened = DatabaseProvider.DesignTimeConnectionString;

    private static readonly DateTimeOffset _now = new(2026, 6, 7, 8, 9, 10, TimeSpan.Zero);

    private readonly TodoListMapper _mapper = new();
    private readonly TodoListTracker _tracker;
    private readonly RecordingLogger<DomainEventDispatchSaveChangesInterceptor> _logger = new();

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
        reported.Message.ShouldContain(nameof(TodoListCreatedDomainEvent));
    }

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

    [Fact]
    public async Task ASaveOfAContextThatDoesNotMapTheRows_TakesNoneOfTheEvents()
    {
        var dispatcher = new RecordingDomainEventDispatcher();
        var interceptor = AnInterceptor(dispatcher);

        Track(ANewList("Groceries"));

        await using (var unrelated = new UnrelatedContext(
            new DbContextOptionsBuilder<UnrelatedContext>().UseDatabase(_neverOpened).Options))
        {
            await SaveSucceedsAsync(interceptor, unrelated);
        }

        dispatcher.Dispatched.ShouldBeEmpty();

        await SaveSucceedsAsync(interceptor);

        dispatcher.Dispatched.ShouldHaveSingleItem();
    }

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

    private void Track(TodoList aggregate)
    {
        var record = _mapper.ToNewRecord(aggregate);

        _context.Add(record);
        _tracker.Track(aggregate, record);
    }

    private static AppDbContext AnAppDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseDatabase(_neverOpened).Options);

    private sealed class UnrelatedContext(DbContextOptions<UnrelatedContext> options) : DbContext(options);
}
