using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Concurrency;

public sealed class ConcurrencyErrorsTests
{
    [Fact]
    public void PreconditionFailed_IsItsOwnTypeAndCode()
    {
        ConcurrencyErrors.PreconditionFailed.Type.ShouldBe(ErrorType.PreconditionFailed);
        ConcurrencyErrors.PreconditionFailed.Code.ShouldBe("precondition.failed");
        ConcurrencyErrors.PreconditionFailed.Code.ShouldNotBe("concurrency.conflict");
    }
}
