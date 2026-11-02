using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

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

    [Fact]
    public async Task CreateConfirmation_TakesItsSubjectFromTheTemplate()
    {
        var message = await CreateFactory().CreateConfirmationAsync(
            "newcomer@identity.test",
            "confirmation-token",
            TestToken);

        message.Subject.ShouldBe("Confirm your email address");
    }

    private static AccountEmailFactory CreateFactory() =>
        new(
            new OptionsWrapper<EmailConfirmationOptions>(new EmailConfirmationOptions
            {
                ConfirmEmailUrl = new Uri(_confirmationPage, UriKind.Absolute),
            }),
            new OptionsWrapper<PasswordResetOptions>(new PasswordResetOptions()),
            new OptionsWrapper<EmailChangeOptions>(new EmailChangeOptions()));
}
