using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Application.Features.TodoLists.Services.Consumers.AccountDeleted;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Services.Consumers.AccountDeleted;

public sealed class DeleteTodoListsOnAccountDeletedConsumerTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly ITodoListQueries _queries = Substitute.For<ITodoListQueries>();
    private readonly IUserAccountDirectory _accounts = Substitute.For<IUserAccountDirectory>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheOwnersLists_AreRemovedAndCommitted()
    {
        var list = ATodoList.OwnedBy(_ownerId);
        GivenTheAccountIsGone();
        GivenTheOwnerHolds(list);

        var applied = await Consumer().ApplyAsync(_ownerId, TestToken);

        applied.ShouldBeTrue();
        _repository.Received(1).Remove(list);
        await _unitOfWork.Received(1).SaveChangesAsync(TestToken);
    }

    /// <summary>The precondition is re-derived, never trusted: a delivery that arrives for an id
    /// that names an account again — a replay, or a future outbox — must not delete a live owner's
    /// lists.</summary>
    [Fact]
    public async Task AnAccountThatExistsAgain_KeepsItsLists()
    {
        var list = ATodoList.OwnedBy(_ownerId);
        GivenTheAccountIsBack();
        GivenTheOwnerHolds(list);

        var applied = await Consumer().ApplyAsync(_ownerId, TestToken);

        applied.ShouldBeFalse();
        _repository.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>What a redelivery finds: the first delivery already removed everything, and the
    /// second must be a true no-op.</summary>
    [Fact]
    public async Task AnOwnerWithNoListsLeft_CommitsNothing()
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

    /// <summary>The event face is the policy face applied to the event's id: what holds for the
    /// sweep holds for the fast path, because they are one implementation.</summary>
    [Fact]
    public async Task TheEventFace_AppliesThePolicyToTheEventsOwner()
    {
        var list = ATodoList.OwnedBy(_ownerId);
        GivenTheAccountIsGone();
        GivenTheOwnerHolds(list);

        await Consumer().ConsumeAsync(new UserAccountDeleted(_ownerId, DateTimeOffset.UtcNow), TestToken);

        _repository.Received(1).Remove(list);
    }

    private void GivenTheAccountIsGone() =>
        _accounts.ExistsAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(false);

    private void GivenTheAccountIsBack() =>
        _accounts.ExistsAsync(_ownerId, Arg.Any<CancellationToken>()).Returns(true);

    private void GivenTheOwnerHolds(params TodoList[] lists) =>
        _repository.GetForOwnerAsync(_ownerId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TodoList>)[.. lists]);

    private DeleteTodoListsOnAccountDeletedConsumer Consumer() =>
        new(_repository, _queries, _accounts, _unitOfWork);
}
