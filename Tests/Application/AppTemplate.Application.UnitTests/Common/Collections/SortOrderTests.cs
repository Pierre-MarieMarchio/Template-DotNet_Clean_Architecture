using AppTemplate.Application.Common.Collections;
using Shouldly;
using Xunit;
using SortDirection = AppTemplate.Application.Common.Collections.SortDirection;

namespace AppTemplate.Application.UnitTests.Common.Collections;

public sealed class SortOrderTests
{
    private static readonly FakeCollectionPolicy _policy = new();

    [Fact]
    public void ABlankSort_ParsesTheDefault()
    {
        var result = SortOrder.Parse(null, _policy);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Terms.Count.ShouldBe(1);
        result.Value.Terms[0].Field.ShouldBe("createdAt");
        result.Value.Terms[0].Direction.ShouldBe(SortDirection.Descending);
    }

    [Fact]
    public void AWhitespaceSort_ParsesTheDefault() =>
        SortOrder.Parse("   ", _policy).IsSuccess.ShouldBeTrue();

    [Fact]
    public void ABlankDefaultSort_ThrowsRatherThanRecursingForever()
    {
        var policy = new FakeCollectionPolicy { DefaultSort = "   " };

        Should.Throw<InvalidOperationException>(() => SortOrder.Parse(null, policy));
    }

    [Fact]
    public void ABareField_IsAscending()
    {
        var result = SortOrder.Parse("name", _policy);

        result.Value.Terms[0].Field.ShouldBe("name");
        result.Value.Terms[0].Direction.ShouldBe(SortDirection.Ascending);
    }

    [Theory]
    [InlineData("name:asc", SortDirection.Ascending)]
    [InlineData("name:ASC", SortDirection.Ascending)]
    [InlineData("name:desc", SortDirection.Descending)]
    [InlineData("name:DESC", SortDirection.Descending)]
    public void ADirectionSuffix_IsCaseInsensitive(string raw, SortDirection expected) =>
        SortOrder.Parse(raw, _policy).Value.Terms[0].Direction.ShouldBe(expected);

    [Fact]
    public void AFieldName_IsStoredInTheWhitelistsOwnCasing_NotTheCallers() =>
        SortOrder.Parse("NAME:asc", _policy).Value.Terms[0].Field.ShouldBe("name");

    [Fact]
    public void MultipleTerms_AreAllParsedInOrder()
    {
        var result = SortOrder.Parse("name:asc,createdAt:desc", _policy);

        result.Value.Terms.Count.ShouldBe(2);
        result.Value.Terms[0].Field.ShouldBe("name");
        result.Value.Terms[1].Field.ShouldBe("createdAt");
    }

    [Fact]
    public void AnEmptyTerm_IsRejected()
    {
        var result = SortOrder.Parse("name,,createdAt", _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public void MoreThanOneColonInATerm_IsRejected()
    {
        var result = SortOrder.Parse("name:asc:extra", _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public void AnUnrecognisedDirectionToken_IsRejected()
    {
        var result = SortOrder.Parse("name:sideways", _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("sort.invalid");
        result.Error.Message.ShouldContain("sideways");
    }

    [Fact]
    public void AFieldNotOnTheWhitelist_IsRejected()
    {
        var result = SortOrder.Parse("secretColumn", _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("sort.invalid");
        result.Error.Message.ShouldContain("secretColumn");
    }

    /// <summary>
    /// lastModifiedAt is offset-only, not absent from the whitelist: sorting by it is fine. Only
    /// resuming a keyset page from it is refused, and that is <see cref="Cursor.Decode"/>'s concern.
    /// </summary>
    [Fact]
    public void AFieldNotWhitelistedForKeyset_IsStillSortable() =>
        SortOrder.Parse("lastModifiedAt", _policy).IsSuccess.ShouldBeTrue();

    [Fact]
    public void ADuplicateField_IsRejected()
    {
        var result = SortOrder.Parse("name:asc,name:desc", _policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public void ADuplicateField_IsRejected_EvenAcrossDifferentCasing()
    {
        var result = SortOrder.Parse("name,NAME", _policy);

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void ExactlyMaxSortTerms_IsAccepted()
    {
        var policy = new FakeCollectionPolicy
        {
            SortableFields =
            [
                SortableField.Keyset("a"),
                SortableField.Keyset("b"),
                SortableField.Keyset("c"),
            ],
            MaxSortTerms = 3,
        };

        SortOrder.Parse("a,b,c", policy).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void OneMoreThanMaxSortTerms_IsRejected()
    {
        var policy = new FakeCollectionPolicy
        {
            SortableFields =
            [
                SortableField.Keyset("a"),
                SortableField.Keyset("b"),
                SortableField.Keyset("c"),
                SortableField.Keyset("d"),
            ],
            MaxSortTerms = 3,
        };

        var result = SortOrder.Parse("a,b,c,d", policy);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public void Parse_Rejects_ANullPolicy() =>
        Should.Throw<ArgumentNullException>(() => SortOrder.Parse("name", null!));
}
