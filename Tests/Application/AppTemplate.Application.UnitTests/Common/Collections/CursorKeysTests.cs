using AppTemplate.Application.Common.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Collections;

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

    /// <summary>
    /// The tampered-cursor-key decision this check exists for: a date cursor whose key was edited
    /// into something that is not a date is refused here — as a validation failure, never an
    /// exception — before it can ever reach the persistence layer's keyset predicate. That predicate
    /// only has a throw for this case, which the global exception handler turns into a 500; this
    /// check is what keeps a tampered cursor a 400 instead.
    /// </summary>
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

    [Fact]
    public void Validate_Rejects_ANullCursor() =>
        Should.Throw<ArgumentNullException>(() => CursorKeys.Validate(null!, _dateField));
}
