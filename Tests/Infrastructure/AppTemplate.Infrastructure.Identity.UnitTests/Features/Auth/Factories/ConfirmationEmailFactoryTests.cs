using AppTemplate.Infrastructure.Identity.Features.Auth.Factories;
using AppTemplate.Infrastructure.Identity.Features.Auth.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Identity.UnitTests.Features.Auth.Factories;

/// <summary>
/// The factory's job is to put a user-supplied name into an HTML document. That makes it an
/// injection site, and the encoding is the whole defence.
/// </summary>
public sealed class ConfirmationEmailFactoryTests
{
    private const string _confirmationPage = "https://localhost:5001/confirm-email";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// A username is whatever the user typed. Substituted raw it would let anyone deliver markup —
    /// an anchor pointing anywhere — inside a mail sent from this domain.
    /// </summary>
    [Fact]
    public async Task ComposeAsync_HtmlEncodesAUserNameContainingMarkup()
    {
        const string hostile = "<a href=\"https://evil.test\">click me</a>";

        var message = await CreateFactory().CreateAsync(
            hostile,
            "victim@identity.test",
            "confirmation-token",
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
            "newcomer",
            "newcomer@identity.test",
            "a+token/with=reserved characters",
            TestToken);

        message.HtmlBody.ShouldContain($"{_confirmationPage}#email=newcomer%40identity.test&amp;token=");
        message.HtmlBody.ShouldNotContain("a+token/with=reserved characters");
    }

    /// <summary>
    /// The subject comes from configuration and is handed back rather than applied by the factory,
    /// because the caller is what sends the message.
    /// </summary>
    [Fact]
    public async Task ComposeAsync_CarriesTheConfiguredSubject()
    {
        var message = await CreateFactory().CreateAsync(
            "newcomer",
            "newcomer@identity.test",
            "confirmation-token",
            TestToken);

        message.Subject.ShouldBe("Confirm your email address");
    }

    /// <summary>
    /// <c>OptionsWrapper</c> rather than <c>Options.Create</c>: this project mirrors <c>Src/</c>, so it
    /// has an <c>Options</c> folder of its own and the short name resolves to that namespace.
    /// </summary>
    private static ConfirmationEmailFactory CreateFactory() =>
        new(new OptionsWrapper<EmailConfirmationOptions>(new EmailConfirmationOptions
        {
            ConfirmEmailUrl = new Uri(_confirmationPage, UriKind.Absolute),
            Subject = "Confirm your email address",
        }));
}
