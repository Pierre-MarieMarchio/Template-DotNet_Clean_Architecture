using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

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
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var received = Fixture.DomainEvents.CompletedItems.ShouldHaveSingleItem();

        received.TodoListId.ShouldBe(listId);
        received.TodoItemId.ShouldBe(itemId);
        received.Title.ShouldBe("Do the thing");
        received.OccurredOn.ShouldBe(completedAt);
    }

    [Fact]
    public async Task CompletingAnItem_RunsTheProductsOwnConsumer()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        Fixture.Logs.Clear();

        using var response = await CompleteAsync(client, listId, itemId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var written = Fixture.Logs.Snapshot()
            .Where(record => record.Category.EndsWith("LogTodoItemCompletedConsumer", StringComparison.Ordinal))
            .ToList();

        var record = written.ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Information);
        record.Message.ShouldContain(itemId.ToString());
        record.Message.ShouldContain(listId.ToString());
        record.Message.ShouldContain("Do the thing");
    }

    [Fact]
    public async Task EachCompletion_PublishesExactlyOneEvent()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var first = await AddTodoItemAsync(client, listId, "First");
        var second = await AddTodoItemAsync(client, listId, "Second");

        Fixture.DomainEvents.Clear();

        using var completedFirst = await CompleteAsync(client, listId, first);
        completedFirst.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var completedSecond = await CompleteAsync(client, listId, second);
        completedSecond.StatusCode.ShouldBe(HttpStatusCode.OK);

        Fixture.DomainEvents.CompletedItems.Count.ShouldBe(2);
        Fixture.DomainEvents.CompletedItems.Select(completed => completed.TodoItemId)
            .ShouldBe([first, second], ignoreOrder: true);
    }

    [Fact]
    public async Task ARefusedCompletion_PublishesNothing()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        using var completed = await CompleteAsync(client, listId, itemId);
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);

        Fixture.DomainEvents.Clear();

        using var again = await CompleteAsync(client, listId, itemId);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        Fixture.DomainEvents.CompletedItems.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheConsumerHasRun_TheChangeIsAlreadyCommitted()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Do the thing");

        using var response = await CompleteAsync(client, listId, itemId);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        Fixture.DomainEvents.CompletedItems.ShouldHaveSingleItem();

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        var stored = await LoadTodoListAsync(scope.ServiceProvider, listId);

        stored.Items.Single(item => item.Id == itemId).CompletedAt.ShouldNotBeNull();
    }

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
