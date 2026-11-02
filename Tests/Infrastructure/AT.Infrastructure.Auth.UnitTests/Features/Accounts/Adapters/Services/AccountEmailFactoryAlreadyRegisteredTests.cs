using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests.Features.Accounts.Adapters.Services;

public sealed class AccountEmailFactoryAlreadyRegisteredTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateAlreadyRegistered_TakesItsSubjectFromTheTemplate()
    {
        var message = await CreateFactory().CreateAlreadyRegisteredAsync("someone@identity.test", TestToken);

        message.Subject.ShouldBe("You already have an account");
    }

    [Fact]
    public async Task CreateAlreadyRegistered_CarriesNoLinkAndNoUnfilledPlaceholder()
    {
        var message = await CreateFactory().CreateAlreadyRegisteredAsync("someone@identity.test", TestToken);

        message.HtmlBody.ShouldNotContain("href", Case.Insensitive);
        message.HtmlBody.ShouldNotContain("{{");
    }

    private static AccountEmailFactory CreateFactory() =>
        new(
            new OptionsWrapper<EmailConfirmationOptions>(new EmailConfirmationOptions()),
            new OptionsWrapper<PasswordResetOptions>(new PasswordResetOptions()),
            new OptionsWrapper<EmailChangeOptions>(new EmailChangeOptions()));
}
