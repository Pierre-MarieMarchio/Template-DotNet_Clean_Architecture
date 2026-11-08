using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Notes.Ports.NoteQueries;
using AppTemplate.Application.Features.Notes.Ports.NoteStore;
using AppTemplate.Application.Features.Notes.Services.Consumers;
using AppTemplate.Domain.Core.Common.Primitives;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Notes.Services.Consumers;

public sealed class DeleteNotesOnAccountDeletedConsumerTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

    private readonly INoteStore _notes = Substitute.For<INoteStore>();
    private readonly INoteQueries _queries = Substitute.For<INoteQueries>();
    private readonly IUserAccountDirectory _accounts = Substitute.For<IUserAccountDirectory>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheOwnersNotes_AreStagedForRemovalAndCommitted()
    {
        GivenTheAccountIsGone();
        _notes.StageRemovalForOwnerAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(3);

        bool applied = await Consumer().ApplyAsync(_ownerId, TestToken);

        applied.ShouldBeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    [Fact]
    public async Task AnAccountThatExistsAgain_KeepsItsNotes()
    {
        GivenTheAccountIsBack();
        _notes.StageRemovalForOwnerAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(3);

        bool applied = await Consumer().ApplyAsync(_ownerId, TestToken);

        applied.ShouldBeFalse();
        _notes.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOwnerWithNoNotesLeft_CommitsNothing()
    {
        GivenTheAccountIsGone();
        _notes.StageRemovalForOwnerAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(0);

        bool applied = await Consumer().ApplyAsync(_ownerId, TestToken);

        applied.ShouldBeFalse();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheSweepsOwnerListing_IsTheQueriesPortsAnswer()
    {
        _queries.ListDataOwnerIdsAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<UserId>)[_ownerId]);

        var owners = await Consumer().ListDataOwnerIdsAsync(TestToken);

        owners.ShouldBe([_ownerId]);
    }

    [Fact]
    public async Task TheEventFace_AppliesThePolicyToTheEventsOwner()
    {
        GivenTheAccountIsGone();
        _notes.StageRemovalForOwnerAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(1);

        await Consumer().ConsumeAsync(new UserAccountDeleted(_ownerId, DateTimeOffset.UtcNow), TestToken);

        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    private void GivenTheAccountIsGone() =>
        _accounts.ExistsAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(false);

    private void GivenTheAccountIsBack() =>
        _accounts.ExistsAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(true);

    private DeleteNotesOnAccountDeletedConsumer Consumer() =>
        new(_notes, _queries, _accounts, _unitOfWork);
}
