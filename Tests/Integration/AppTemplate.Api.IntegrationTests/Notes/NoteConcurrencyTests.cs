using System.Net;
using System.Net.Http.Headers;
using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Notes.Dtos;
using AppTemplate.Application.Features.Notes.Ports.NoteQueries;
using AppTemplate.Application.Features.Notes.Ports.NoteStore;
using AppTemplate.Domain.Core.Common.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Notes;

/// <summary>
/// A stale write to a note is refused, and refused by the database: the <c>UPDATE</c> and the
/// <c>DELETE</c> carry the <c>xmin</c> the row was loaded at, so a writer that lost a race cannot
/// overwrite or delete what the winner committed.
/// </summary>
/// <remarks>
/// Needs the PostgreSQL fixture. The same scenarios run against SQLite, with <c>xmin</c> played by an
/// integer column, in <c>AppTemplate.Api.UnitTests</c>; only here is the token the real system column.
/// </remarks>
public sealed class NoteConcurrencyTests(ApiFixture fixture) : NotesTestBase(fixture)
{
    /// <summary>Enough simultaneous writers that several are overwhelmingly likely to read the same version.</summary>
    private const int _concurrentWriters = 8;

    [Fact]
    public async Task AnUpdateNamingAStaleVersion_Answers412_AndChangesNothing()
    {
        var (client, _, _) = await SignInAsync();
        var note = await CreateNoteAsync(client, "Original");

        using var winner = await UpdateNoteAsync(client, note.Note.Id, "Winner", "Winner", note.ETag);
        winner.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var stale = await UpdateNoteAsync(client, note.Note.Id, "Loser", "Loser", note.ETag);

        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ApiJson.ReadProblemAsync(stale, TestToken)).Code.ShouldBe("precondition.failed");
        (await ReadNoteAsync(client, note.Note.Id)).Note.Title.ShouldBe("Winner");
    }

    [Fact]
    public async Task ADeleteNamingAStaleVersion_Answers412_AndTheNoteStays()
    {
        var (client, _, _) = await SignInAsync();
        var note = await CreateNoteAsync(client, "Original");

        using var winner = await UpdateNoteAsync(client, note.Note.Id, "Winner", "Winner", note.ETag);
        winner.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var stale = await DeleteNoteAsync(client, note.Note.Id, note.ETag);

        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ReadNoteAsync(client, note.Note.Id)).Note.Title.ShouldBe("Winner");
    }

    [Fact]
    public async Task IfMatchStar_AssertsExistence_AndFailsWith412ForANoteThatIsGone()
    {
        var (client, _, _) = await SignInAsync();
        var note = await CreateNoteAsync(client);

        using var present = await UpdateNoteAsync(client, note.Note.Id, "Still there", "Body", "*");
        present.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var gone = await UpdateNoteAsync(client, Guid.CreateVersion7(), "Nothing", "Body", "*");
        gone.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
    }

    [Fact]
    public async Task AMalformedIfMatch_Answers400()
    {
        var (client, _, _) = await SignInAsync();
        var note = await CreateNoteAsync(client);

        using var response = await UpdateNoteAsync(client, note.Note.Id, "Title", "Body", "not-a-quoted-tag");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("precondition.malformed");
    }

    [Fact]
    public async Task AnUpdateWithoutIfMatch_IsAccepted_WhereThePolicyIsOptional()
    {
        var (client, _, _) = await SignInAsync();
        var note = await CreateNoteAsync(client);

        using var response = await UpdateNoteAsync(client, note.Note.Id, "Unconditional");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WherePreconditionsAreRequired_AnUnconditionalWriteIsRefusedWith428_AndAConditionalOneSucceeds()
    {
        var (client, _, session) = await SignInAsync();
        var note = await CreateNoteAsync(client);

        using var host = Fixture.Factory.WithWebHostBuilder(builder => builder.UseSetting(
            $"{ConcurrencyOptions.SectionName}:{nameof(ConcurrencyOptions.IfMatch)}",
            nameof(IfMatchRequirement.Required)));
        using var strict = host.CreateClient();
        strict.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Tokens.AccessToken);
        strict.DefaultRequestHeaders.Add(TestClientAddressStartupFilter.HeaderName, "10.250.0.2");

        using var unconditional = await UpdateNoteAsync(strict, note.Note.Id, "Unconditional");
        unconditional.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);
        (await ApiJson.ReadProblemAsync(unconditional, TestToken)).Code.ShouldBe("precondition.required");

        using var unconditionalDelete = await DeleteNoteAsync(strict, note.Note.Id);
        unconditionalDelete.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired);

        using var conditional = await UpdateNoteAsync(strict, note.Note.Id, "Conditional", "Body", note.ETag);
        conditional.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>
    /// The competing write, with no luck involved: the loser loads the note and stages its change at a
    /// version that still matches <c>If-Match</c>, the winner then commits through the API, and the loser
    /// commits last. The check made at load time passes; the write is the one refused, because its
    /// <c>WHERE</c> names the version it loaded and that row no longer exists.
    /// </summary>
    [Fact]
    public async Task AWriteStagedAtTheCurrentVersion_IsRefusedAtTheCommit_WhenAnotherWriterCommitsFirst()
    {
        var (client, _, session) = await SignInAsync();
        var note = await CreateNoteAsync(client, "Original");
        var owner = UserId.Create(session.UserId);

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<INoteStore>();

        var staged = await store.StageReplacementAsync(
            owner,
            note.Note.Id,
            new NoteContentDto("Loser", "Loser"),
            DateTimeOffset.UtcNow,
            new VersionPrecondition([await VersionOfAsync(scope.ServiceProvider, owner, note.Note.Id)]),
            TestToken);

        staged.ShouldBe(NoteChangeStatus.Staged, "the version still matches when the loser stages its write");

        using var winner = await UpdateNoteAsync(client, note.Note.Id, "Winner", "Winner", note.ETag);
        winner.StatusCode.ShouldBe(HttpStatusCode.OK);

        var conflict = await Should.ThrowAsync<ConcurrencyConflictException>(
            () => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestToken));

        conflict.InnerException.ShouldBeOfType<DbUpdateConcurrencyException>();
        (await ReadNoteAsync(client, note.Note.Id)).Note.Title.ShouldBe("Winner", "nothing was overwritten");
    }

    [Fact]
    public async Task ADeleteStagedAtTheCurrentVersion_IsRefusedAtTheCommit_WhenAnotherWriterCommitsFirst()
    {
        var (client, _, session) = await SignInAsync();
        var note = await CreateNoteAsync(client, "Original");

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<INoteStore>().StageRemovalAsync(
                UserId.Create(session.UserId),
                note.Note.Id,
                new VersionPrecondition([await VersionOfAsync(scope.ServiceProvider, UserId.Create(session.UserId), note.Note.Id)]),
                TestToken))
            .ShouldBe(NoteChangeStatus.Staged);

        using var winner = await UpdateNoteAsync(client, note.Note.Id, "Winner", "Winner", note.ETag);
        winner.StatusCode.ShouldBe(HttpStatusCode.OK);

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestToken));

        (await ReadNoteAsync(client, note.Note.Id)).Note.Title.ShouldBe("Winner", "the note was not deleted");
    }

    /// <summary>
    /// Several writers name the same version at the same moment. Exactly one can win; every other is
    /// refused with a well-formed problem document, 412 when it read the new version and 409 when it lost
    /// the race at the write itself, and never a 500 or a second success.
    /// </summary>
    [Fact]
    public async Task SimultaneousWritersNamingOneVersion_LetExactlyOneWin()
    {
        var (client, _, _) = await SignInAsync();
        var note = await CreateNoteAsync(client, "Original");

        var responses = await Task.WhenAll(Enumerable.Range(0, _concurrentWriters)
            .Select(writer => UpdateNoteAsync(client, note.Note.Id, $"Writer {writer}", "Body", note.ETag)));

        try
        {
            responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);

            foreach (var loser in responses.Where(response => response.StatusCode != HttpStatusCode.OK))
            {
                loser.StatusCode.ShouldBeOneOf(HttpStatusCode.PreconditionFailed, HttpStatusCode.Conflict);

                var problem = await ApiJson.ReadProblemAsync(loser, TestToken);

                problem.Code.ShouldBe(
                    loser.StatusCode == HttpStatusCode.Conflict ? "concurrency.conflict" : "precondition.failed",
                    problem.Body);
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        (await ReadNoteAsync(client, note.Note.Id)).Note.Title.ShouldStartWith("Writer ");
    }

    /// <summary>
    /// The version as storage holds it, read through the application's own read port: the ETag a client
    /// holds is opaque, and the competing-write tests work below the API, where the version is a number.
    /// </summary>
    private static async Task<uint> VersionOfAsync(IServiceProvider services, UserId owner, Guid noteId) =>
        (await services.GetRequiredService<INoteQueries>().GetAsync(owner, noteId, TestToken))!.Version;
}
