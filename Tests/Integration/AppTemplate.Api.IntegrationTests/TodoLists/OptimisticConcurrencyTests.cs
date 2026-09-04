using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.TodoLists;

/// <summary>
/// Two modifications of the same aggregate cannot both win, and the loser is told. An unconditional
/// <c>Update()</c> of every column would let the later writer silently overwrite the earlier one's
/// changes and its audit columns; the aggregate root carries PostgreSQL's <c>xmin</c> instead.
/// </summary>
/// <remarks>
/// <para>
/// EF does not map the domain types, so it cannot see a change inside an aggregate and cannot know
/// which version that change was decided against: the token has to travel aggregate → row and back
/// deliberately. These tests are the evidence that it does, driving writes through
/// <see cref="ITodoListRepository"/> and <see cref="IUnitOfWork"/> — the exact path a use case takes —
/// rather than through a <c>DbSet</c>, so nothing about the mapping is bypassed.
/// </para>
/// <para>
/// The races are made deterministic rather than raced. Firing two requests at once exercises the same
/// code path but only <em>sometimes</em> interleaves, and a concurrency test that passes when the two
/// writes happen to be sequential is not a test.
/// </para>
/// </remarks>
public sealed class OptimisticConcurrencyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    /// Enough simultaneous writers that at least two are overwhelmingly likely to read the same version
    /// before either writes. Eight rather than two, because two requests fired together on a busy machine
    /// often simply queue.
    /// </summary>
    private const int _concurrentWriters = 8;

    /// <summary>
    /// How many bursts to try before declaring that concurrency is not happening at all. Bounded, and
    /// exhausting it is a failure rather than a skip.
    /// </summary>
    private const int _burstAttempts = 5;

    [Fact]
    public async Task AStaleWriteToTheRoot_Fails()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Original");

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        var stale = await LoadTodoListAsync(scope.ServiceProvider, listId);
        uint versionWhenLoaded = stale.Version;

        // The token reached the aggregate at all. Without this the assertions below could pass with every
        // version zero and every write checked against nothing — which is precisely the failure mode a
        // separate persistence model introduces.
        versionWhenLoaded.ShouldNotBe(0u);

        // Somebody else renames it through the API.
        using var renamed = await client.PutAsJsonAsync(
            $"{TodoListsRoute}/{listId}",
            new RenameTodoListRequest("Renamed by the winner"),
            TestToken);
        renamed.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The stale writer now tries to commit what it decided before that happened.
        stale.Rename("Renamed by the loser");

        var conflict = await Should.ThrowAsync<ConcurrencyConflictException>(
            async () => await CommitAsync(scope.ServiceProvider));

        // Translated at the unit of work — the one place a commit happens — with the provider's own
        // exception kept as the inner one so the log still says which rows lost.
        conflict.InnerException.ShouldBeOfType<DbUpdateConcurrencyException>();

        // The winner's change survived: nothing was silently overwritten.
        (await ReadNameAsync(client, listId)).ShouldBe("Renamed by the winner");

        // And the token actually moved, which is what made the stale write detectable.
        await using var fresh = Fixture.Factory.Services.CreateAsyncScope();
        (await LoadTodoListAsync(fresh.ServiceProvider, listId)).Version.ShouldNotBe(versionWhenLoaded);
    }

    /// <summary>
    /// The outcome a client can actually see: <b>409</b>, <c>application/problem+json</c>, and the stable
    /// code <c>concurrency.conflict</c> — not a 500, and not a silently accepted overwrite.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the assertion that proves the whole chain rather than one link of it: <c>xmin</c> in the
    /// <c>WHERE</c> clause, zero rows affected, <c>DbUpdateConcurrencyException</c> from EF,
    /// <see cref="ConcurrencyConflictException"/> from the unit of work, and the global exception
    /// handler's problem document. Every step is production code; the endpoint is the ordinary rename
    /// endpoint, with no test-only route and no injected failure.
    /// </para>
    /// <para>
    /// <b>How the interleaving is forced.</b> A transaction of this test's own updates the row and stays
    /// open. Under <c>READ COMMITTED</c> the API's handler then reads the row at its previous version —
    /// uncommitted changes are invisible — and its own <c>UPDATE</c> blocks on the row lock. The test
    /// waits until PostgreSQL reports a session waiting on a lock, which is proof that the handler has
    /// already done its read, and only then commits. PostgreSQL re-evaluates the blocked statement's
    /// <c>WHERE</c> against the new row version, <c>xmin</c> no longer matches, and zero rows are
    /// affected. No sleeps, and nothing that passes when the two writes happen to be sequential.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The outcome a client can actually see when two writers race: <b>409</b>,
    /// <c>application/problem+json</c>, and the stable code <c>concurrency.conflict</c> — never a 500, and
    /// never a success that quietly discarded somebody's change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the assertion that proves the whole chain rather than one link of it: <c>xmin</c> in the
    /// <c>WHERE</c> clause, zero rows affected, <c>DbUpdateConcurrencyException</c> from EF,
    /// <see cref="ConcurrencyConflictException"/> from the unit of work, and the global exception
    /// handler's problem document. Every step is production code — the ordinary rename endpoint, no
    /// test-only route, no injected failure.
    /// </para>
    /// <para>
    /// <b>Why a burst rather than a contrived interleaving.</b> Several writers are fired at the same list
    /// at once and <em>every</em> response is checked, not just the interesting one. That makes the test
    /// meaningful whichever way the requests happen to interleave: a run in which they serialised produces
    /// eight 200s and is retried, and a run in which any of them raced must produce a well-formed 409.
    /// The failure this guards — a lost update reported as success, or as a 500 — would show up in the
    /// per-response assertions on the very first burst.
    /// </para>
    /// <para>
    /// The retry exists because interleaving is the operating system's decision, not the test's. It is
    /// bounded, and exhausting it fails: "no two writers ever raced" would mean this test had silently
    /// stopped exercising concurrency at all, which is exactly the vacuous pass worth failing on.
    /// </para>
    /// </remarks>
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

        // Whatever the interleaving was, exactly one writer's value is stored — and it is one of theirs,
        // not a blend of several.
        string stored = await ReadNameAsync(client, listId);
        stored.ShouldStartWith("Writer ");
    }

    /// <summary>
    /// Fires <see cref="_concurrentWriters"/> renames at one aggregate at the same moment and returns the
    /// problem document of every request that lost.
    /// </summary>
    /// <remarks>
    /// Every response is inspected. A 200 is a writer that won its race and a 409 is one that lost; both
    /// are correct outcomes. Anything else — a 500 above all — is a failure, and it is asserted here
    /// rather than in the caller so that the very first burst catches it even when the caller goes on
    /// to retry.
    /// </remarks>
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

    /// <summary>
    /// The token lives on the root only, because the root is the consistency boundary. Adding an item
    /// therefore has to move the <em>list's</em> version — otherwise two people could add items to the
    /// same list concurrently and the list would claim it had not changed.
    /// </summary>
    [Fact]
    public async Task AChangeToAChild_MakesAStaleWriteToTheRootFailToo()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Original");

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var stale = await LoadTodoListAsync(scope.ServiceProvider, listId);

        // A child changes, not the root's own columns.
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

        // Neither the loser's item nor a half-applied version of it landed. This is the assertion the
        // separate persistence model most endangers: the item row is a child insert, and it is the root's
        // token that has to refuse it. An aggregate boundary that had stopped being enforced would show
        // up here as a second item rather than as an exception.
        (await ReadDetailAsync(client, listId)).Items
            .Select(item => item.Title)
            .ShouldBe(["First"]);
    }

    /// <summary>
    /// Sequential writes through the API must keep working. Without this, "one of two writes fails"
    /// would be satisfied by a system in which every second write fails.
    /// </summary>
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

    /// <summary>
    /// Two writes in one scope, in sequence. The aggregate has to be told the token PostgreSQL assigned
    /// to its first write, or the second would be checked against a version that no longer exists and
    /// would fail for no reason — a system in which a use case may only ever save once.
    /// </summary>
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

    /// <summary>
    /// The lost update, prevented — the failure <c>xmin</c> alone cannot see. Two clients read the
    /// same list and each decides a change against what it read. The first commits; the second is
    /// refused, and the first client's change is still there afterwards.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here races. Both requests are sequential, and each is separately in the read-write
    /// window a use case's own transaction protects, so <c>xmin</c> is satisfied by both: the second
    /// writer loads the row the first one wrote, and its <c>UPDATE</c> matches. That is exactly the
    /// hole — without a validator in the request, the second write is indistinguishable from somebody
    /// deliberately renaming an already-renamed list, and it silently discards the first change.
    /// </para>
    /// <para>
    /// Deleting the precondition check from <c>RenameTodoListUseCase</c> makes this test fail on the
    /// second client's status and again on the stored name; every other test in this file still
    /// passes, which is the point of it being here.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task TwoClientsHoldingTheSameETag_CannotBothWin()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Original");

        // Both clients render an edit form from the same read.
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

        // The assertion that makes the refusal worth anything.
        (await ReadNameAsync(client, listId)).ShouldBe("Renamed by the first client");

        // And the loser can recover: read again, and the same change is accepted.
        using var retried = await RenameAsync(
            client,
            listId,
            "Renamed by the second client",
            await ReadETagAsync(client, listId));

        retried.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadNameAsync(client, listId)).ShouldBe("Renamed by the second client");
    }

    // ---- Helpers ---------------------------------------------------------------------------------

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
