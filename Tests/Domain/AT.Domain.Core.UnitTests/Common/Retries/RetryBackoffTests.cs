using AppTemplate.Domain.Core.Common.Retries;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.Core.UnitTests.Common.Retries;

public sealed class RetryBackoffTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(8, 60)]
    [InlineData(1_000_000, 60)]
    public void After_DoublesFromAMinuteUpToAnHour(int attempts, int expectedMinutes) =>
        RetryBackoff.After(attempts).ShouldBe(TimeSpan.FromMinutes(expectedMinutes));

    [Fact]
    public void After_Rejects_NoAttemptAtAll() =>
        Should.Throw<ArgumentOutOfRangeException>(() => RetryBackoff.After(0));
}
