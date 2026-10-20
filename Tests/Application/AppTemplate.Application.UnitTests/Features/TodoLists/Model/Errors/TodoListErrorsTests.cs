using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Errors;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.Model.Errors;

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
    public void EveryCode_IsDistinct()
    {
        string[] codes =
        [
            TodoListErrors.ListNotFound(Guid.Empty).Code,
            TodoListErrors.ItemNotFound(Guid.Empty).Code,
        ];

        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Length);
        codes.ShouldAllBe(code => code.Contains('.', StringComparison.Ordinal));
    }
}
