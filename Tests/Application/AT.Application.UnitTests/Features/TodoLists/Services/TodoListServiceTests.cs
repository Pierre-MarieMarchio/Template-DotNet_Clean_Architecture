using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Errors;
using AppTemplate.Application.Features.TodoLists.Services;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Services;

public sealed class TodoListServiceTests
{
    private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await AccessFor(StubCurrentUser.Anonymous)
            .LoadOwnedAsync(Guid.CreateVersion7(), precondition: null, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_NeverReachesTheRepository()
    {
        await AccessFor(StubCurrentUser.Anonymous).LoadOwnedAsync(Guid.CreateVersion7(), null, TestToken);

        await _repository.DidNotReceive().GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownId_IsReportedAsNotFound()
    {
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((TodoList?)null);

        var result = await Access().LoadOwnedAsync(missingId, null, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("todoList.notFound");
    }

    [Fact]
    public async Task AnotherUsersList_IsIndistinguishableFromAMissingOne()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var missingId = Guid.CreateVersion7();
        _repository.GetAsync(missingId, Arg.Any<CancellationToken>()).Returns((TodoList?)null);

        var foreignResult = await Access().LoadOwnedAsync(foreign.Id, null, TestToken);
        var missingResult = await Access().LoadOwnedAsync(missingId, null, TestToken);

        foreignResult.IsFailure.ShouldBeTrue();
        foreignResult.Error!.Code.ShouldBe(missingResult.Error!.Code);
        foreignResult.Error.Type.ShouldBe(missingResult.Error.Type);
    }

    [Fact]
    public async Task ANullPrecondition_LeavesTheLoadUnconditional()
    {
        var list = ATodoList.OwnedBy(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);

        var result = await Access().LoadOwnedAsync(list.Id, precondition: null, TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(list);
    }

    [Fact]
    public async Task ASatisfiedPrecondition_Succeeds()
    {
        var list = ATodoList.OwnedBy(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);
        var precondition = new VersionPrecondition([list.Version]);

        var result = await Access().LoadOwnedAsync(list.Id, precondition, TestToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(list);
    }

    [Fact]
    public async Task AnUnsatisfiedPrecondition_Fails()
    {
        var list = ATodoList.OwnedBy(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);
        var precondition = new VersionPrecondition([list.Version + 1]);

        var result = await Access().LoadOwnedAsync(list.Id, precondition, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.PreconditionFailed);
        result.Error.Code.ShouldBe("precondition.failed");
    }

    [Fact]
    public async Task AnEmptyAcceptableVersionSet_SatisfiesNothing()
    {
        var list = ATodoList.OwnedBy(_callerId);
        _repository.GetAsync(list.Id, Arg.Any<CancellationToken>()).Returns(list);
        var precondition = new VersionPrecondition([]);

        var result = await Access().LoadOwnedAsync(list.Id, precondition, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.PreconditionFailed);
    }

    [Fact]
    public async Task APreconditionOnAForeignList_IsReportedAsNotFoundNotAsPreconditionFailed()
    {
        var foreign = ATodoList.OwnedBySomebodyElseThan(_callerId);
        _repository.GetAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);
        var precondition = new VersionPrecondition([foreign.Version]);

        var result = await Access().LoadOwnedAsync(foreign.Id, precondition, TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe(TodoListErrors.ListNotFound(foreign.Id).Code);
    }

    private TodoListService AccessFor(AppTemplate.Application.Core.Common.Ports.ICurrentUser currentUser) =>
        new(_repository, currentUser);

    private TodoListService Access() => AccessFor(StubCurrentUser.WithId(_callerId));
}
