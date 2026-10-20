using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

/// <summary>
/// Mirrors <see cref="AccountEmailFactoryPasswordResetTests"/>: the factory's job is to put a
/// user-supplied name into an HTML document, which makes it an injection site, and the encoding is
/// the whole defence.
/// </summary>
public sealed class AccountEmailFactoryEmailChangeTests
{
    private const string _confirmPage = "https://localhost:5001/confirm-email-change";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateEmailChange_EncodesAnAddressContainingMarkup()
    {
        const string hostile = "<a href=\"https://evil.test\">click me</a>@identity.test";

        var message = await CreateFactory().CreateEmailChangeAsync(
            hostile,
            "change-token",
            TestToken);

        message.HtmlBody.ShouldNotContain("<a href=\"https://evil.test\"");
    }

    /// <summary>
    /// The token travels in the link's fragment, which browsers never send to a server, so it stays
    /// out of access logs and <c>Referer</c> headers.
    /// </summary>
    [Fact]
    public async Task CreateEmailChange_PutsTheTokenInTheLinkFragment()
    {
        var message = await CreateFactory().CreateEmailChangeAsync(
            "someone.new@identity.test",
            "a+token/with=reserved characters",
            TestToken);

        message.HtmlBody.ShouldContain($"{_confirmPage}#email=someone.new%40identity.test&amp;token=");
        message.HtmlBody.ShouldNotContain("a+token/with=reserved characters");
    }

    [Fact]
    public async Task CreateEmailChange_TakesItsSubjectFromTheTemplate()
    {
        var message = await CreateFactory().CreateEmailChangeAsync(
            "someone.new@identity.test",
            "change-token",
            TestToken);

        message.Subject.ShouldBe("Confirm your new email address");
    }

    private static AccountEmailFactory CreateFactory() =>
        new(
            new OptionsWrapper<EmailConfirmationOptions>(new EmailConfirmationOptions()),
            new OptionsWrapper<PasswordResetOptions>(new PasswordResetOptions()),
            new OptionsWrapper<EmailChangeOptions>(new EmailChangeOptions
            {
                ConfirmEmailChangeUrl = new Uri(_confirmPage, UriKind.Absolute),
            }));
}
