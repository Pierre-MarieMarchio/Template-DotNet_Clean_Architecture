using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

/// <summary>
/// The audit columns are stamped by the interceptor, and <c>CreatedBy</c> is the caller who actually
/// made the request — never <c>Guid.Empty</c>, which looks like a user id and is not one.
/// </summary>
/// <remarks>
/// The interceptor stamps the <em>row</em>, and the values then have to travel back into the
/// aggregate. Reading the stamps straight out of the row would pass even if the aggregate never
/// learned them, so these tests load through
/// <see cref="ITodoListRepository"/> — the same path a use case
/// takes — and assert on the aggregate.
/// </remarks>
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

    /// <summary>
    /// A change to a child is a change to its aggregate, so adding an item has to move the root's
    /// <c>LastModifiedAt</c> even though no column of the root itself changed. The flusher is what
    /// marks the root modified; nothing in EF's own change tracking would.
    /// </summary>
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

    /// <summary>
    /// Two different callers, two different <c>CreatedBy</c> values. Without this, "CreatedBy is the
    /// caller" would be satisfied by an interceptor that stamped any single constant.
    /// </summary>
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

    /// <summary>
    /// The clock the audit columns come from is the injected one, not the machine's. Moving it and
    /// seeing the stamp move with it is what proves that.
    /// </summary>
    [Fact]
    public async Task TheStampComesFromTheInjectedClock()
    {
        var (client, _, _) = await SignInAsync();

        Clock.Advance(TimeSpan.FromDays(3));
        var expected = Clock.UtcNow;

        var listId = await CreateTodoListAsync(client, "Three days from now");

        var stored = await LoadAggregateAsync(listId);

        stored.CreatedAt.ShouldBe(expected);

        // Which is not the machine clock: it is days ahead of it.
        stored.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddDays(2));
    }

    /// <summary>
    /// The row is the authority — the interceptor writes it — so this reads the columns directly and
    /// checks the aggregate agrees. If the read-back after a save were dropped, the aggregate would
    /// carry defaults while the row carried the truth, and every assertion above would still pass on the
    /// row alone.
    /// </summary>
    [Fact]
    public async Task TheAggregateAndTheRow_AgreeAboutTheStamps()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Audited");

        var aggregate = await LoadAggregateAsync(listId);

        // Compared in SQL rather than by parsing a rendered timestamp back into .NET: the comparison then
        // happens in PostgreSQL's own type, so the assertion cannot pass or fail on a formatting detail.
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
