using AppTemplate.Application.Common;
using AppTemplate.Application.Features.TodoLists.Errors;
using FluentValidation.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Errors;

public sealed class TodoListErrorsTests
{
    [Fact]
    public void NotAuthenticated_IsAnUnauthorizedErrorWithAStableCode()
    {
        TodoListErrors.NotAuthenticated.Type.ShouldBe(ErrorType.Unauthorized);
        TodoListErrors.NotAuthenticated.Code.ShouldBe("auth.required");
    }

    [Fact]
    public void ListNotFound_IsANotFoundErrorNamingTheList()
    {
        var listId = Guid.CreateVersion7();

        var error = TodoListErrors.ListNotFound(listId);

        error.Type.ShouldBe(ErrorType.NotFound);
        error.Code.ShouldBe("todoList.notFound");
        error.Message.ShouldContain(listId.ToString());
    }

    [Fact]
    public void ItemNotFound_IsANotFoundErrorNamingTheItem()
    {
        var itemId = Guid.CreateVersion7();

        var error = TodoListErrors.ItemNotFound(itemId);

        error.Type.ShouldBe(ErrorType.NotFound);
        error.Code.ShouldBe("todoItem.notFound");
        error.Message.ShouldContain(itemId.ToString());
    }

    [Fact]
    public void AListAndAnItemNotFound_AreDistinguishableFromEachOther() =>
        TodoListErrors.ListNotFound(Guid.CreateVersion7()).Code
            .ShouldNotBe(TodoListErrors.ItemNotFound(Guid.CreateVersion7()).Code);

    /// <summary>
    /// An invariant violation is a conflict, not a validation error: the caller could not
    /// have avoided it by sending better input.
    /// </summary>
    [Fact]
    public void InvariantViolated_IsAConflictCarryingTheDomainsOwnMessage()
    {
        var error = TodoListErrors.InvariantViolated("This list already contains an item titled 'Buy milk'.");

        error.Type.ShouldBe(ErrorType.Conflict);
        error.Code.ShouldBe("todoList.invariantViolated");
        error.Message.ShouldBe("This list already contains an item titled 'Buy milk'.");
    }

    [Fact]
    public void InvalidPaging_IsAValidationError()
    {
        var error = TodoListErrors.InvalidPaging("The page number must be 1 or greater.");

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("paging.invalid");
        error.Message.ShouldBe("The page number must be 1 or greater.");
    }

    [Fact]
    public void Invalid_IsAValidationErrorCarryingEveryFailureMessage()
    {
        var validationResult = new ValidationResult(
        [
            new ValidationFailure("Name", "A list name is required."),
            new ValidationFailure("TodoListId", "A list id is required."),
        ]);

        var error = TodoListErrors.Invalid(validationResult);

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("todoList.validationFailed");
        error.Message.ShouldContain("A list name is required.");
        error.Message.ShouldContain("A list id is required.");
    }

    [Fact]
    public void Invalid_ProducesAnEmptyMessage_WhenThereAreNoFailures() =>
        TodoListErrors.Invalid(new ValidationResult()).Message.ShouldBeEmpty();

    [Fact]
    public void Invalid_Rejects_ANullValidationResult() =>
        Should.Throw<ArgumentNullException>(() => TodoListErrors.Invalid(null!));

    /// <summary>
    /// A stale write is not the same failure as a race, and the two must not answer the same code:
    /// one asks the caller to read again, the other says the caller never read at all.
    /// </summary>
    [Fact]
    public void PreconditionFailed_IsItsOwnTypeAndCode()
    {
        TodoListErrors.PreconditionFailed.Type.ShouldBe(ErrorType.PreconditionFailed);
        TodoListErrors.PreconditionFailed.Code.ShouldBe("precondition.failed");
        TodoListErrors.PreconditionFailed.Code.ShouldNotBe("concurrency.conflict");
    }

    [Fact]
    public void IfMatchRequired_IsAPreconditionRequiredErrorNamingTheHeader()
    {
        TodoListErrors.IfMatchRequired.Type.ShouldBe(ErrorType.PreconditionRequired);
        TodoListErrors.IfMatchRequired.Code.ShouldBe("precondition.required");
        TodoListErrors.IfMatchRequired.Message.ShouldContain("If-Match");
    }

    /// <summary>
    /// A header this API cannot parse is the caller's mistake, not a state conflict — 400, and
    /// distinguishable from the 412 a well-formed but stale validator gets.
    /// </summary>
    [Fact]
    public void MalformedIfMatch_IsAValidationError()
    {
        TodoListErrors.MalformedIfMatch.Type.ShouldBe(ErrorType.Validation);
        TodoListErrors.MalformedIfMatch.Code.ShouldBe("precondition.malformed");
    }

    /// <summary>Clients branch on the code rather than on the prose, so two must never collide.</summary>
    [Fact]
    public void EveryCode_IsDistinct()
    {
        string[] codes =
        [
            TodoListErrors.NotAuthenticated.Code,
            TodoListErrors.ListNotFound(Guid.Empty).Code,
            TodoListErrors.ItemNotFound(Guid.Empty).Code,
            TodoListErrors.InvariantViolated("m").Code,
            TodoListErrors.InvalidPaging("m").Code,
            TodoListErrors.Invalid(new ValidationResult()).Code,
            TodoListErrors.PreconditionFailed.Code,
            TodoListErrors.IfMatchRequired.Code,
            TodoListErrors.MalformedIfMatch.Code,
        ];

        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Length);
        codes.ShouldAllBe(code => code.Contains('.', StringComparison.Ordinal));
    }
}
