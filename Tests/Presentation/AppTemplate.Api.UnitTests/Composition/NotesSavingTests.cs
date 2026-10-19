using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Ports.NoteQueries;
using AppTemplate.Application.Features.Notes.Ports.NoteStore;
using AppTemplate.Application.Features.Notes.Services;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Composition;

/// <summary>
/// A note commits through the unit of work alone, and a stale write is refused by the write itself.
/// </summary>
/// <remarks>
/// Real module composition, real context, real commits: see <see cref="SqliteSavingHost"/> for what
/// stands in for PostgreSQL. SQLite has no <c>xmin</c>, so the host makes it an integer column the
/// database does not move by itself; a test moves it with SQL to play the competing writer. The same
/// races run against PostgreSQL in <c>AppTemplate.Api.IntegrationTests</c>.
/// </remarks>
public sealed class NotesSavingTests
{
    private static readonly Guid _caller = Guid.CreateVersion7();
    private static readonly Guid _someoneElse = Guid.CreateVersion7();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>
    /// The adapters take the context and nothing else: no mapper to write a model onto rows, no tracker
    /// to flush, no event source to publish from. What commits the note is the unit of work.
    /// </summary>
    [Fact]
    public async Task TheNoteAdapters_DependOnTheContextAlone()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        await using var scope = host.RequestScope(_caller);

        foreach (var port in new[] { typeof(INoteStore), typeof(INoteQueries) })
        {
            var adapter = scope.ServiceProvider.GetRequiredService(port).GetType();

            adapter.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType)
                .ShouldBe([typeof(AppDbContext)], $"{adapter.Name} should reach storage through the context only");
        }
    }

    [Fact]
    public async Task CreatingANote_CommitsIt_ReadsItsVersionBack_AndPublishesNothing()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);

        Guid noteId;

        await using (var scope = host.RequestScope(_caller))
        {
            var created = await scope.ServiceProvider.GetRequiredService<INoteService>()
                .CreateAsync(new NoteContentDto("  Groceries ", "Milk"), Token);

            created.IsSuccess.ShouldBeTrue();
            created.Value.Version.ShouldBe(1u, "the token the store assigned on insert, read back");
            noteId = created.Value.Value.Id;
        }

        await using var check = host.RequestScope(_caller);
        var stored = await check.ServiceProvider.GetRequiredService<INoteService>().GetAsync(noteId, Token);

        stored.Value.Value.Title.ShouldBe("Groceries");
        stored.Value.Value.Body.ShouldBe("Milk");
        host.Published.All.ShouldBeEmpty("a note raises no domain event, and nothing is published for it");
    }

    /// <summary>
    /// The version the caller names matches when the write is staged, and the row moves before the
    /// commit. The <c>UPDATE</c> carries the version it loaded, finds no such row and is refused, so the
    /// competing writer's change survives. The refusal comes from the write, not from the earlier read.
    /// </summary>
    [Fact]
    public async Task AnUpdate_WhoseRowMovedBetweenTheReadAndTheWrite_IsRefusedAtTheWrite()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var noteId = await ANoteAsync(host, _caller);

        await using var scope = host.RequestScope(_caller);
        var services = scope.ServiceProvider;

        var status = await services.GetRequiredService<INoteStore>().StageReplacementAsync(
            UserId.Create(_caller),
            noteId,
            new NoteContentDto("Mine", "Mine"),
            DateTimeOffset.UtcNow,
            new VersionPrecondition([1u]),
            Token);

        status.ShouldBe(NoteChangeStatus.Staged, "the caller's version still matches when the write is staged");

        await host.ExecuteOnBusinessStoreAsync("UPDATE \"Notes\" SET \"Title\" = 'Theirs', \"xmin\" = \"xmin\" + 1", Token);

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token));

        await using var check = host.RequestScope(_caller);
        var survivor = await check.ServiceProvider.GetRequiredService<INoteService>().GetAsync(noteId, Token);

        survivor.Value.Value.Title.ShouldBe("Theirs", "the refused write left the competing change in place");
        survivor.Value.Version.ShouldBe(2u);
    }

    [Fact]
    public async Task ADelete_WhoseRowMovedBetweenTheReadAndTheWrite_IsRefusedAtTheWrite_AndTheNoteStays()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var noteId = await ANoteAsync(host, _caller);

        await using var scope = host.RequestScope(_caller);
        var services = scope.ServiceProvider;

        (await services.GetRequiredService<INoteStore>()
            .StageRemovalAsync(UserId.Create(_caller), noteId, new VersionPrecondition([1u]), Token))
            .ShouldBe(NoteChangeStatus.Staged);

        await host.ExecuteOnBusinessStoreAsync("UPDATE \"Notes\" SET \"xmin\" = \"xmin\" + 1", Token);

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token));

        await using var check = host.RequestScope(_caller);
        (await check.ServiceProvider.GetRequiredService<INoteService>().GetAsync(noteId, Token))
            .IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task AnUpdateNamingAStaleVersion_IsRefusedBeforeAnythingIsStaged_AndChangesNothing()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var noteId = await ANoteAsync(host, _caller);

        await using var scope = host.RequestScope(_caller);
        var service = scope.ServiceProvider.GetRequiredService<INoteService>();

        var result = await service.UpdateAsync(noteId, new NoteContentDto("Late", "Late"), new VersionPrecondition([99u]), Token);

        result.Error.ShouldBe(ConcurrencyErrors.PreconditionFailed);
        (await service.GetAsync(noteId, Token)).Value.Value.Title.ShouldBe("Groceries");
    }

    [Fact]
    public async Task AnUpdateOnTheCurrentVersion_Commits_AndAnswersTheNewVersion()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var noteId = await ANoteAsync(host, _caller);

        await using var scope = host.RequestScope(_caller);
        var service = scope.ServiceProvider.GetRequiredService<INoteService>();

        var result = await service.UpdateAsync(noteId, new NoteContentDto("Edited", "Edited"), new VersionPrecondition([1u]), Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.Title.ShouldBe("Edited");
        result.Value.Value.LastModifiedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// Somebody else's note is a note that does not exist: not found on every write, and the row is
    /// never touched.
    /// </summary>
    [Fact]
    public async Task AnotherUsersNote_CannotBeChangedOrDeleted_AndIsNotFound()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var noteId = await ANoteAsync(host, _caller);

        await using (var intruder = host.RequestScope(_someoneElse))
        {
            var service = intruder.ServiceProvider.GetRequiredService<INoteService>();

            (await service.GetAsync(noteId, Token)).Error!.Code.ShouldBe("note.notFound");
            (await service.UpdateAsync(noteId, new NoteContentDto("Mine now", "Mine now"), null, Token))
                .Error!.Code.ShouldBe("note.notFound");
            (await service.DeleteAsync(noteId, null, Token)).Error!.Code.ShouldBe("note.notFound");
        }

        await using var owner = host.RequestScope(_caller);
        (await owner.ServiceProvider.GetRequiredService<INoteService>().GetAsync(noteId, Token))
            .Value.Value.Title.ShouldBe("Groceries");
    }

    private static async Task<Guid> ANoteAsync(SqliteSavingHost host, Guid owner)
    {
        await using var scope = host.RequestScope(owner);

        var created = await scope.ServiceProvider.GetRequiredService<INoteService>()
            .CreateAsync(new NoteContentDto("Groceries", "Milk"), Token);

        return created.Value.Value.Id;
    }
}
