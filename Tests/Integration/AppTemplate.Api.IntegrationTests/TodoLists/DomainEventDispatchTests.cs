using System.Net;
using AppTemplate.Api.Features.TodoLists.Contracts;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

/// <summary>
/// A domain event raised inside a request really reaches a consumer — the whole mechanism can be
/// present and dispatch nothing.
/// </summary>
/// <remarks>
/// <para>
/// Two independent assertions, deliberately. The recording consumer the test host registers proves
/// that a consumer registered through the persistence module's public entry point is resolved and run.
/// The log the product's own <c>LogTodoItemCompletedConsumer</c> writes proves the <em>shipped</em>
/// consumer ran, which a double registered alongside it cannot show.
/// </para>
/// <para>
/// Events are raised by the aggregate and EF does not track the aggregate, so an interceptor walking
/// <c>ChangeTracker.Entries&lt;IAggregateRoot&gt;()</c> would find nothing and fail silently. They are
/// drained from the feature's aggregate tracker instead, and these tests are what prove it happens.
/// </para>
/// </remarks>
public sealed class DomainEventDispatchTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CompletingAnItem_ReachesTheRegisteredConsumer()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        Clock.Advance(TimeSpan.FromMinutes(4));
        var completedAt = Clock.UtcNow;

        Fixture.DomainEvents.Clear();

        using var response = await CompleteAsync(client, listId, itemId);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var received = Fixture.DomainEvents.CompletedItems.ShouldHaveSingleItem();

        received.TodoListId.ShouldBe(listId);
        received.TodoItemId.ShouldBe(itemId);
        received.Title.ShouldBe("Do the thing");
        received.OccurredOn.ShouldBe(completedAt);
    }

    /// <summary>
    /// The product's own consumer, whose only observable effect is a log line. Asserting on it is the
    /// only way to know the shipped code ran rather than only the test's double.
    /// </summary>
    [Fact]
    public async Task CompletingAnItem_RunsTheProductsOwnConsumer()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        Fixture.Logs.Clear();

        using var response = await CompleteAsync(client, listId, itemId);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var written = Fixture.Logs.Snapshot()
            .Where(record => record.Category.EndsWith("LogTodoItemCompletedConsumer", StringComparison.Ordinal))
            .ToList();

        var record = written.ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Information);
        record.Message.ShouldContain(itemId.ToString());
        record.Message.ShouldContain(listId.ToString());
        record.Message.ShouldContain("Do the thing");
    }

    /// <summary>
    /// One event per completion, not one per subsequent save of the same tracked aggregate: the events
    /// are drained when they are collected.
    /// </summary>
    [Fact]
    public async Task EachCompletion_PublishesExactlyOneEvent()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var first = await AddTodoItemAsync(client, listId, "First");
        var second = await AddTodoItemAsync(client, listId, "Second");

        Fixture.DomainEvents.Clear();

        using var completedFirst = await CompleteAsync(client, listId, first);
        completedFirst.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var completedSecond = await CompleteAsync(client, listId, second);
        completedSecond.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        Fixture.DomainEvents.CompletedItems.Count.ShouldBe(2);
        Fixture.DomainEvents.CompletedItems.Select(completed => completed.TodoItemId)
            .ShouldBe([first, second], ignoreOrder: true);
    }

    /// <summary>
    /// A request that never committed published nothing. Publishing before the commit would let a
    /// handler act on a change that then rolled back.
    /// </summary>
    [Fact]
    public async Task ARefusedCompletion_PublishesNothing()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        using var completed = await CompleteAsync(client, listId, itemId);
        completed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        Fixture.DomainEvents.Clear();

        using var again = await CompleteAsync(client, listId, itemId);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        Fixture.DomainEvents.CompletedItems.ShouldBeEmpty();
    }

    /// <summary>
    /// The event is published after the transaction commits, so by the time a consumer has run the
    /// change it describes is durable — asserted by reading the row back outside the request.
    /// </summary>
    [Fact]
    public async Task WhenTheConsumerHasRun_TheChangeIsAlreadyCommitted()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        using var response = await CompleteAsync(client, listId, itemId);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        Fixture.DomainEvents.CompletedItems.ShouldHaveSingleItem();

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        var stored = await LoadTodoListAsync(scope.ServiceProvider, listId);

        stored.Items.Single(item => item.Id == itemId).CompletedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// <c>TodoListCreatedDomainEvent</c> has no consumer registered anywhere. Dispatching it must be a
    /// no-op rather than a failure, or adding an event would mean having to add a consumer for it.
    /// </summary>
    [Fact]
    public async Task AnEventWithNoConsumer_IsHarmless()
    {
        var (client, _, _) = await SignInAsync();

        var listId = await CreateTodoListAsync(client, "Nobody listens");

        listId.ShouldNotBe(Guid.Empty);
        Fixture.DomainEvents.CompletedItems.ShouldBeEmpty();
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, Guid listId, Guid itemId) =>
        client.PostAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}/complete", UriKind.Relative),
            content: null,
            TestToken);
}
