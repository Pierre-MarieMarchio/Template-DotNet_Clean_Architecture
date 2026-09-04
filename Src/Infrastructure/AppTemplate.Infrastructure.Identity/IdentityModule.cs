using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports;
using AppTemplate.Infrastructure.Identity.Bearer;
using AppTemplate.Infrastructure.Identity.Notifications;
using AppTemplate.Infrastructure.Identity.Options;
using AppTemplate.Infrastructure.Identity.Tokens;
using AppTemplate.Infrastructure.Identity.Users;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Identity;

/// <summary>
/// Composes the identity module: who a caller is, how that is proved, and how long the proof
/// lasts. Its one reason to change is authentication policy.
/// <para>
/// <b>It owns no database.</b> The store — the account, role and grant tables, and the
/// <c>DbContext</c> that maps them — lives in <c>AppTemplate.Infrastructure.Persistence</c>, so this project has
/// no EF provider reference, no migrations and no design-time factory. What lives here is everything
/// that talks to ASP.NET Identity: password and lockout policy, bearer validation, access-token
/// signing, and refresh-token rotation. ASP.NET Identity's stores are pointed at the shared
/// <see cref="AppDbContext"/>.
/// </para>
/// <para>
/// It sends no mail. <see cref="ConfirmationEmailComposer"/> renders the confirmation message and
/// hands it back; the use case delivers it through <see cref="IEmailSender"/>, which the host
/// supplies by composing <c>AppTemplate.Infrastructure.Email</c> or <c>AppTemplate.Infrastructure.InMemory</c>.
/// </para>
/// <para>
/// It makes no authentication decision either. Each registration below satisfies one narrow
/// capability port, and the sequencing — what a refusal means, when a token is minted, whether a
/// delivery failure fails the request — belongs to the use cases in <c>AppTemplate.Application</c>.
/// </para>
/// </summary>
public static class IdentityModule
{
    /// <summary>
    /// Registers ASP.NET Identity itself, bearer validation, and an adapter for each authentication
    /// port the application layer declares.
    /// <para>
    /// Every configuration section is bound to an options type with safe defaults and a
    /// validator that runs at start-up, so the process refuses to boot on a missing signing
    /// key or a password policy weaker than the hard floor — rather than failing at the first
    /// login attempt, or not failing at all.
    /// </para>
    /// <para>
    /// Idempotent, like <c>AddPersistenceModule</c>: a second call adds nothing. Without the guard it
    /// would compose ASP.NET Identity twice and leave a duplicate of every adapter, so the container
    /// would resolve one of two equivalent registrations and be a little harder to reason about for
    /// no reason at all.
    /// </para>
    /// </summary>
    /// <param name="services">The container being composed.</param>
    /// <param name="configuration">Must supply the <c>Default</c> connection string, which this module
    /// forwards to the persistence module rather than opening a connection of its own.</param>
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // One guard at the top rather than a Try- form per registration, for the same reason the
        // persistence module uses one: AddIdentity and AddAuthentication have no Try- form, and would
        // duplicate a schema and a whole store on a second call.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IUserAccounts)))
        {
            return services;
        }

        AddValidatedOptions(services, configuration);

        // Idempotent, and called here rather than assumed: this module needs the shared context, the
        // clock and the refresh-token store, so it says so instead of depending on the host having
        // composed them first.
        services.AddPersistenceModule(configuration);

        // One adapter per capability port. Nothing in this module depends on a concrete class, so
        // replacing one is a single line here.
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<IEmailConfirmationTokens, EmailConfirmationTokens>();
        services.AddScoped<IAccessTokenIssuer, AccessTokenIssuer>();
        services.AddScoped<IRefreshTokenGrants, RefreshTokenGrants>();
        services.AddScoped<IConfirmationEmailComposer, ConfirmationEmailComposer>();

        // Not a port: the account lookup and claim generation the two token adapters share.
        services.AddScoped<IAppUserDirectory, AppUserDirectory>();

        AddIdentityCore(services);
        AddJwtBearerAuthentication(services);

        return services;
    }

    private static void AddValidatedOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

        services.AddOptions<IdentityPolicyOptions>()
            .Bind(configuration.GetSection(IdentityPolicyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityPolicyOptions>, IdentityPolicyOptionsValidator>();

        services.AddOptions<RefreshTokenOptions>()
            .Bind(configuration.GetSection(RefreshTokenOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RefreshTokenOptions>, RefreshTokenOptionsValidator>();

        services.AddOptions<EmailConfirmationOptions>()
            .Bind(configuration.GetSection(EmailConfirmationOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailConfirmationOptions>, EmailConfirmationOptionsValidator>();

        // IdentitySeedOptions is deliberately absent: seeding is a persistence concern and the
        // persistence module binds and validates that section. The section name is unchanged.
    }

    private static void AddIdentityCore(IServiceCollection services)
    {
        services.AddIdentity<AppUser, AppRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        // Applied after AddIdentity's own defaults, and sourced from validated options rather than
        // from a section read eagerly at composition time.
        services.AddOptions<IdentityOptions>()
            .Configure<IOptions<IdentityPolicyOptions>>((identity, policyAccessor) =>
            {
                var policy = policyAccessor.Value;

                identity.User.RequireUniqueEmail = policy.RequireUniqueEmail;
                identity.SignIn.RequireConfirmedAccount = false;
                identity.SignIn.RequireConfirmedEmail = policy.RequireConfirmedEmail;

                identity.Password.RequiredLength = policy.EffectivePasswordRequiredLength;
                identity.Password.RequiredUniqueChars = policy.PasswordRequiredUniqueChars;
                identity.Password.RequireDigit = policy.PasswordRequireDigit;
                identity.Password.RequireLowercase = policy.PasswordRequireLowercase;
                identity.Password.RequireUppercase = policy.PasswordRequireUppercase;
                identity.Password.RequireNonAlphanumeric = policy.PasswordRequireNonAlphanumeric;

                // Lockout was never configured, and CheckPasswordSignInAsync was called with
                // lockoutOnFailure: false, so AccessFailedCount never moved and password guessing
                // was unbounded.
                identity.Lockout.AllowedForNewUsers = policy.LockoutEnabled;
                identity.Lockout.MaxFailedAccessAttempts = policy.LockoutMaxFailedAccessAttempts;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(policy.LockoutDurationInMinutes);
            });
    }

    private static void AddJwtBearerAuthentication(IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer();

        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();
    }
}
