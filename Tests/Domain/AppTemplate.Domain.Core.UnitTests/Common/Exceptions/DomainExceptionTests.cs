using AppTemplate.Domain.Core.Common.Exceptions;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.Core.UnitTests.Common.Exceptions;

public sealed class DomainExceptionTests
{
    [Fact]
    public void TheMessageConstructor_KeepsTheMessage() =>
        new DomainException("A to-do list must have an owner.").Message
            .ShouldBe("A to-do list must have an owner.");

    [Fact]
    public void TheInnerExceptionConstructor_KeepsBoth()
    {
        var inner = new InvalidOperationException("underlying");

        var exception = new DomainException("A to-do list must have an owner.", inner);

        exception.Message.ShouldBe("A to-do list must have an owner.");
        exception.InnerException.ShouldBeSameAs(inner);
    }

    [Fact]
    public void EveryConstructor_TakesAMessage() =>
        typeof(DomainException).GetConstructors()
            .ShouldAllBe(constructor => constructor.GetParameters().Length > 0);

    [Fact]
    public void EveryConstructor_ProducesANonEmptyMessage() =>
        typeof(DomainException).GetConstructors()
            .Select(constructor => (DomainException)constructor.Invoke(
                [.. constructor.GetParameters().Select(
                    parameter => parameter.ParameterType == typeof(string)
                        ? (object)"invariant violated"
                        : new InvalidOperationException("underlying"))]))
            .ShouldAllBe(exception => !string.IsNullOrWhiteSpace(exception.Message));

    [Fact]
    public void TheExceptionType_IsSealed() => typeof(DomainException).IsSealed.ShouldBeTrue();
}
