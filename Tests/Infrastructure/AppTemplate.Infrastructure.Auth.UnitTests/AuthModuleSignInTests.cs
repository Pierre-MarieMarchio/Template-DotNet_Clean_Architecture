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
        using var provider = BuildProvider();

        provider.GetRequiredService<IOptions<IdentityOptions>>().Value.SignIn.RequireConfirmedEmail.ShouldBeTrue(
            "ConfirmEmailUseCase revokes no refresh token on the strength of this setting: an " +
            "unconfirmed account cannot sign in, so it holds no session for a confirmation to end.");
    }

    [Fact]
    public void AddAuthModule_RefusesToTurnOffTheConfirmedEmailRequirement()
    {
        using var provider = BuildProvider(("Identity:RequireConfirmedEmail", "false"));

        var refusal = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<IdentityOptions>>().Value);

        refusal.Message.ShouldContain("'Identity:RequireConfirmedEmail' cannot be disabled");
    }

    private static ServiceProvider BuildProvider(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Default"] = "Host=nowhere;Database=apptemplate;Username=u;Password=p",
        };
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        var services = new ServiceCollection();
        services.AddLogging();

        return services
            .AddAuthModule(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .BuildServiceProvider(validateScopes: true);
    }
}
