using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Policies;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries.GetTodoLists;

public sealed class GetTodoListsCursorKeysTests
{
    private static readonly TodoListCollectionPolicy _policy = TodoListCollectionPolicy.Instance;

    [Fact]
    public void Validate_AValidCreatedAtKey_Succeeds()
    {
        var term = SortOrder.Parse("createdAt", _policy).Value.Terms[0];
        var cursor = Cursor.After(term, DateTimeOffset.UtcNow.ToString("O"), Guid.CreateVersion7());

        GetTodoListsCursorKeys.Validate(cursor).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// The tampered-cursor-key decision this feature makes: a <c>createdAt</c> cursor whose key was
    /// edited into something that is not a date is refused here — as a validation failure, never an
    /// exception — before it can ever reach the persistence layer's keyset predicate. That predicate
    /// only has a throw for this case, which the global exception handler turns into a 500; this
    /// check is what keeps a tampered cursor a 400 instead.
    /// </summary>
    [Fact]
    public void Validate_ATamperedCreatedAtKey_IsRejectedAsCursorInvalid()
    {
        var term = SortOrder.Parse("createdAt", _policy).Value.Terms[0];
        var cursor = Cursor.After(term, "not-a-date", Guid.CreateVersion7());

        var result = GetTodoListsCursorKeys.Validate(cursor);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void Validate_ANameKey_NeedsNoParsing_SoAnyValueIsFine()
    {
        var term = SortOrder.Parse("name", _policy).Value.Terms[0];
        var cursor = Cursor.After(term, "not-a-date-either", Guid.CreateVersion7());

        GetTodoListsCursorKeys.Validate(cursor).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Rejects_ANullCursor() =>
        Should.Throw<ArgumentNullException>(() => GetTodoListsCursorKeys.Validate(null!));
}
