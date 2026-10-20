using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Reminders.Ports.ReminderQueries;
using AppTemplate.Application.Features.Reminders.Services.Consumers.AccountDeleted;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;
using AppTemplate.Domain.Features.Reminders.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.Services.Consumers.AccountDeleted;

public sealed class DeleteRemindersOnAccountDeletedConsumerTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

    private readonly IReminderRepository _repository = Substitute.For<IReminderRepository>();
    private readonly IReminderQueries _queries = Substitute.For<IReminderQueries>();
    private readonly IUserAccountDirectory _accounts = Substitute.For<IUserAccountDirectory>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheOwnersReminders_AreRemovedAndCommittedWhateverTheirState()
    {
        var pending = AReminder.OwnedBy(_ownerId, todoItemId: Guid.CreateVersion7());
        var fired = AReminder.Rehydrated(_ownerId, StubDateTimeProvider.DefaultInstant, ReminderState.Fired, notifiedAt: StubDateTimeProvider.DefaultInstant);
        GivenTheAccountIsGone();
        GivenTheOwnerHolds(pending, fired);

        var applied = await Consumer().ApplyAsync(_ownerId, TestToken);

        applied.ShouldBeTrue();
        _repository.Received(1).Remove(pending);
        _repository.Received(1).Remove(fired);
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    [Fact]
    public async Task AnAccountThatExistsAgain_KeepsItsReminders()
    {
        GivenTheAccountIsBack();
        GivenTheOwnerHolds(AReminder.OwnedBy(_ownerId, todoItemId: Guid.CreateVersion7()));

        var applied = await Consumer().ApplyAsync(_ownerId, TestToken);

        applied.ShouldBeFalse();
        _repository.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOwnerWithNoRemindersLeft_CommitsNothing()
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

    [Fact]
    public async Task TheEventFace_AppliesThePolicyToTheEventsOwner()
    {
        var reminder = AReminder.OwnedBy(_ownerId, todoItemId: Guid.CreateVersion7());
        GivenTheAccountIsGone();
        GivenTheOwnerHolds(reminder);

        await Consumer().ConsumeAsync(new UserAccountDeleted(_ownerId, DateTimeOffset.UtcNow), TestToken);

        _repository.Received(1).Remove(reminder);
    }

    private void GivenTheAccountIsGone() =>
        _accounts.ExistsAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(false);

    private void GivenTheAccountIsBack() =>
        _accounts.ExistsAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(true);

    private void GivenTheOwnerHolds(params Reminder[] reminders) =>
        _repository.GetForOwnerAsync(_ownerId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)[.. reminders]);

    private DeleteRemindersOnAccountDeletedConsumer Consumer() =>
        new(_repository, _queries, _accounts, _unitOfWork);
}
