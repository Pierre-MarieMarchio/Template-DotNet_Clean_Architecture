using AppTemplate.Api.Common.Errors;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Errors;

public sealed class ProblemTypesTests
{
    [Fact]
    public void For_UsesTheDefaultBaseUri_WhenNoneIsGiven() =>
        ProblemTypes.For("todoList.notFound").ShouldBe($"{ProblemTypes.DefaultBaseUri}/todoList.notFound");

    [Fact]
    public void For_UsesTheGivenBaseUri() =>
        ProblemTypes.For("todoList.notFound", "https://example.org/problems")
            .ShouldBe("https://example.org/problems/todoList.notFound");

    [Fact]
    public void For_TrimsATrailingSlashOnTheBaseUri_SoTheResultNeverDoublesIt() =>
        ProblemTypes.For("todoList.notFound", "https://example.org/problems/")
            .ShouldBe("https://example.org/problems/todoList.notFound");

    [Fact]
    public void For_DistinguishesTwoCodesThatShareOneStatus() =>
        ProblemTypes.For("request.validationFailed").ShouldNotBe(ProblemTypes.For("precondition.malformed"));

    [Fact]
    public void For_IsStable_ForTheSameCode() =>
        ProblemTypes.For("todoList.notFound").ShouldBe(ProblemTypes.For("todoList.notFound"));
}
