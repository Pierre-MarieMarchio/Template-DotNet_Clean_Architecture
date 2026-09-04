using AppTemplate.Domain.Common.Exceptions;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Common.Exceptions;

/// <summary>
/// The message is the only thing that tells a caller which invariant was violated, so every way of
/// building this exception has to carry one. Adding a message-less constructor turns the last test red.
/// </summary>
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

    /// <summary>
    /// Sealed because nothing distinguishes one invariant violation from another at the catch site:
    /// the application layer catches this single type and turns its message into a failure result.
    /// </summary>
    [Fact]
    public void TheExceptionType_IsSealed() => typeof(DomainException).IsSealed.ShouldBeTrue();
}
