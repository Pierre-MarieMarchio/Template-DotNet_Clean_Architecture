using AppTemplate.Api.Common.Concurrency;
using Microsoft.Net.Http.Headers;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Concurrency;

public sealed class EntityTagMappingTests
{
    [Fact]
    public void From_ProducesAQuotedStrongTag()
    {
        string tag = EntityTagMapping.From(42);

        tag.ShouldStartWith("\"");
        tag.ShouldEndWith("\"");
        tag.StartsWith("W/", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(42u)]
    [InlineData(uint.MaxValue)]
    public void TryReadVersion_RoundTripsWhatFromEncoded(uint version)
    {
        string tag = EntityTagMapping.From(version);
        var parsed = EntityTagHeaderValue.Parse(tag);

        bool ok = EntityTagMapping.TryReadVersion(parsed, out uint decoded);

        ok.ShouldBeTrue();
        decoded.ShouldBe(version);
    }

    [Fact]
    public void TryReadVersion_RejectsAWeakTag()
    {
        string strong = EntityTagMapping.From(1);
        var weak = EntityTagHeaderValue.Parse("W/" + strong);

        EntityTagMapping.TryReadVersion(weak, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryReadVersion_RejectsATagThisApiNeverIssued()
    {
        var tag = new EntityTagHeaderValue("\"not-base64url!!\"");

        EntityTagMapping.TryReadVersion(tag, out _).ShouldBeFalse();
    }

    [Fact]
    public void TwoDifferentVersions_ProduceDifferentTags() =>
        EntityTagMapping.From(1).ShouldNotBe(EntityTagMapping.From(2));
}
