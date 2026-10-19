using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Application.Features.Files.Services.Consumers.AccountDeleted;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.Events;
using AppTemplate.Domain.Features.Files.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Services.Consumers.AccountDeleted;

public sealed class DeleteStoredFilesOnAccountDeletedConsumerTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

    private readonly IStoredFileRepository _repository = Substitute.For<IStoredFileRepository>();
    private readonly IStoredFileQueries _queries = Substitute.For<IStoredFileQueries>();
    private readonly IUserAccountDirectory _accounts = Substitute.For<IUserAccountDirectory>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IDateTimeProvider _dateTimeProvider = new StubDateTimeProvider();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>The removal goes through the aggregate, so each file announces its bytes: that is
    /// what makes the content reclamation prompt rather than sweep-found.</summary>
    [Fact]
    public async Task TheOwnersFiles_AreRemovedAnnouncingTheirBytesAndCommitted()
    {
        var pending = AStoredFile.PendingOwnedBy(_ownerId);
        var available = AStoredFile.AvailableOwnedBy(_ownerId);
        GivenTheAccountIsGone();
        GivenTheOwnerHolds(pending, available);

        var applied = await Consumer().ApplyAsync(_ownerId, TestToken);

        applied.ShouldBeTrue();
        _repository.Received(1).Remove(pending);
        _repository.Received(1).Remove(available);
        pending.DomainEvents.OfType<StoredFileDeletedDomainEvent>().ShouldHaveSingleItem();
        available.DomainEvents.OfType<StoredFileDeletedDomainEvent>().ShouldHaveSingleItem();
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    /// <summary>The precondition is re-derived, never trusted: a replayed delivery must not delete
    /// the data of an account that exists again.</summary>
    [Fact]
    public async Task AnAccountThatExistsAgain_KeepsItsFiles()
    {
        GivenTheAccountIsBack();
        GivenTheOwnerHolds(AStoredFile.PendingOwnedBy(_ownerId));

        var applied = await Consumer().ApplyAsync(_ownerId, TestToken);

        applied.ShouldBeFalse();
        _repository.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>What a redelivery finds: nothing left, so nothing to commit and no event raised.</summary>
    [Fact]
    public async Task AnOwnerWithNoFilesLeft_CommitsNothing()
    {
        GivenTheAccountIsGone();
        GivenTheOwnerHolds();

        var applied = await Consumer().ApplyAsync(_ownerId, TestToken);

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

    /// <summary>The event face is the policy face applied to the event's id.</summary>
    [Fact]
    public async Task TheEventFace_AppliesThePolicyToTheEventsOwner()
    {
        var file = AStoredFile.PendingOwnedBy(_ownerId);
        GivenTheAccountIsGone();
        GivenTheOwnerHolds(file);

        await Consumer().ConsumeAsync(new UserAccountDeleted(_ownerId, DateTimeOffset.UtcNow), TestToken);

        _repository.Received(1).Remove(file);
    }

    private void GivenTheAccountIsGone() =>
        _accounts.ExistsAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(false);

    private void GivenTheAccountIsBack() =>
        _accounts.ExistsAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(true);

    private void GivenTheOwnerHolds(params StoredFile[] files) =>
        _repository.GetForOwnerAsync(_ownerId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<StoredFile>)[.. files]);

    private DeleteStoredFilesOnAccountDeletedConsumer Consumer() =>
        new(_repository, _queries, _accounts, _unitOfWork, _dateTimeProvider);
}
