using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Model.Errors;
using AppTemplate.Application.Features.Notes.Services;
using AppTemplate.PresentationAppMcp.Features.Notes.Tools;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppMcp.UnitTests.Features.Notes.Tools;

public sealed class NoteToolsTests
{
    private static readonly NoteDto _note = new(Guid.CreateVersion7(), "Groceries", "Milk", DateTimeOffset.UnixEpoch, null);

    private readonly INoteService _notes = Substitute.For<INoteService>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListNotes_AsksForThePageItWasGiven()
    {
        _notes.ListAsync(Arg.Any<NoteListQueryDto>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(PagedResult.Offset<NoteSummaryDto>([], 2, 5, 0)));

        var answer = await new NoteTools(_notes).ListNotesAsync(2, 5, Cancellation);

        answer.IsError.ShouldBeNull();
        await _notes.Received(1).ListAsync(NoteListQueryDto.Offset(2, 5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetNote_AnswersTheNote_OrTheProblemOfAnAbsentOne()
    {
        var absent = Guid.CreateVersion7();
        _notes.GetAsync(_note.Id, Arg.Any<CancellationToken>()).Returns(Result.Success(new Versioned<NoteDto>(_note, 3)));
        _notes.GetAsync(absent, Arg.Any<CancellationToken>()).Returns(Result.Failure<Versioned<NoteDto>>(NoteErrors.NoteNotFound(absent)));
        var tools = new NoteTools(_notes);

        var found = await tools.GetNoteAsync(_note.Id, Cancellation);
        var missing = await tools.GetNoteAsync(absent, Cancellation);

        found.StructuredContent!.Value.GetProperty("body").GetString().ShouldBe("Milk");
        missing.IsError.ShouldBe(true);
        missing.StructuredContent!.Value.GetProperty("code").GetString().ShouldBe(NoteErrors.NoteNotFound(absent).Code);
    }

    [Fact]
    public async Task CreateNote_CreatesTheNoteItWasGiven()
    {
        _notes.CreateAsync(Arg.Any<NoteContentDto>(), Arg.Any<CancellationToken>()).Returns(Result.Success(new Versioned<NoteDto>(_note, 1)));

        var answer = await new NoteTools(_notes).CreateNoteAsync("Groceries", "Milk", Cancellation);

        answer.StructuredContent!.Value.GetProperty("id").GetGuid().ShouldBe(_note.Id);
        await _notes.Received(1).CreateAsync(new NoteContentDto("Groceries", "Milk"), Arg.Any<CancellationToken>());
    }
}
