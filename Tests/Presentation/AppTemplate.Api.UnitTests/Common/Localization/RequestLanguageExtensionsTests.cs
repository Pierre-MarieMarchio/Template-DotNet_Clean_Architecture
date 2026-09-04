using AppTemplate.Api.Common.Localization;
using AppTemplate.Application.Common.Localization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Localization;

/// <summary>
/// What a caller's <c>Accept-Language</c> turns into, which is what decides the language of every
/// mail their request causes.
/// </summary>
public sealed class RequestLanguageExtensionsTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("fr", "fr")]
    [InlineData("fr-CA", "fr-CA")]
    // Quality values are parsed off and the first tag wins: a mail is written in one language.
    [InlineData("fr;q=0.9,en;q=1.0", "fr")]
    [InlineData("  fr  , en", "fr")]
    // A wildcard says "anything", which is not a language.
    [InlineData("*", null)]
    [InlineData("*,fr", "fr")]
    public async Task TheHeader_BecomesTheAmbientTag(string header, string? expected)
    {
        (await TagAfterAsync(header)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a tag")]
    [InlineData("fr_CA")]
    public async Task AMalformedHeader_LeavesNoTag(string header) =>
        (await TagAfterAsync(header)).ShouldBeNull();

    /// <summary>
    /// The header is a caller's to write, including its length. Refusing an absurd one keeps this
    /// from being a way to make the process split and scan a large string on every request.
    /// </summary>
    [Fact]
    public async Task AnAbsurdlyLongHeader_IsIgnoredEntirely() =>
        (await TagAfterAsync("fr," + string.Join(",", Enumerable.Repeat("en-GB", 100)))).ShouldBeNull();

    /// <summary>
    /// With no tag of its own the request still has an answer, and it is the host's — which is what
    /// <c>CurrentLanguage.Current</c> falls back to rather than leaving a renderer with nothing.
    /// </summary>
    [Fact]
    public async Task ARequestWithNoHeader_FallsBackToTheHostsDefault()
    {
        (await TagAfterAsync(header: null)).ShouldBeNull(
            "nothing was named, so nothing is set — and CurrentLanguage.Current answers the host's "
            + "default rather than leaving a renderer with no language at all");

        CurrentLanguage.Current.ShouldBe(CurrentLanguage.Default);
    }

    /// <summary>
    /// Read from <em>inside</em> the pipeline, which is the only place it can be read: an
    /// <see cref="AsyncLocal{T}"/> set in a middleware flows down to everything that middleware
    /// awaits and never back out to its caller. That is exactly the property this design needs —
    /// the controller, the use case and the mail factory all run downstream — and asserting it from
    /// outside would have been asserting the wrong thing.
    /// </summary>
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
