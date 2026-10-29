using AppTemplate.Application.Auth;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
//#if (anyFeature)
using AppTemplate.Application.Core.Common.Ports;
//#endif
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
//#endif
using AppTemplate.Infrastructure.Auth;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Seeding;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;
using AppTemplate.Presentation.Core.Common.Security;
using AppTemplate.PresentationApp.Common.Hosting;
//#if (reminders)
using AppTemplate.PresentationApp.Features.Reminders.Composition;
//#endif
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

namespace AppTemplate.PresentationApp.Features.Auth.Composition;

internal static class CookieSessionComposition
{
    internal const string SignInPath = "/account/sign-in";

    internal const string SignOutPath = "/account/sign-out";

    internal static IServiceCollection AddCookieSessions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAuthApplication();

        services.AddScoped<ISecurityEventOriginProvider, HttpContextSecurityEventOriginProvider>();
        services.AddScoped<ICurrentSession, CurrentSession>();

        services.AddAuthModule(configuration);
        //#if (anyFeature)

        services.AddScoped<IUserAccountDirectory, AccountUserAccountDirectory>();
        //#endif
        //#if (reminders)
        services.AddScoped<IReminderRecipientDirectory, AccountReminderRecipientDirectory>();
        //#endif

        services.AddHealthChecks()
            .AddDbContextCheck<AuthDbContext>(name: "auth-database", tags: [HealthEndpoints.ReadyTag]);

        services.AddSingleton<ActiveSessionProvider>();
        services.AddScoped<CookieSession>();
        services.AddScoped<AuthenticationStateProvider, SessionRevalidatingStateProvider>();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = "__Host-session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = false;
                options.LoginPath = SignInPath;
                options.Events.OnValidatePrincipal = context => context.HttpContext.RequestServices
                    .GetRequiredService<ActiveSessionProvider>()
                    .ValidateCookieAsync(context);
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Administrator, policy => policy.RequireRole(IdentityRoles.Administrator));

        return services;
    }

    internal static IEndpointRouteBuilder MapCookieSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost(SignOutPath, async (HttpContext context, CookieSession session) =>
            {
                await session.SignOutAsync(context);

                return Results.LocalRedirect("~/");
            })
            .RequireAuthorization()
            .WithMetadata(new RequireAntiforgeryTokenAttribute());

        return endpoints;
    }
}
