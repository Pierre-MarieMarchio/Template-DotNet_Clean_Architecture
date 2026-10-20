using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

public sealed class AuditingTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreatingAList_StampsTheInstantAndTheRealCaller()
    {
        var (client, _, session) = await SignInAsync();
        var createdAt = Clock.UtcNow;

        var listId = await CreateTodoListAsync(client, "Audited");

        var stored = await LoadAggregateAsync(listId);

        stored.CreatedAt.ShouldBe(createdAt);
        stored.CreatedBy.ShouldBe(session.UserId);
        stored.LastModifiedAt.ShouldBeNull();
        stored.LastModifiedBy.ShouldBeNull();
    }

    [Fact]
    public async Task ModifyingAList_StampsTheModificationAndLeavesTheCreationAlone()
    {
        var (client, _, session) = await SignInAsync();
        var createdAt = Clock.UtcNow;

        var listId = await CreateTodoListAsync(client, "Audited");

        Clock.Advance(TimeSpan.FromMinutes(7));
        var modifiedAt = Clock.UtcNow;

        await AddTodoItemAsync(client, listId, "An item");

        var stored = await LoadAggregateAsync(listId);

        stored.CreatedAt.ShouldBe(createdAt);
        stored.CreatedBy.ShouldBe(session.UserId);
        stored.LastModifiedAt.ShouldBe(modifiedAt);
        stored.LastModifiedBy.ShouldBe(session.UserId);
    }

    [Fact]
    public async Task AChangeToAChild_StampsTheRoot()
    {
        var (client, _, session) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Audited");
        var itemId = await AddTodoItemAsync(client, listId, "An item");

        var afterAdd = await LoadAggregateAsync(listId);

        Clock.Advance(TimeSpan.FromMinutes(1));
        var completedAt = Clock.UtcNow;

        using var completed = await client.PostAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}/complete", UriKind.Relative),
            content: null,
            TestToken);
        completed.EnsureSuccessStatusCode();

        var afterComplete = await LoadAggregateAsync(listId);

        afterComplete.LastModifiedAt.ShouldBe(completedAt);
        afterComplete.LastModifiedAt.ShouldNotBe(afterAdd.LastModifiedAt);
        afterComplete.LastModifiedBy.ShouldBe(session.UserId);
    }

    [Fact]
    public async Task TwoCallers_AreRecordedAsTwoDifferentCreators()
    {
        var (firstClient, _, firstSession) = await SignInAsync("first");
        var (secondClient, _, secondSession) = await SignInAsync("second");

        var firstList = await CreateTodoListAsync(firstClient, "First's list");
        var secondList = await CreateTodoListAsync(secondClient, "Second's list");

        firstSession.UserId.ShouldNotBe(secondSession.UserId);

        (await LoadAggregateAsync(firstList)).CreatedBy.ShouldBe(firstSession.UserId);
        (await LoadAggregateAsync(secondList)).CreatedBy.ShouldBe(secondSession.UserId);
    }

    [Fact]
    public async Task TheStampComesFromTheInjectedClock()
    {
        var (client, _, _) = await SignInAsync();

        Clock.Advance(TimeSpan.FromDays(3));
        var expected = Clock.UtcNow;

        var listId = await CreateTodoListAsync(client, "Three days from now");

        var stored = await LoadAggregateAsync(listId);

        stored.CreatedAt.ShouldBe(expected);

        stored.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddDays(2));
    }

    [Fact]
    public async Task TheAggregateAndTheRow_AgreeAboutTheStamps()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Audited");

        var aggregate = await LoadAggregateAsync(listId);

        int matches = await Database.CountAsync(
            $"""
            SELECT count(*) FROM todo."TodoLists"
            WHERE "Id" = '{listId}' AND "CreatedAt" = '{aggregate.CreatedAt:O}'
            """,
            TestToken);

        matches.ShouldBe(
            1,
            "the aggregate is carrying a CreatedAt the row does not have, which means the values the audit "
            + "interceptor wrote were never read back into the domain object.");
    }

    private async Task<TodoList> LoadAggregateAsync(Guid listId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        return await LoadTodoListAsync(scope.ServiceProvider, listId);
    }
}
