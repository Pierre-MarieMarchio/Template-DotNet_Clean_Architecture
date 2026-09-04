using AppTemplate.Infrastructure.Identity.EmailChange;
using AppTemplate.Infrastructure.Identity.UnitTests.PasswordReset;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.EmailChange;

/// <summary>
/// Mirrors <see cref="PasswordResetEmailFactoryTests"/>: the factory's job is to put a
/// user-supplied name into an HTML document, which makes it an injection site, and the encoding is
/// the whole defence.
/// </summary>
public sealed class EmailChangeEmailFactoryTests
{
    private const string _confirmPage = "https://localhost:5001/confirm-email-change";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ComposeAsync_HtmlEncodesAUserNameContainingMarkup()
    {
        const string hostile = "<a href=\"https://evil.test\">click me</a>";

        var message = await CreateFactory().CreateAsync(
            hostile,
            "victim@identity.test",
            "change-token",
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
            "someone.new@identity.test",
            "a+token/with=reserved characters",
            TestToken);

        message.HtmlBody.ShouldContain($"{_confirmPage}#email=someone.new%40identity.test&amp;token=");
        message.HtmlBody.ShouldNotContain("a+token/with=reserved characters");
    }

    [Fact]
    public async Task ComposeAsync_CarriesTheConfiguredSubject()
    {
        var message = await CreateFactory().CreateAsync(
            "someone",
            "someone.new@identity.test",
            "change-token",
            TestToken);

        message.Subject.ShouldBe("Confirm your new email address");
    }

    private static EmailChangeEmailFactory CreateFactory() =>
        new(new OptionsWrapper<EmailChangeOptions>(new EmailChangeOptions
        {
            ConfirmEmailChangeUrl = new Uri(_confirmPage, UriKind.Absolute),
            Subject = "Confirm your new email address",
        }));
}
