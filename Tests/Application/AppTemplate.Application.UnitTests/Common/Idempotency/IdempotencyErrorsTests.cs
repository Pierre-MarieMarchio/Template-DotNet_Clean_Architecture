using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Idempotency;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Idempotency;

public sealed class IdempotencyErrorsTests
{
    [Fact]
    public void KeyInvalid_IsAValidationErrorWithTheStableCode()
    {
        var error = IdempotencyErrors.KeyInvalid("m");

        error.Type.ShouldBe(ErrorType.Validation);
        error.Code.ShouldBe("idempotency.keyInvalid");
        error.Message.ShouldBe("m");
    }

    [Fact]
    public void KeyReused_IsAConflictErrorWithTheStableCode()
    {
        IdempotencyErrors.KeyReused.Type.ShouldBe(ErrorType.Conflict);
        IdempotencyErrors.KeyReused.Code.ShouldBe("idempotency.keyReused");
    }

    [Fact]
    public void InProgress_IsAConflictErrorWithTheStableCode()
    {
        IdempotencyErrors.InProgress.Type.ShouldBe(ErrorType.Conflict);
        IdempotencyErrors.InProgress.Code.ShouldBe("idempotency.inProgress");
    }

    [Fact]
    public void NotReplayable_IsAConflictErrorWithTheStableCode()
    {
        IdempotencyErrors.NotReplayable.Type.ShouldBe(ErrorType.Conflict);
        IdempotencyErrors.NotReplayable.Code.ShouldBe("idempotency.notReplayable");
    }

    /// <summary>Clients branch on the code rather than on the prose, so two must never collide.</summary>
    [Fact]
    public void EveryCode_IsDistinct()
    {
        string[] codes =
        [
            IdempotencyErrors.KeyInvalid("m").Code,
            IdempotencyErrors.KeyReused.Code,
            IdempotencyErrors.InProgress.Code,
            IdempotencyErrors.NotReplayable.Code,
        ];

        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Length);
        codes.ShouldAllBe(code => code.StartsWith("idempotency.", StringComparison.Ordinal));
    }
}
