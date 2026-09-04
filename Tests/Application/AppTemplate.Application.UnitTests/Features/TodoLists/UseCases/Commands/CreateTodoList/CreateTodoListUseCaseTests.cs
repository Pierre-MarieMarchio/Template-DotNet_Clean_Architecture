using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Commands.CreateTodoList;

public sealed class CreateTodoListUseCaseTests
{
    private static readonly Guid _callerId = Guid.CreateVersion7();

    private readonly ITodoListRepository _repository = Substitute.For<ITodoListRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FixedDateTimeProvider _clock = new();
    private readonly CreateTodoListCommandValidator _validator = new();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    #region Authentication

    [Fact]
    public async Task AnAnonymousCaller_IsRefused()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new CreateTodoListCommand("Groceries"), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
        result.Error.Code.ShouldBe("auth.required");
    }

    [Fact]
    public async Task AnAnonymousCaller_WritesNothing()
    {
        await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new CreateTodoListCommand("Groceries"), TestToken);

        _repository.DidNotReceive().Add(Arg.Any<TodoList>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Authentication is checked before validation, so an anonymous caller learns nothing
    /// about which of their fields were malformed.
    /// </summary>
    [Fact]
    public async Task AnAnonymousCaller_IsRefusedBeforeTheirInputIsValidated()
    {
        var result = await UseCaseFor(StubCurrentUser.Anonymous)
            .ExecuteAsync(new CreateTodoListCommand(""), TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Unauthorized);
    }

    #endregion

    #region Validation

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankName_IsRejected(string name)
    {
        var result = await UseCase().ExecuteAsync(new CreateTodoListCommand(name), TestToken);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("request.validationFailed");
        result.Error.Details!["name"].Any(message => message.Contains("required", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task ANameLongerThanTheDomainAllows_IsRejected()
    {
        var result = await UseCase().ExecuteAsync(
            new CreateTodoListCommand(new string('a', TodoListName.MaxLength + 1)),
            TestToken);

        result.Error!.Type.ShouldBe(ErrorType.Validation);
        result.Error.Details!["name"].Any(message => message.Contains("exceed", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task ANameOfExactlyTheMaximumLength_IsAccepted()
    {
        var result = await UseCase().ExecuteAsync(
            new CreateTodoListCommand(new string('a', TodoListName.MaxLength)),
            TestToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// The transactional boundary: a use case that failed early must not have committed,
    /// and must not have staged anything a later commit could pick up.
    /// </summary>
    [Fact]
    public async Task AnInvalidCommand_WritesNothing()
    {
        await UseCase().ExecuteAsync(new CreateTodoListCommand(""), TestToken);

        _repository.DidNotReceive().Add(Arg.Any<TodoList>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ANullCommand_IsAProgrammingError() =>
        await Should.ThrowAsync<ArgumentNullException>(() => UseCase().ExecuteAsync(null!, TestToken));

    #endregion

    #region Success

    [Fact]
    public async Task AValidCommand_StagesAListOwnedByTheCaller()
    {
        await UseCase().ExecuteAsync(new CreateTodoListCommand("Groceries"), TestToken);

        _repository.Received(1).Add(Arg.Is<TodoList>(list => list != null && list.OwnerId == _callerId));
    }

    /// <summary>
    /// The owner comes from <see cref="ICurrentUser"/> and the command has no owner field,
    /// so no caller can create a list in somebody else's name.
    /// </summary>
    [Fact]
    public async Task TheOwner_IsAlwaysTheCallerAndNeverComesFromTheRequest()
    {
        var otherCallerId = Guid.CreateVersion7();

        await UseCaseFor(StubCurrentUser.WithId(otherCallerId))
            .ExecuteAsync(new CreateTodoListCommand("Groceries"), TestToken);

        _repository.Received(1).Add(Arg.Is<TodoList>(list => list != null && list.OwnerId == otherCallerId));
        _repository.DidNotReceive().Add(Arg.Is<TodoList>(list => list != null && list.OwnerId == _callerId));
    }

    [Fact]
    public async Task AValidCommand_ReturnsTheDetailOfTheStagedList()
    {
        var staged = CaptureStagedLists();

        var result = await UseCase().ExecuteAsync(new CreateTodoListCommand("Groceries"), TestToken);

        result.IsSuccess.ShouldBeTrue();
        var list = staged.ShouldHaveSingleItem();
        result.Value.Value.Id.ShouldBe(list.Id);
        result.Value.Value.Id.ShouldNotBe(Guid.Empty);
        result.Value.Value.Items.ShouldBeEmpty();
        result.Value.Version.ShouldBe(list.Version);
    }

    [Fact]
    public async Task TheName_IsNormalisedByTheDomain()
    {
        var staged = CaptureStagedLists();

        await UseCase().ExecuteAsync(new CreateTodoListCommand("  Groceries  "), TestToken);

        staged.ShouldHaveSingleItem().Name.Value.ShouldBe("Groceries");
    }

    /// <summary>Two calls would mean two transactions with no rollback path between them.</summary>
    [Fact]
    public async Task ASuccessfulCommand_CommitsExactlyOnce()
    {
        await UseCase().ExecuteAsync(new CreateTodoListCommand("Groceries"), TestToken);

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheCancellationToken_IsForwardedToTheCommit()
    {
        using var cancellation = new CancellationTokenSource();

        await UseCase().ExecuteAsync(new CreateTodoListCommand("Groceries"), cancellation.Token);

        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    /// <summary>
    /// The creation event is raised with the injected clock's instant, not with an ambient
    /// <c>DateTime.UtcNow</c> read inside the aggregate.
    /// </summary>
    [Fact]
    public async Task TheStagedList_CarriesACreationEventStampedByTheInjectedClock()
    {
        var staged = CaptureStagedLists();

        await UseCase().ExecuteAsync(new CreateTodoListCommand("Groceries"), TestToken);

        var domainEvent = staged.ShouldHaveSingleItem().DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<TodoListCreatedDomainEvent>();
        domainEvent.OwnerId.ShouldBe(_callerId);
        domainEvent.OccurredOn.ShouldBe(FixedDateTimeProvider.DefaultInstant);
    }

    #endregion

    private CreateTodoListUseCase UseCaseFor(ICurrentUser currentUser) =>
        new(_repository, _unitOfWork, currentUser, _clock, _validator);

    private CreateTodoListUseCase UseCase() => UseCaseFor(StubCurrentUser.WithId(_callerId));

    private List<TodoList> CaptureStagedLists()
    {
        var staged = new List<TodoList>();

        _repository.When(repository => repository.Add(Arg.Any<TodoList>()))
            .Do(call => staged.Add(call.Arg<TodoList>()!));

        return staged;
    }
}
