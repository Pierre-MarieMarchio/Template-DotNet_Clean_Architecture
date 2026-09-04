using AppTemplate.Application.Common;
using AppTemplate.Application.Features.TodoLists.Errors;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Errors;

public sealed class TodoListErrorsTests
{
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
            TodoListErrors.ListNotFound(Guid.Empty).Code,
            TodoListErrors.ItemNotFound(Guid.Empty).Code,
            TodoListErrors.IfMatchRequired.Code,
            TodoListErrors.MalformedIfMatch.Code,
        ];

        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Length);
        codes.ShouldAllBe(code => code.Contains('.', StringComparison.Ordinal));
    }
}
