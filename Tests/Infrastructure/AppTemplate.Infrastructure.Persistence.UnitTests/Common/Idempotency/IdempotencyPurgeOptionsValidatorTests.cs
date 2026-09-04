using AppTemplate.Infrastructure.Persistence.Common.Idempotency;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Idempotency;

public sealed class IdempotencyPurgeOptionsValidatorTests
{
    private readonly IdempotencyPurgeOptionsValidator _validator = new();

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    [InlineData(100_000)]
    public void Validate_Succeeds_ForAnInRangeBatchSize(int batchSize)
    {
        var result = _validator.Validate(name: null, new IdempotencyPurgeOptions { BatchSize = batchSize });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void Validate_Fails_ForAnOutOfRangeBatchSize(int batchSize)
    {
        var result = _validator.Validate(name: null, new IdempotencyPurgeOptions { BatchSize = batchSize });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("IdempotencyPurge:BatchSize");
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}
