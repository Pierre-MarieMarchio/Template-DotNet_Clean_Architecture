using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Features.TodoLists.Model.Policies;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;
using Shouldly;
using Xunit;
using SortDirection = AppTemplate.Application.Core.Common.Collections.SortDirection;

namespace AppTemplate.Application.UnitTests.Features.TodoLists.UseCases.Queries.GetTodoLists;

public sealed class GetTodoListsRequestBinderTests
{
    private static readonly TodoListCollectionPolicy _policy = TodoListCollectionPolicy.Instance;

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

    [Fact]
    public void ATamperedCursorKey_IsAValidationFailure_NotAnUnhandledException()
    {
        var term = SortOrder.Parse("createdAt", _policy).Value.Terms[0];
        string tampered = Cursor.After(term, "not-a-date", Guid.CreateVersion7()).Encode();

        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery("cursor", null, 10, tampered, "createdAt", null, null, null));

        result.IsFailure.ShouldBeTrue();
        result.Error!.Type.ShouldBe(AppTemplate.Application.Core.Common.Results.ErrorType.Validation);
        result.Error.Code.ShouldBe("cursor.invalid");
    }

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

    [Fact]
    public void ACursorMintedUnderTheDefaultSort_IsAcceptedWithNoSortParameter()
    {
        var defaultTerm = SortOrder.Parse(null, _policy).Value.Terms[0];
        string cursor = Cursor.After(defaultTerm, DateTimeOffset.UtcNow.ToString("O"), Guid.CreateVersion7()).Encode();

        var result = GetTodoListsRequestBinder.Bind(
            new GetTodoListsQuery("cursor", null, 10, cursor, null, null, null, null));

        result.IsSuccess.ShouldBeTrue();
    }

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
}
