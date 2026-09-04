using AppTemplate.Application.Common;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common;

public sealed class CommonErrorsTests
{
    [Fact]
    public void NotAuthenticated_IsAnUnauthorizedErrorWithAStableCode()
    {
        CommonErrors.NotAuthenticated.Type.ShouldBe(ErrorType.Unauthorized);
        CommonErrors.NotAuthenticated.Code.ShouldBe("auth.required");
    }

    /// <summary>
    /// <paramref name="message"/> is authored by the domain, and is returned verbatim: this is
    /// what lets a caller learn which invariant it tripped.
    /// </summary>
    [Fact]
    public void InvariantViolated_IsAConflictCarryingTheDomainsOwnMessage()
    {
        var error = CommonErrors.InvariantViolated("This list already contains an item titled 'Buy milk'.");

        error.Type.ShouldBe(ErrorType.Conflict);
        error.Code.ShouldBe("domain.invariantViolated");
        error.Message.ShouldBe("This list already contains an item titled 'Buy milk'.");
    }

    /// <summary>Clients branch on the code rather than on the prose, so two must never collide.</summary>
    [Fact]
    public void EveryCode_IsDistinct()
    {
        string[] codes =
        [
            CommonErrors.NotAuthenticated.Code,
            CommonErrors.InvariantViolated("m").Code,
        ];

        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Length);
        codes.ShouldAllBe(code => code.Contains('.', StringComparison.Ordinal));
    }
}
