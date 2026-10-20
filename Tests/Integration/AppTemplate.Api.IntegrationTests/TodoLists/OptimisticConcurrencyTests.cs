using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

public sealed class OptimisticConcurrencyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const int _concurrentWriters = 8;

    private const int _burstAttempts = 5;

    [Fact]
    public async Task AStaleWriteToTheRoot_Fails()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Original");

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        var stale = await LoadTodoListAsync(scope.ServiceProvider, listId);
        uint versionWhenLoaded = stale.Version;

        versionWhenLoaded.ShouldNotBe(0u);

        using var renamed = await client.PutAsJsonAsync(
            $"{TodoListsRoute}/{listId}",
            new RenameTodoListRequest("Renamed by the winner"),
            TestToken);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        stale.Rename("Renamed by the loser");

        var conflict = await Should.ThrowAsync<ConcurrencyConflictException>(
            async () => await CommitAsync(scope.ServiceProvider));

        conflict.InnerException.ShouldBeOfType<DbUpdateConcurrencyException>();

        (await ReadNameAsync(client, listId)).ShouldBe("Renamed by the winner");

        await using var fresh = Fixture.Factory.Services.CreateAsyncScope();
        (await LoadTodoListAsync(fresh.ServiceProvider, listId)).Version.ShouldNotBe(versionWhenLoaded);
    }

    [Fact]
    public async Task TwoConcurrentModifications_AnswerA409ProblemDocument()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Original");

        var conflicts = new List<ProblemResponse>();

        for (int attempt = 1; attempt <= _burstAttempts && conflicts.Count == 0; attempt++)
        {
            conflicts.AddRange(await RaceRenamesAsync(client, listId, attempt));
        }

        conflicts.ShouldNotBeEmpty(
            $"{_burstAttempts} bursts of {_concurrentWriters} simultaneous writers against one aggregate "
            + "never produced a single conflict. Either the writes are being serialised somewhere, or the "
            + "concurrency token has stopped being checked — and this test would then be passing without "
            + "exercising anything.");

        foreach (var problem in conflicts)
        {
            problem.Status.ShouldBe((int)HttpStatusCode.Conflict, problem.Body);
            problem.Title.ShouldBe("Conflict", problem.Body);
            problem.Detail.ShouldNotBeNullOrWhiteSpace(problem.Body);
            problem.Code.ShouldBe(
                "concurrency.conflict",
                "clients branch on the code, never on the prose: a lost update has to be distinguishable "
                + "from every other 409. " + problem.Body);
        }

        string stored = await ReadNameAsync(client, listId);
        stored.ShouldStartWith("Writer ");
    }

    private static async Task<List<ProblemResponse>> RaceRenamesAsync(HttpClient client, Guid listId, int attempt)
    {
        var sends = Enumerable.Range(0, _concurrentWriters)
            .Select(writer => client.PutAsJsonAsync(
                $"{TodoListsRoute}/{listId}",
                new RenameTodoListRequest($"Writer {attempt}.{writer}"),
                TestToken))
            .ToList();

        var responses = await Task.WhenAll(sends);
        var conflicts = new List<ProblemResponse>();

        try
        {
            foreach (var response in responses)
            {
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    response.Content.Headers.ContentType?.MediaType.ShouldBe(
                        "application/problem+json",
                        "every failure in this API is RFC 7807, and the most common one must not be the "
                        + "exception.");

                    conflicts.Add(await ApiJson.ReadProblemAsync(response, TestToken));
                    continue;
                }

                response.StatusCode.ShouldBe(
                    HttpStatusCode.OK,
                    "a concurrent write either wins (200) or is refused (409). Any other answer — a 500 "
                    + "most of all — means the conflict escaped untranslated: "
                    + await response.Content.ReadAsStringAsync(TestToken));
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        return conflicts;
    }

    [Fact]
    public async Task AChangeToAChild_MakesAStaleWriteToTheRootFailToo()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Original");

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var stale = await LoadTodoListAsync(scope.ServiceProvider, listId);

        await AddTodoItemAsync(client, listId, "Added by the winner");

        stale.Rename("Renamed by the loser");

        await Should.ThrowAsync<ConcurrencyConflictException>(
            async () => await CommitAsync(scope.ServiceProvider));

        var detail = await ReadDetailAsync(client, listId);
        detail.Name.ShouldBe("Original");
        detail.Items.Single().Title.ShouldBe("Added by the winner");
    }

    [Fact]
    public async Task AStaleAddOfAnItem_Fails()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Original");

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var stale = await LoadTodoListAsync(scope.ServiceProvider, listId);

        await AddTodoItemAsync(client, listId, "First");

        stale.AddItem("Second", null);

        await Should.ThrowAsync<ConcurrencyConflictException>(
            async () => await CommitAsync(scope.ServiceProvider));

        (await ReadDetailAsync(client, listId)).Items
            .Select(item => item.Title)
            .ShouldBe(["First"]);
    }

    [Fact]
    public async Task SequentialWrites_AllSucceedAndTheVersionAdvancesEachTime()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Original");

        var versions = new List<uint>();

        for (int round = 1; round <= 3; round++)
        {
            using var renamed = await client.PutAsJsonAsync(
                $"{TodoListsRoute}/{listId}",
                new RenameTodoListRequest($"Round {round}"),
                TestToken);

            renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

            await using var scope = Fixture.Factory.Services.CreateAsyncScope();
            versions.Add((await LoadTodoListAsync(scope.ServiceProvider, listId)).Version);
        }

        versions.Distinct().Count().ShouldBe(3);
        (await ReadNameAsync(client, listId)).ShouldBe("Round 3");
    }

    [Fact]
    public async Task TwoWritesInOneScope_BothSucceed()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Original");

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var aggregate = await LoadTodoListAsync(scope.ServiceProvider, listId);
        uint versionWhenLoaded = aggregate.Version;

        aggregate.Rename("First write");
        await CommitAsync(scope.ServiceProvider);

        aggregate.Version.ShouldNotBe(
            versionWhenLoaded,
            "the aggregate has to be told the version its own write produced, or the next one fails "
            + "against a token it moved itself.");

        aggregate.Rename("Second write");
        await CommitAsync(scope.ServiceProvider);

        (await ReadNameAsync(client, listId)).ShouldBe("Second write");
    }

    [Fact]
    public async Task TwoClientsHoldingTheSameETag_CannotBothWin()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Original");

        string firstClientsETag = await ReadETagAsync(client, listId);
        string secondClientsETag = await ReadETagAsync(client, listId);

        secondClientsETag.ShouldBe(
            firstClientsETag,
            "two reads with nothing in between must describe the same state, or this test is not "
            + "about two clients holding one version.");

        using var first = await RenameAsync(client, listId, "Renamed by the first client", firstClientsETag);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var second = await RenameAsync(client, listId, "Renamed by the second client", secondClientsETag);

        second.StatusCode.ShouldBe(
            HttpStatusCode.PreconditionFailed,
            "the second client's change was decided against a version that no longer exists. "
            + "Accepting it is the lost update: " + await second.Content.ReadAsStringAsync(TestToken));

        var problem = await ApiJson.ReadProblemAsync(second, TestToken);
        problem.Code.ShouldBe("precondition.failed", problem.Body);

        (await ReadNameAsync(client, listId)).ShouldBe("Renamed by the first client");

        using var retried = await RenameAsync(
            client,
            listId,
            "Renamed by the second client",
            await ReadETagAsync(client, listId));

        retried.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadNameAsync(client, listId)).ShouldBe("Renamed by the second client");
    }

    private static async Task CommitAsync(IServiceProvider services) =>
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestToken);

    private static async Task<string> ReadNameAsync(HttpClient client, Guid listId) =>
        (await ReadDetailAsync(client, listId)).Name;

    private static async Task<TodoListResponse> ReadDetailAsync(HttpClient client, Guid listId)
    {
        using var response = await client.GetAsync(
            new Uri($"{TodoListsRoute}/{listId}", UriKind.Relative),
            TestToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return await ApiJson.ReadAsync<TodoListResponse>(response, TestToken);
    }
}
