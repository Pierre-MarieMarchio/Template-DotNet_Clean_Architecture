using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

/// <summary>
/// Mirrors <see cref="AccountEmailFactoryConfirmationTests"/>: the factory's job is to put a user-supplied
/// name into an HTML document, which makes it an injection site, and the encoding is the whole
/// defence.
/// </summary>
public sealed class AccountEmailFactoryPasswordResetTests
{
    private const string _resetPage = "https://localhost:5001/reset-password";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatePasswordReset_HtmlEncodesAUserNameContainingMarkup()
    {
        const string hostile = "<a href=\"https://evil.test\">click me</a>";

        var message = await CreateFactory().CreatePasswordResetAsync(
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
    public async Task CreatePasswordReset_PutsTheTokenInTheLinkFragment()
    {
        var message = await CreateFactory().CreatePasswordResetAsync(
            "someone",
            "someone@identity.test",
            "a+token/with=reserved characters",
            TestToken);

        message.HtmlBody.ShouldContain($"{_resetPage}#email=someone%40identity.test&amp;token=");
        message.HtmlBody.ShouldNotContain("a+token/with=reserved characters");
    }

    [Fact]
    public async Task CreatePasswordReset_TakesItsSubjectFromTheTemplate()
    {
        var message = await CreateFactory().CreatePasswordResetAsync(
            "someone",
            "someone@identity.test",
            "reset-token",
            TestToken);

        message.Subject.ShouldBe("Reset your password");
    }

    private static AccountEmailFactory CreateFactory() =>
        new(
            new OptionsWrapper<EmailConfirmationOptions>(new EmailConfirmationOptions()),
            new OptionsWrapper<PasswordResetOptions>(new PasswordResetOptions
            {
                ResetPasswordUrl = new Uri(_resetPage, UriKind.Absolute),
            }),
            new OptionsWrapper<EmailChangeOptions>(new EmailChangeOptions()));
}
