using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Collections;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Collections;

public sealed class CollectionErrorsTests
{
    [Fact]
    public void InvalidPaging_IsAValidationErrorWithTheStableCode()
    {
        var error = CollectionErrors.InvalidPaging("m");

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("paging.invalid");
        error.Message.ShouldBe("m");
    }

    [Fact]
    public void InvalidSort_IsAValidationErrorWithTheStableCode()
    {
        var error = CollectionErrors.InvalidSort("m");

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("sort.invalid");
    }

    [Fact]
    public void InvalidFilter_IsAValidationErrorWithTheStableCode()
    {
        var error = CollectionErrors.InvalidFilter("m");

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("filter.invalid");
    }

    [Fact]
    public void InvalidCursor_IsAValidationErrorWithTheStableCode()
    {
        var error = CollectionErrors.InvalidCursor("m");

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("cursor.invalid");
    }

    /// <summary>Clients branch on the code rather than on the prose, so two must never collide.</summary>
    [Fact]
    public void EveryCode_IsDistinct()
    {
        string[] codes =
        [
            CollectionErrors.InvalidPaging("m").Code,
            CollectionErrors.InvalidSort("m").Code,
            CollectionErrors.InvalidFilter("m").Code,
            CollectionErrors.InvalidCursor("m").Code,
        ];

        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Length);
        codes.ShouldAllBe(code => code.Contains('.', StringComparison.Ordinal));
    }
}
