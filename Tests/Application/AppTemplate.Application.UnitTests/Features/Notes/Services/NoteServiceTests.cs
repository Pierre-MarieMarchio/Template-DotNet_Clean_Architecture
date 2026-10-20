using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Notes.Model.Dtos;
using AppTemplate.Application.Features.Notes.Ports.NoteQueries;
using AppTemplate.Application.Features.Notes.Ports.NoteStore;
using AppTemplate.Application.Features.Notes.Services;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Notes.Services;

public sealed class NoteServiceTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly INoteQueries _queries = Substitute.For<INoteQueries>();
    private readonly INoteStore _store = Substitute.For<INoteStore>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private static NoteContentDto AValidContent => new("Groceries", "Milk, eggs");

    [Fact]
    public async Task AnAnonymousCaller_IsRefusedByEveryOperation()
    {
        var service = ServiceFor(StubCurrentUser.Anonymous);
        var noteId = Guid.CreateVersion7();

        Result[] results =
        [
            await service.ListAsync(NoteListQueryDto.Offset(null, null), TestToken),
            await service.GetAsync(noteId, TestToken),
            await service.CreateAsync(AValidContent, TestToken),
            await service.UpdateAsync(noteId, AValidContent, null, TestToken),
            await service.DeleteAsync(noteId, null, TestToken),
        ];

        results.ShouldAllBe(result => result.IsFailure);
        results.ShouldAllBe(result => result.Error!.Type == ErrorType.Unauthorized && result.Error.Code == "auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_ReadsAndWritesNothing()
    {
        var service = ServiceFor(StubCurrentUser.Anonymous);
        var noteId = Guid.CreateVersion7();

        await service.ListAsync(NoteListQueryDto.Offset(null, null), TestToken);
        await service.GetAsync(noteId, TestToken);
        await service.CreateAsync(AValidContent, TestToken);
        await service.UpdateAsync(noteId, AValidContent, null, TestToken);
        await service.DeleteAsync(noteId, null, TestToken);

        _queries.ReceivedCalls().ShouldBeEmpty();
        _store.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_AnswersTheNoteAndItsVersion_ReadForTheCallerOnly()
    {
        var note = ANote();
        _queries.GetAsync(_callerId, note.Id, Arg.Any<CancellationToken>())
            .Returns(new Versioned<NoteDto>(note, 7));

        var result = await Service().GetAsync(note.Id, TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(note);
        result.Value.Version.ShouldBe(7u);
    }

    [Fact]
    public async Task AForeignNote_IsIndistinguishableFromAMissingOne()
    {
        var foreignId = Guid.CreateVersion7();
        var missingId = Guid.CreateVersion7();
        _queries.GetAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Versioned<NoteDto>?)null);

        var foreign = await Service().GetAsync(foreignId, TestToken);
        var missing = await Service().GetAsync(missingId, TestToken);

        foreign.Error!.Code.ShouldBe("note.notFound");
        foreign.Error.Type.ShouldBe(missing.Error!.Type);
        foreign.Error.Code.ShouldBe(missing.Error.Code);
        await _queries.Received(2).GetAsync(_callerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListAsync_ScopesThePageToTheCaller_AndWritesNothing()
    {
        _queries.GetForOwnerAsync(Arg.Any<UserId>(), Arg.Any<FeaturePageRequest<NoteFilter>>(), Arg.Any<CancellationToken>())
            .Returns(PagedResult.Offset<NoteSummaryDto>([], 1, 20, 0));

        var result = await Service().ListAsync(NoteListQueryDto.Offset(null, null), TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _queries.Received(1).GetForOwnerAsync(
            _callerId,
            Arg.Any<FeaturePageRequest<NoteFilter>>(),
            Arg.Any<CancellationToken>());
        _store.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("body:asc", "sort.invalid")]
    [InlineData("title:asc,createdAt:asc,title:desc", "sort.invalid")]
    public async Task ListAsync_RefusesASortOutsideTheWhitelist(string sort, string code)
    {
        var result = await Service().ListAsync(new NoteListQueryDto(null, null, null, null, sort), TestToken);

        result.Error!.Code.ShouldBe(code);
        _queries.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task ListAsync_RefusesAPageSizeAboveTheCeiling()
    {
        var result = await Service().ListAsync(NoteListQueryDto.Offset(1, 101), TestToken);

        result.Error!.Code.ShouldBe("paging.invalid");
    }

    [Fact]
    public async Task CreateAsync_StagesTheNoteForTheCaller_CommitsOnce_AndAnswersWhatWasStored()
    {
        Guid? stagedId = null;
        _store.When(store => store.Add(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<NoteContentDto>(), Arg.Any<DateTimeOffset>()))
            .Do(call => stagedId = call.ArgAt<Guid>(1));
        _queries.GetAsync(_callerId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => new Versioned<NoteDto>(
                new NoteDto(call.ArgAt<Guid>(1), "Groceries", "Milk, eggs", StubDateTimeProvider.DefaultInstant, null),
                1));

        var result = await Service().CreateAsync(new NoteContentDto("  Groceries  ", "Milk, eggs"), TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.Id.ShouldBe(stagedId!.Value);
        result.Value.Version.ShouldBe(1u);
        _store.Received(1).Add(
            _callerId,
            stagedId.Value,
            new NoteContentDto("Groceries", "Milk, eggs"),
            StubDateTimeProvider.DefaultInstant);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "body")]
    [InlineData("   ", "body")]
    [InlineData(null, "body")]
    [InlineData("title", null)]
    public async Task CreateAsync_RejectsInvalidContent_AndStagesNothing(string? title, string? body)
    {
        var result = await Service().CreateAsync(new NoteContentDto(title!, body!), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        _store.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_StagesTheReplacementForTheCaller_CommitsOnce_AndAnswersTheNewVersion()
    {
        var noteId = Guid.CreateVersion7();
        var precondition = new VersionPrecondition([3]);
        _store.StageReplacementAsync(_callerId, noteId, Arg.Any<NoteContentDto>(), Arg.Any<DateTimeOffset>(), precondition, Arg.Any<CancellationToken>())
            .Returns(NoteChangeStatus.Staged);
        _queries.GetAsync(_callerId, noteId, Arg.Any<CancellationToken>())
            .Returns(new Versioned<NoteDto>(ANote(noteId), 4));

        var result = await Service().UpdateAsync(noteId, new NoteContentDto(" Title ", "Body"), precondition, TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(4u);
        await _store.Received(1).StageReplacementAsync(
            _callerId,
            noteId,
            new NoteContentDto("Title", "Body"),
            StubDateTimeProvider.DefaultInstant,
            precondition,
            Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_OfAnUnknownOrForeignNote_IsNotFound_AndCommitsNothing()
    {
        var noteId = Guid.CreateVersion7();
        _store.StageReplacementAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<NoteContentDto>(), Arg.Any<DateTimeOffset>(), Arg.Any<VersionPrecondition?>(), Arg.Any<CancellationToken>())
            .Returns(NoteChangeStatus.NotFound);

        var result = await Service().UpdateAsync(noteId, AValidContent, new VersionPrecondition([1]), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("note.notFound");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_AtAVersionTheNoteNoLongerHolds_IsPreconditionFailed_AndCommitsNothing()
    {
        _store.StageReplacementAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<NoteContentDto>(), Arg.Any<DateTimeOffset>(), Arg.Any<VersionPrecondition?>(), Arg.Any<CancellationToken>())
            .Returns(NoteChangeStatus.VersionMismatch);

        var result = await Service().UpdateAsync(Guid.CreateVersion7(), AValidContent, new VersionPrecondition([1]), TestToken);

        result.Error.ShouldBe(ConcurrencyErrors.PreconditionFailed);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAsync_RejectsInvalidContent_BeforeTouchingStorage()
    {
        var result = await Service().UpdateAsync(Guid.CreateVersion7(), new NoteContentDto("", "body"), null, TestToken);

        result.Error!.Code.ShouldBe("request.validationFailed");
        _store.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_ThatLosesTheRaceAtTheCommit_LetsTheConflictThrough_WithoutRetrying()
    {
        _store.StageReplacementAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<NoteContentDto>(), Arg.Any<DateTimeOffset>(), Arg.Any<VersionPrecondition?>(), Arg.Any<CancellationToken>())
            .Returns(NoteChangeStatus.Staged);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new ConcurrencyConflictException("lost"));

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => Service().UpdateAsync(Guid.CreateVersion7(), AValidContent, null, TestToken));

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_StagesTheRemovalForTheCaller_AndCommitsOnce()
    {
        var noteId = Guid.CreateVersion7();
        var precondition = new VersionPrecondition([3]);
        _store.StageRemovalAsync(_callerId, noteId, precondition, Arg.Any<CancellationToken>())
            .Returns(NoteChangeStatus.Staged);

        var result = await Service().DeleteAsync(noteId, precondition, TestToken);

        result.IsSuccess.ShouldBeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(NoteChangeStatus.NotFound, "note.notFound")]
    [InlineData(NoteChangeStatus.VersionMismatch, "precondition.failed")]
    public async Task DeleteAsync_ThatCannotBeStaged_FailsAndCommitsNothing(NoteChangeStatus status, string code)
    {
        _store.StageRemovalAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<VersionPrecondition?>(), Arg.Any<CancellationToken>())
            .Returns(status);

        var result = await Service().DeleteAsync(Guid.CreateVersion7(), null, TestToken);

        result.Error!.Code.ShouldBe(code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_ThatLosesTheRaceAtTheCommit_LetsTheConflictThrough()
    {
        _store.StageRemovalAsync(Arg.Any<UserId>(), Arg.Any<Guid>(), Arg.Any<VersionPrecondition?>(), Arg.Any<CancellationToken>())
            .Returns(NoteChangeStatus.Staged);
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new ConcurrencyConflictException("lost"));

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => Service().DeleteAsync(Guid.CreateVersion7(), null, TestToken));
    }

    private static NoteDto ANote(Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), "Groceries", "Milk, eggs", StubDateTimeProvider.DefaultInstant, null);

    private NoteService ServiceFor(ICurrentUser currentUser) =>
        new(_queries, _store, _unitOfWork, currentUser, new StubDateTimeProvider(), new NoteContentDtoValidator());

    private NoteService Service() => ServiceFor(StubCurrentUser.WithId(_callerId));
}
