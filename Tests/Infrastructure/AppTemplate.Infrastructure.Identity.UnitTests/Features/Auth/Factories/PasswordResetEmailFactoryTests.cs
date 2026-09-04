using AppTemplate.Infrastructure.Identity.Features.Auth.Factories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Features.Auth.Factories;

/// <summary>
/// Mirrors <see cref="ConfirmationEmailFactoryTests"/>: the factory's job is to put a user-supplied
/// name into an HTML document, which makes it an injection site, and the encoding is the whole
/// defence.
/// </summary>
public sealed class PasswordResetEmailFactoryTests
{
    private const string _resetPage = "https://localhost:5001/reset-password";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ComposeAsync_HtmlEncodesAUserNameContainingMarkup()
    {
        const string hostile = "<a href=\"https://evil.test\">click me</a>";

        var message = await CreateFactory().CreateAsync(
            hostile,
            "victim@identity.test",
            "reset-token",
            TestToken);

        message.HtmlBody.ShouldNotContain("<a href=\"https://evil.test\"");
        message.HtmlBody.ShouldContain("&lt;a href=&quot;https://evil.test&quot;&gt;click me&lt;/a&gt;");
    }

    /// <summary>
    /// The token travels in the link's fragment, which browsers never send to a server, so it stays
    /// out of access logs and <c>Referer</c> headers.
    /// </summary>
    [Fact]
    public async Task ComposeAsync_PutsTheTokenInTheLinkFragment()
    {
        var message = await CreateFactory().CreateAsync(
            "someone",
            "someone@identity.test",
            "a+token/with=reserved characters",
            TestToken);

        message.HtmlBody.ShouldContain($"{_resetPage}#email=someone%40identity.test&amp;token=");
        message.HtmlBody.ShouldNotContain("a+token/with=reserved characters");
    }

    [Fact]
    public async Task ComposeAsync_TakesItsSubjectFromTheTemplate()
    {
        var message = await CreateFactory().CreateAsync(
            "someone",
            "someone@identity.test",
            "reset-token",
            TestToken);

        message.Subject.ShouldBe("Reset your password");
    }

    private static PasswordResetEmailFactory CreateFactory() =>
        new(new OptionsWrapper<PasswordResetOptions>(new PasswordResetOptions
        {
            ResetPasswordUrl = new Uri(_resetPage, UriKind.Absolute),
        }));
}
