using AppTemplate.Application.Core.Common.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Collections;

public sealed class CursorKeysTests
{
    private static readonly FakeCollectionPolicy _policy = new();

    private const string _dateField = "createdAt";

    [Fact]
    public void Validate_AValidDateKey_Succeeds()
    {
        var term = SortOrder.Parse(_dateField, _policy).Value.Terms[0];
        var cursor = Cursor.After(term, DateTimeOffset.UtcNow.ToString("O"), Guid.CreateVersion7());

        CursorKeys.Validate(cursor, _dateField).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Validate_ATamperedDateKey_IsRejectedAsCursorInvalid()
    {
        var term = SortOrder.Parse(_dateField, _policy).Value.Terms[0];
        var cursor = Cursor.After(term, "not-a-date", Guid.CreateVersion7());

        var result = CursorKeys.Validate(cursor, _dateField);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("cursor.invalid");
    }

    [Fact]
    public void Validate_AKeyOfAFieldTheFeatureDidNotCallADate_NeedsNoParsing_SoAnyValueIsFine()
    {
        var term = SortOrder.Parse("name", _policy).Value.Terms[0];
        var cursor = Cursor.After(term, "not-a-date-either", Guid.CreateVersion7());

        CursorKeys.Validate(cursor, _dateField).IsSuccess.ShouldBeTrue();
    }
}
