using AppTemplate.Application;
//#if (anyFeature)
using AppTemplate.Application.Core.Common.Ports;
//#endif
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
//#endif
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Presentation.Core;
using AppTemplate.Presentation.Core.Common.Security;
using AppTemplate.PresentationAppPortal.Features.ExternalIdentity.Configuration.Options;
//#if (reminders)
using AppTemplate.PresentationAppPortal.Features.Reminders.Composition;
//#endif
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationAppPortal.Features.ExternalIdentity.Composition;

internal static class ExternalSignInComposition
{
    internal const string SignInPath = "/account/sign-in";

    internal const string SignOutPath = "/account/sign-out";

    internal static IServiceCollection AddExternalSignIn(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddExternalIdentity();
        services.AddExternalIdentityPersistence(configuration);
        //#if (anyFeature)

        services.AddScoped<IUserAccountDirectory, ExternalIdentityUserAccountDirectory>();
        //#endif
        //#if (reminders)
        services.AddScoped<IReminderRecipientDirectory, ExternalIdentityReminderRecipientDirectory>();
        //#endif

        services.AddAuthenticationExternalOptions();
        services.AddOptions<AuthenticationExternalInteractiveOptions>()
            .Bind(configuration.GetSection(AuthenticationExternalInteractiveOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthenticationExternalInteractiveOptions>, AuthenticationExternalInteractiveOptionsValidator>();

        services.AddScoped<ExternalSignInMapping>();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "__Host-session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = false;
            })
            .AddOpenIdConnect();
        services.AddSingleton<IConfigureOptions<OpenIdConnectOptions>, ConfigureExternalOpenIdConnectOptions>();

        services.AddOptions<AuthorizationOptions>()
            .Configure<IOptions<AuthenticationExternalOptions>>((authorization, external) =>
                authorization.AddPolicy(
                    AuthorizationPolicies.Administrator,
                    policy => policy.RequireRole(external.Value.Roles.AdministratorRole)));

        return services;
    }

    internal static IEndpointRouteBuilder MapExternalSignInEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(SignInPath, (string? returnUrl) => Results.Challenge(
            new AuthenticationProperties { RedirectUri = IsLocal(returnUrl) ? returnUrl : "/" },
            [OpenIdConnectDefaults.AuthenticationScheme]));

        endpoints.MapPost(SignOutPath, async (HttpContext context) =>
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                return Results.LocalRedirect("~/");
            })
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute());

        return endpoints;
    }

    private static bool IsLocal(string? url) =>
        url is { Length: > 0 } && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
