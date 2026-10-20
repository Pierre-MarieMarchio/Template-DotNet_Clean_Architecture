using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

/// <summary>
/// The factory's job is to put a user-supplied address into an HTML document. That makes it an
/// injection site, and the encoding is the whole defence.
/// </summary>
public sealed class AccountEmailFactoryConfirmationTests
{
    private const string _confirmationPage = "https://localhost:5001/confirm-email";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateConfirmation_EncodesAnAddressContainingMarkup()
    {
        const string hostile = "<a href=\"https://evil.test\">click me</a>@identity.test";

        var message = await CreateFactory().CreateConfirmationAsync(
            hostile,
            "confirmation-token",
            TestToken);

        message.HtmlBody.ShouldNotContain("<a href=\"https://evil.test\"");
    }

    /// <summary>
    /// The token travels in the link's fragment, which browsers never send to a server, so it stays
    /// out of access logs and <c>Referer</c> headers.
    /// </summary>
    [Fact]
    public async Task CreateConfirmation_PutsTheTokenInTheLinkFragment()
    {
        var message = await CreateFactory().CreateConfirmationAsync(
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
    public async Task CreateConfirmation_TakesItsSubjectFromTheTemplate()
    {
        var message = await CreateFactory().CreateConfirmationAsync(
            "newcomer@identity.test",
            "confirmation-token",
            TestToken);

        message.Subject.ShouldBe("Confirm your email address");
    }

    /// <summary>
    /// <c>OptionsWrapper</c> rather than <c>Options.Create</c>: this project mirrors <c>Src/</c>, so it
    /// has an <c>Options</c> folder of its own and the short name resolves to that namespace.
    /// </summary>
    private static AccountEmailFactory CreateFactory() =>
        new(
            new OptionsWrapper<EmailConfirmationOptions>(new EmailConfirmationOptions
            {
                ConfirmEmailUrl = new Uri(_confirmationPage, UriKind.Absolute),
            }),
            new OptionsWrapper<PasswordResetOptions>(new PasswordResetOptions()),
            new OptionsWrapper<EmailChangeOptions>(new EmailChangeOptions()));
}
