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

    /// <summary>
    /// RFC 9457 §3.1: <c>type</c> identifies the problem, not the status. Two errors with different
    /// codes must resolve to different URIs even when both map to the same HTTP status — the defect
    /// the literal <c>https://httpstatuses.io/{status}</c> had.
    /// </summary>
    [Fact]
    public void For_DistinguishesTwoCodesThatShareOneStatus() =>
        ProblemTypes.For("request.validationFailed").ShouldNotBe(ProblemTypes.For("precondition.malformed"));

    [Fact]
    public void For_IsStable_ForTheSameCode() =>
        ProblemTypes.For("todoList.notFound").ShouldBe(ProblemTypes.For("todoList.notFound"));
}
