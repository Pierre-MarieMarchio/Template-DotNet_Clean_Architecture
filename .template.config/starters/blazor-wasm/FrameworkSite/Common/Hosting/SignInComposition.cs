using AppTemplate.Client.Common.Sessions;
using AppTemplate.FrameworkSite.Common.Sessions;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Options;

namespace AppTemplate.FrameworkSite.Common.Hosting;

public static class SignInComposition
{
    public const string AuthorizationCodeFlow = "code";

    public static IServiceCollection AddSignIn(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<SignInOptions>, SignInOptionsValidator>();
        services.AddOptions<SignInOptions>()
            .Bind(configuration.GetSection(SignInOptions.SectionName))
            .ValidateOnStart();

        services.AddOidcAuthentication(authentication => authentication.ProviderOptions.ResponseType = AuthorizationCodeFlow);
        services.AddOptions<RemoteAuthenticationOptions<OidcProviderOptions>>()
            .Configure<IOptions<SignInOptions>>((authentication, signIn) => DescribeTheProvider(authentication.ProviderOptions, signIn.Value));

        services.AddScoped<IAccessTokenSource, ProviderAccessTokenSource>();

        return services;
    }

    private static void DescribeTheProvider(OidcProviderOptions provider, SignInOptions signIn)
    {
        provider.Authority = signIn.Authority?.OriginalString;
        provider.ClientId = signIn.ClientId;
        provider.DefaultScopes.Clear();

        foreach (string scope in SignInOptionsValidator.Scopes(signIn))
        {
            provider.DefaultScopes.Add(scope);
        }
    }
}
