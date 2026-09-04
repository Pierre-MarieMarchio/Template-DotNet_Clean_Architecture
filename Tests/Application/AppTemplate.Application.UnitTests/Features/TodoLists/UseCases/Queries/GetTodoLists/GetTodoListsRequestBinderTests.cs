using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Policies;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;
using Shouldly;
using Xunit;
using SortDirection = AppTemplate.Application.Common.Collections.SortDirection;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries.GetTodoLists;

/// <summary>
/// The query-string translation lifted out of <c>GetTodoListsUseCase</c>: paging bounds,
/// sort parsing and the cursor/sort coherence rules, tested against
/// <see cref="GetTodoListsRequestBinder.Bind"/> directly rather than through a use case and a
/// mocked port.
/// </summary>
public sealed class GetTodoListsRequestBinderTests
{
    private static readonly TodoListCollectionPolicy _policy = TodoListCollectionPolicy.Instance;

    #region Paging bounds

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void APageNumberBelowOne_IsRejected(int page)
    {
        var result = GetTodoListsRequestBinder.Bind(GetTodoListsQuery.Offset(page, 10));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void APageSizeOutsideTheAllowedRange_IsRejected(int pageSize)
    {
        var result = GetTodoListsRequestBinder.Bind(GetTodoListsQuery.Offset(1, pageSize));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
    }

    [Fact]
    public void APageSizeAboveTheCeiling_IsRejected()
    {
        var result = GetTodoListsRequestBinder.Bind(GetTodoListsQuery.Offset(1, _policy.MaxPageSize + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void APageSizeInsideTheAllowedRange_IsAccepted(int pageSize) =>
        GetTodoListsRequestBinder.Bind(GetTodoListsQuery.Offset(1, pageSize)).IsSuccess.ShouldBeTrue();

    [Fact]
    public void ThePageSizeCeiling_IsItselfAccepted() =>
        GetTodoListsRequestBinder.Bind(GetTodoListsQuery.Offset(1, _policy.MaxPageSize)).IsSuccess.ShouldBeTrue();

    #endregion

    #region Sorting

    [Fact]
    public void AnInvalidSort_IsRejected()
    {
        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery(null, 1, 10, null, "bogusField", null, null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public void AMultiTermSortInCursorMode_IsRejected()
    {
        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery("cursor", null, 10, null, "name,createdAt", null, null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    /// <summary>
    /// Same rule restated with no cursor sent at all: a caller must not be let through on page 1
    /// only to be refused once they try to resume with page 2.
    /// </summary>
    [Fact]
    public void AMultiTermSortInCursorMode_IsRejected_OnTheFirstPageBeforeAnyCursorExists()
    {
        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery("cursor", null, 20, null, "name:asc,createdAt:desc", null, null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void ASingleTermSortInCursorMode_IsAccepted() =>
        GetTodoListsRequestBinder.Bind(new GetTodoListsQuery("cursor", null, 10, null, "name", null, null, null))
            .IsSuccess.ShouldBeTrue();

    #endregion

    #region Cursor and offset interaction

    [Fact]
    public void ACursorSentWithPagingOffset_IsRejected()
    {
        var term = SortOrder.Parse(null, _policy).Value.Terms[0];
        string cursor = Cursor.After(term, DateTimeOffset.UtcNow.ToString("O"), Guid.CreateVersion7()).Encode();

        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery("offset", 1, 10, cursor, null, null, null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
    }

    [Fact]
    public void APageNumberSentWithPagingCursor_IsRejected()
    {
        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery("cursor", 1, 10, null, null, null, null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("paging.invalid");
    }

    /// <summary>Proves the tampered-cursor-key decision end to end: a 400, never an exception.</summary>
    [Fact]
    public void ATamperedCursorKey_IsAValidationFailure_NotAnUnhandledException()
    {
        var term = SortOrder.Parse("createdAt", _policy).Value.Terms[0];
        string tampered = Cursor.After(term, "not-a-date", Guid.CreateVersion7()).Encode();

        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery("cursor", null, 10, tampered, "createdAt", null, null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(AppTemplate.Application.Common.Results.ErrorType.Validation);
        result.Error.Code.ShouldBe("cursor.invalid");
    }

    /// <summary>
    /// The read side compares a cursor's key using the term from <c>sort</c>, not the term recorded
    /// in the cursor. Resuming a name-ordered cursor under <c>sort=createdAt</c> would therefore hand
    /// a list's name to a date comparison, whose only recourse is to throw — a 500 for what is really
    /// a malformed request. This is the check that keeps it a 400.
    /// </summary>
    [Fact]
    public void ACursorMintedUnderADifferentSortField_IsRejected()
    {
        var nameTerm = SortOrder.Parse("name:asc", _policy).Value.Terms[0];
        string cursor = Cursor.After(nameTerm, "Groceries", Guid.CreateVersion7()).Encode();

        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery("cursor", null, 10, cursor, "createdAt:desc", null, null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void ACursorMintedUnderTheOppositeDirection_IsRejected()
    {
        var descending = SortOrder.Parse("createdAt:desc", _policy).Value.Terms[0];
        string cursor = Cursor.After(descending, DateTimeOffset.UtcNow.ToString("O"), Guid.CreateVersion7()).Encode();

        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery("cursor", null, 10, cursor, "createdAt:asc", null, null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    /// <summary>
    /// The default sort is what a caller gets when they send none, so a cursor minted under it must
    /// keep working with <c>sort</c> omitted.
    /// </summary>
    [Fact]
    public void ACursorMintedUnderTheDefaultSort_IsAcceptedWithNoSortParameter()
    {
        var defaultTerm = SortOrder.Parse(null, _policy).Value.Terms[0];
        string cursor = Cursor.After(defaultTerm, DateTimeOffset.UtcNow.ToString("O"), Guid.CreateVersion7()).Encode();

        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery("cursor", null, 10, cursor, null, null, null, null));

        result.IsSuccess.ShouldBeTrue();
    }

    #endregion

    #region Shape of a bound request

    [Fact]
    public void ABoundRequest_CarriesTheParsedPagingSortAndFilter()
    {
        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery(null, 3, 25, null, "name:asc", "milk", null, null));

        result.IsSuccess.ShouldBeTrue();
        var request = result.Value;
        request.Paging.Mode.ShouldBe(PagingMode.Offset);
        request.Paging.Page.ShouldBe(3);
        request.Paging.PageSize.ShouldBe(25);
        request.Sort.Terms.ShouldHaveSingleItem().Field.ShouldBe("name");
        request.Sort.Terms[0].Direction.ShouldBe(SortDirection.Ascending);
        request.Filter.Search!.Value.ShouldBe("milk");
    }

    #endregion
}
