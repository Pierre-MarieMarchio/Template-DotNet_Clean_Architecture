using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Domain.Core.Common.Exceptions;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Validation;

public sealed class DomainGuardTests
{
    [Fact]
    public void Try_ReturnsSuccess_WhenTheOperationCompletes()
    {
        int calls = 0;

        var result = DomainGuard.Try(() => calls++);

        result.IsSuccess.ShouldBeTrue();
        calls.ShouldBe(1);
    }

    [Fact]
    public void Try_ReturnsAnInvariantViolation_WhenTheOperationThrowsADomainException()
    {
        var result = DomainGuard.Try(() => throw new DomainException("A list already contains that title."));

        result.IsFailure.ShouldBeTrue();

        var error = result.Error;

        error.ShouldNotBeNull();
        error.Code.ShouldBe("domain.invariantViolated");
        error.Message.ShouldBe("A list already contains that title.");
    }

    [Fact]
    public void Try_LetsAnyOtherExceptionPropagate() =>
        Should.Throw<InvalidOperationException>(() => DomainGuard.Try(() => throw new InvalidOperationException()));

    [Fact]
    public void Try_LetsAnOperationCanceledExceptionPropagate() =>
        Should.Throw<OperationCanceledException>(() => DomainGuard.Try(() => throw new OperationCanceledException()));

    [Fact]
    public void TryOfT_ReturnsTheValue_WhenTheOperationCompletes()
    {
        var result = DomainGuard.Try(() => 42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void TryOfT_ReturnsAnInvariantViolation_WhenTheOperationThrowsADomainException()
    {
        var result = DomainGuard.Try<int>(() => throw new DomainException("The list is full."));

        result.IsFailure.ShouldBeTrue();

        var error = result.Error;

        error.ShouldNotBeNull();
        error.Code.ShouldBe("domain.invariantViolated");
        error.Message.ShouldBe("The list is full.");
    }

    [Fact]
    public void TryOfT_LetsAnyOtherExceptionPropagate() =>
        Should.Throw<InvalidOperationException>(
            () => DomainGuard.Try<int>(() => throw new InvalidOperationException()));
}
