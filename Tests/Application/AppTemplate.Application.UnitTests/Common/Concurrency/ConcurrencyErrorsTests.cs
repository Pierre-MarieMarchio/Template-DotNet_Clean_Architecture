using AppTemplate.Application.Common;
using AppTemplate.Application.Common.Concurrency;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Concurrency;

public sealed class ConcurrencyErrorsTests
{
    /// <summary>
    /// A stale write is not the same failure as a race, and the two must not answer the same code:
    /// one asks the caller to read again, the other says the caller never read at all.
    /// </summary>
    [Fact]
    public void PreconditionFailed_IsItsOwnTypeAndCode()
    {
        ConcurrencyErrors.PreconditionFailed.Type.ShouldBe(ErrorType.PreconditionFailed);
        ConcurrencyErrors.PreconditionFailed.Code.ShouldBe("precondition.failed");
        ConcurrencyErrors.PreconditionFailed.Code.ShouldNotBe("concurrency.conflict");
    }
}
