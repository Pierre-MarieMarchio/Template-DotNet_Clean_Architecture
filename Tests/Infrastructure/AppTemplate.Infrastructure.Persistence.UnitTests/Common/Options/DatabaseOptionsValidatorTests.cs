using AppTemplate.Infrastructure.Persistence.Common.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Options;

public sealed class DatabaseOptionsValidatorTests
{
    private readonly DatabaseOptionsValidator _validator = new();

    [Fact]
    public void Validate_Succeeds_ForTheDefaults()
    {
        var result = _validator.Validate(name: null, new DatabaseOptions());

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(501)]
    public void Validate_Fails_WhenMaxPoolSizeIsOutOfRange(int maxPoolSize)
    {
        var result = _validator.Validate(name: null, new DatabaseOptions { MaxPoolSize = maxPoolSize });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Database:MaxPoolSize");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void Validate_Fails_WhenCommandTimeoutSecondsIsOutOfRange(int commandTimeoutSeconds)
    {
        var result = _validator.Validate(
            name: null,
            new DatabaseOptions { CommandTimeoutSeconds = commandTimeoutSeconds });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Database:CommandTimeoutSeconds");
    }

    [Fact]
    public void Validate_ReportsBothFailures_WhenBothAreOutOfRange()
    {
        var result = _validator.Validate(
            name: null,
            new DatabaseOptions { MaxPoolSize = -1, CommandTimeoutSeconds = -1 });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MaxPoolSize");
        result.FailureMessage.ShouldContain("CommandTimeoutSeconds");
    }

    [Fact]
    public void Validate_RejectsNull() =>
        Should.Throw<ArgumentNullException>(() => _validator.Validate(name: null, options: null!));
}
