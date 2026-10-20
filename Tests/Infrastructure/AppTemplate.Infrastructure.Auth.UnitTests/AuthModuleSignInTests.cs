using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests;

public sealed class AuthModuleSignInTests
{
    [Fact]
    public void AddAuthModule_RequiresAConfirmedEmailToSignIn_SoConfirmingOneHasNoSessionToRevoke()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = "Host=nowhere;Database=apptemplate;Username=u;Password=p",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        using var provider = services
            .AddAuthModule(configuration)
            .BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IOptions<IdentityOptions>>().Value.SignIn.RequireConfirmedEmail.ShouldBeTrue(
            "ConfirmEmailUseCase revokes no refresh token on the strength of this setting: an " +
            "unconfirmed account cannot sign in, so it holds no session for a confirmation to end.");
    }
}
