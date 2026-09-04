using AppTemplate.Api.Common.Concurrency;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Concurrency;

public sealed class IfMatchPreconditionTests
{
    [Fact]
    public void Read_IsAbsent_WhenNoHeaderIsSent()
    {
        var precondition = IfMatchPrecondition.Read(ARequestWithIfMatch());

        precondition.State.ShouldBe(IfMatchState.Absent);
        precondition.Required.ShouldBeNull();
    }

    [Fact]
    public void Read_IsAny_ForAWildcard()
    {
        var precondition = IfMatchPrecondition.Read(ARequestWithIfMatch("*"));

        precondition.State.ShouldBe(IfMatchState.Any);
        precondition.Required.ShouldBeNull();
    }

    [Fact]
    public void Read_IsTags_ForAKnownStrongEntityTag()
    {
        string tag = EntityTagMapping.From(7);

        var precondition = IfMatchPrecondition.Read(ARequestWithIfMatch(tag));

        precondition.State.ShouldBe(IfMatchState.Tags);
        precondition.Required.ShouldNotBeNull();
        precondition.Required!.IsSatisfiedBy(7).ShouldBeTrue();
    }

    [Fact]
    public void Read_AcceptsSeveralTags_AsOneAcceptableSet()
    {
        string first = EntityTagMapping.From(1);
        string second = EntityTagMapping.From(2);

        var precondition = IfMatchPrecondition.Read(ARequestWithIfMatch($"{first}, {second}"));

        precondition.Required!.IsSatisfiedBy(1).ShouldBeTrue();
        precondition.Required!.IsSatisfiedBy(2).ShouldBeTrue();
        precondition.Required!.IsSatisfiedBy(3).ShouldBeFalse();
    }

    [Theory]
    [InlineData("not-an-etag-at-all")]
    [InlineData("\"unterminated")]
    public void Read_IsMalformed_ForAValueThatIsNeitherAWildcardNorAnEntityTagList(string value)
    {
        var precondition = IfMatchPrecondition.Read(ARequestWithIfMatch(value));

        precondition.State.ShouldBe(IfMatchState.Malformed);
    }

    /// <summary>
    /// Well-formed but naming no version this API could have issued — a failed precondition, not a
    /// malformed request, because ignoring it would turn a conditional write into an unconditional
    /// one.
    /// </summary>
    [Fact]
    public void Read_IsTagsWithAnEmptyAcceptableSet_ForAWeakEntityTag()
    {
        var precondition = IfMatchPrecondition.Read(ARequestWithIfMatch("W/\"anything\""));

        precondition.State.ShouldBe(IfMatchState.Tags);
        precondition.Required!.AcceptableVersions.ShouldBeEmpty();
    }

    private static HttpRequest ARequestWithIfMatch(string? value = null)
    {
        var context = new DefaultHttpContext();

        if (value is not null)
        {
            context.Request.Headers.IfMatch = value;
        }

        return context.Request;
    }
}
