using AppTemplate.Api.Common.Localization;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Presentation.Core.Common.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Localization;

public sealed class RequestLanguageExtensionsTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("fr-CA", "fr-CA")]
    public async Task ATagWithoutWeights_BecomesTheAmbientTag(string header, string? expected) =>
        (await TagAfterAsync(header)).ShouldBe(expected);

    [Theory]
    [InlineData("fr;q=0.9,en;q=1.0", "en")]
    [InlineData("fr;q=0.5,en", "en")]
    [InlineData("fr;q=0.9,de;q=0.3,en-GB;q=0.95", "en-GB")]
    public async Task TheHighestWeightWinsWhereverItSits_AndAMissingWeightIsOne(string header, string? expected) =>
        (await TagAfterAsync(header)).ShouldBe(expected);

    [Theory]
    [InlineData("en;q=0.8,fr;q=0.8", "en")]
    [InlineData("fr,en", "fr")]
    public async Task EqualWeightsKeepTheOrderTheCallerWrote(string header, string? expected) =>
        (await TagAfterAsync(header)).ShouldBe(expected);

    [Theory]
    [InlineData("fr;q=0,en;q=0.1", "en")]
    [InlineData("fr;q=0", null)]
    public async Task AZeroWeightMeansNotAcceptable_SoThatTagIsNeverChosen(string header, string? expected) =>
        (await TagAfterAsync(header)).ShouldBe(expected);

    [Theory]
    [InlineData("fr;q=abc,en;q=0.2", "en")]
    [InlineData("fr;q=1.5,en;q=0.2", "en")]
    [InlineData("fr_CA;q=1,en;q=0.2", "en")]
    [InlineData("not a tag,en;q=0.2", "en")]
    [InlineData("  fr  , en", "fr")]
    public async Task AnEntryWithAnInvalidWeightOrAMalformedTag_IsSkipped_AndTheRestStillCounts(string header, string? expected) =>
        (await TagAfterAsync(header)).ShouldBe(expected);

    [Theory]
    [InlineData("*", null)]
    [InlineData("*,fr", "fr")]
    public async Task AWildcardIsNotALanguage(string header, string? expected) =>
        (await TagAfterAsync(header)).ShouldBe(expected);

    [Theory]
    [InlineData("")]
    [InlineData("not a tag")]
    [InlineData("fr_CA")]
    public async Task AMalformedHeader_LeavesNoTag(string header) =>
        (await TagAfterAsync(header)).ShouldBeNull();

    [Fact]
    public async Task AnAbsurdlyLongHeader_IsIgnoredEntirely() =>
        (await TagAfterAsync("fr," + string.Join(",", Enumerable.Repeat("en-GB", 100)))).ShouldBeNull();

    [Fact]
    public async Task ARequestWithNoHeader_FallsBackToTheHostsDefault()
    {
        (await TagAfterAsync(header: null)).ShouldBeNull(
            "nothing was named, so nothing is set — and CurrentLanguage.Current answers the host's "
            + "default rather than leaving a renderer with no language at all");

        CurrentLanguage.Current.ShouldBe(CurrentLanguage.Default);
    }

    private static async Task<string?> TagAfterAsync(string? header)
    {
        var services = new ServiceCollection();
        services.AddOptions<LocalizationOptions>();

        string? seenDownstream = null;

        var app = new ApplicationBuilder(services.BuildServiceProvider());
        app.UseRequestLanguage();
        app.Run(_ =>
        {
            seenDownstream = CurrentLanguage.Tag;

            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();

        if (header is not null)
        {
            context.Request.Headers.AcceptLanguage = header;
        }

        await app.Build()(context);

        return seenDownstream;
    }
}
