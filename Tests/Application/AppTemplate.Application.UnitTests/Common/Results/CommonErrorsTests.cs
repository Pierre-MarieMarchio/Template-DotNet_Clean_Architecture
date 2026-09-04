using AppTemplate.Application.Common.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Results;

public sealed class CommonErrorsTests
{
    [Fact]
    public void NotAuthenticated_IsAnUnauthorizedErrorWithAStableCode()
    {
        CommonErrors.NotAuthenticated.Type.ShouldBe(ErrorType.Unauthorized);
        CommonErrors.NotAuthenticated.Code.ShouldBe("auth.required");
    }

    /// <summary>
    /// The domain authors the message, and it is returned verbatim: this is
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
