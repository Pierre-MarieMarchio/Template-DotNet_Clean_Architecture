using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries;
using AppTemplate.Application.Features.TodoLists.Validators;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Validators;

/// <summary>
/// These commands and queries carry only ids and a precondition: the only thing to validate is
/// that <c>Guid.Empty</c> does not reach the repository. One class because every case is the
/// same shape, repeated across the validators the spec says were missing entirely.
/// </summary>
public sealed class IdRequiredValidatorsTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeleteTodoListCommandValidator_Rejects_AnEmptyListId() =>
        (await new DeleteTodoListCommandValidator().ValidateAsync(new DeleteTodoListCommand(Guid.Empty), TestToken))
        .IsValid.ShouldBeFalse();

    [Fact]
    public async Task DeleteTodoListCommandValidator_Accepts_ARealListId() =>
        (await new DeleteTodoListCommandValidator()
                .ValidateAsync(new DeleteTodoListCommand(Guid.CreateVersion7()), TestToken))
        .IsValid.ShouldBeTrue();

    [Fact]
    public async Task CompleteTodoItemCommandValidator_Rejects_AnEmptyListOrItemId()
    {
        var validator = new CompleteTodoItemCommandValidator();

        (await validator.ValidateAsync(new CompleteTodoItemCommand(Guid.Empty, Guid.CreateVersion7()), TestToken))
            .IsValid.ShouldBeFalse();
        (await validator.ValidateAsync(new CompleteTodoItemCommand(Guid.CreateVersion7(), Guid.Empty), TestToken))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveTodoItemCommandValidator_Rejects_AnEmptyListOrItemId()
    {
        var validator = new RemoveTodoItemCommandValidator();

        (await validator.ValidateAsync(new RemoveTodoItemCommand(Guid.Empty, Guid.CreateVersion7()), TestToken))
            .IsValid.ShouldBeFalse();
        (await validator.ValidateAsync(new RemoveTodoItemCommand(Guid.CreateVersion7(), Guid.Empty), TestToken))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task ReopenTodoItemCommandValidator_Rejects_AnEmptyListOrItemId()
    {
        var validator = new ReopenTodoItemCommandValidator();

        (await validator.ValidateAsync(new ReopenTodoItemCommand(Guid.Empty, Guid.CreateVersion7()), TestToken))
            .IsValid.ShouldBeFalse();
        (await validator.ValidateAsync(new ReopenTodoItemCommand(Guid.CreateVersion7(), Guid.Empty), TestToken))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveTagFromTodoItemCommandValidator_Rejects_AnEmptyListOrItemId()
    {
        var validator = new RemoveTagFromTodoItemCommandValidator();

        (await validator.ValidateAsync(
                new RemoveTagFromTodoItemCommand(Guid.Empty, Guid.CreateVersion7(), "urgent"), TestToken))
            .IsValid.ShouldBeFalse();
        (await validator.ValidateAsync(
                new RemoveTagFromTodoItemCommand(Guid.CreateVersion7(), Guid.Empty, "urgent"), TestToken))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task GetTodoListQueryValidator_Rejects_AnEmptyListId() =>
        (await new GetTodoListQueryValidator().ValidateAsync(new GetTodoListQuery(Guid.Empty), TestToken))
        .IsValid.ShouldBeFalse();

    [Fact]
    public async Task GetTodoItemQueryValidator_Rejects_AnEmptyListOrItemId()
    {
        var validator = new GetTodoItemQueryValidator();

        (await validator.ValidateAsync(new GetTodoItemQuery(Guid.Empty, Guid.CreateVersion7()), TestToken))
            .IsValid.ShouldBeFalse();
        (await validator.ValidateAsync(new GetTodoItemQuery(Guid.CreateVersion7(), Guid.Empty), TestToken))
            .IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task GetTodoItemsQueryValidator_Rejects_AnEmptyListId() =>
        (await new GetTodoItemsQueryValidator().ValidateAsync(new GetTodoItemsQuery(Guid.Empty), TestToken))
        .IsValid.ShouldBeFalse();
}
