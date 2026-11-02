using AppTemplate.Application.Core.Common.Concurrency;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.Core.UnitTests.Common.Concurrency;

public sealed class VersionPreconditionTests
{
    private const uint _storedVersion = 4242;

    [Fact]
    public void AVersionInTheSet_SatisfiesThePrecondition() =>
        new VersionPrecondition([1, 2, 3]).IsSatisfiedBy(2).ShouldBeTrue();

    [Fact]
    public void AVersionOutsideTheSet_DoesNot() =>
        new VersionPrecondition([1, 2, 3]).IsSatisfiedBy(4).ShouldBeFalse();

    [Fact]
    public void AnEmptySet_SatisfiesNothing()
    {
        var precondition = new VersionPrecondition([]);

        precondition.IsSatisfiedBy(0).ShouldBeFalse();
        precondition.IsSatisfiedBy(_storedVersion).ShouldBeFalse();
    }
}
