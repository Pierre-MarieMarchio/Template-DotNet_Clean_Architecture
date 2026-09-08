using AppTemplate.Application.Auth.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Application.Auth.Features.Auth.Ports.AccountDeletion;
using AppTemplate.Application.Auth.Features.Auth.Ports.AccountLockouts;
using AppTemplate.Application.Auth.Features.Auth.Ports.ConfirmationEmailFactory;
using AppTemplate.Application.Auth.Features.Auth.Ports.EmailChangeEmailFactory;
using AppTemplate.Application.Auth.Features.Auth.Ports.EmailChangeTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalIdentity;
using AppTemplate.Application.Auth.Features.Auth.Ports.ExternalLogins;
using AppTemplate.Application.Auth.Features.Auth.Ports.PasswordResetEmailFactory;
using AppTemplate.Application.Auth.Features.Auth.Ports.PasswordResetTokens;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Auth.Features.Auth.Ports.RefreshTokenMaintenance;
using AppTemplate.Application.Auth.Features.Auth.Ports.RoleAssignments;
using AppTemplate.Application.Auth.Features.Auth.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorAdministration;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorChallenge;
using AppTemplate.Application.Auth.Features.Auth.Ports.TwoFactorEnrollment;
using AppTemplate.Application.Auth.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Auth.Features.Auth.Ports.UserProfiles;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Common.Directories;
using AppTemplate.Infrastructure.Auth.Common.Options;
using AppTemplate.Infrastructure.Auth.Features.Auth.Directories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Factories;
using AppTemplate.Infrastructure.Auth.Features.Auth.Issuers;
using AppTemplate.Infrastructure.Auth.Features.Auth.Logs;
using AppTemplate.Infrastructure.Auth.Features.Auth.Models;
using AppTemplate.Infrastructure.Auth.Features.Auth.Options;
using AppTemplate.Infrastructure.Auth.Features.Auth.Providers;
using AppTemplate.Infrastructure.Auth.Features.Auth.Seeding;
using AppTemplate.Infrastructure.Auth.Features.Auth.Services;
using AppTemplate.Infrastructure.Auth.Features.Auth.Tables;
using AppTemplate.Infrastructure.Auth.Features.Auth.Verifiers;
using AppTemplate.Infrastructure.Core;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AppTemplate.Infrastructure.Auth;

/// <summary>
/// Composes the authentication module: who a caller is, how that is proved, how long the proof
/// lasts, and the tables that record it. Its one reason to change is authentication policy.
/// <para>
/// <b>It owns its store.</b> The account, role and grant tables, the data-protection key ring and
/// the <see cref="AuthDbContext"/> that maps them are this project's, with their own migrations
/// history in their own schema — which is why it references an EF provider and a design-time
/// factory. Beside that sits everything that talks to ASP.NET Identity: password and lockout
/// policy, bearer validation, access-token signing, and refresh-token rotation.
/// </para>
/// <para>
/// It names no other module. What its context saves through — the unit of work, the interceptors,
/// the clock — comes from <c>AppTemplate.Infrastructure.Core</c>, which the business half composes
/// the same way, so neither module has to know the other exists.
/// </para>
/// <para>
/// It sends no mail. <see cref="ConfirmationEmailFactory"/> renders the confirmation message and
/// hands it back; the use case delivers it through <see cref="IEmailSender"/>, which the host
/// supplies by composing <c>AppTemplate.Infrastructure.Email</c> or <c>AppTemplate.Infrastructure.InMemory</c>.
/// </para>
/// <para>
/// It makes no authentication decision either. Each registration below satisfies one narrow
/// capability port, and the sequencing — what a refusal means, when a token is minted, whether a
/// delivery failure fails the request — belongs to the use cases in <c>AppTemplate.Application</c>.
/// </para>
/// </summary>
public static class AuthModule
{
    /// <summary>
    /// The key ring's isolation namespace. Shared between every call to <c>AddDataProtection</c>
    /// this process makes, so a second one — a test host, say — cannot end up unkeyed by naming a
    /// different string.
    /// </summary>
    internal const string DataProtectionApplicationName = "AppTemplate";

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
    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // One guard at the top rather than a Try- form per registration, for the same reason the
        // persistence module uses one: AddIdentity and AddAuthentication have no Try- form, and would
        // duplicate a schema and a whole store on a second call.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IUserAccountsService)))
        {
            return services;
        }

        AddValidatedOptions(services, configuration);

        // This module's own storage: its context, what that context saves through, and the clock the
        // rest of it reads. Called here rather than assumed, so the module says what it needs instead
        // of depending on the host having composed it first.
        AddSeedingOptions(services, configuration);
        AddDatabaseOptions(services, configuration);
        AddContext(services, DefaultConnectionString.Require(configuration));
        services.AddCoreSaving<AuthDbContext>();
        services.AddSystemClock();

        services.TryAddScoped<IRefreshTokenTable, RefreshTokenTable>();

        // Constructible only once ASP.NET Identity is composed below, because seeding an account
        // means hashing a password and generating a security stamp rather than writing a row.
        services.TryAddScoped<IIdentitySeeder, IdentitySeeder>();

        // One adapter per capability port. Nothing in this module depends on a concrete class, so
        // replacing one is a single line here.
        services.AddScoped<IUserAccountsService, UserAccountsService>();
        services.AddScoped<IUserProfilesService, UserProfilesService>();
        services.AddScoped<IAccountLockoutsService, AccountLockoutsService>();
        services.AddScoped<IRoleAssignmentsService, RoleAssignmentsService>();
        services.AddScoped<IAccountDeletionService, AccountDeletionService>();
        services.AddScoped<IEmailConfirmationTokensService, EmailConfirmationTokensService>();
        services.AddScoped<IPasswordResetTokensService, PasswordResetTokensService>();
        services.AddScoped<IEmailChangeTokensService, EmailChangeTokensService>();
        services.AddScoped<IAccessTokenIssuer, AccessTokenIssuer>();
        services.AddScoped<IRefreshTokenGrantsService, RefreshTokenGrantsService>();
        services.AddScoped<IRefreshTokenMaintenanceService, RefreshTokenMaintenanceService>();
        services.AddScoped<ITwoFactorEnrollmentService, TwoFactorEnrollmentService>();
        services.AddScoped<ITwoFactorChallengeService, TwoFactorChallengeService>();
        services.AddScoped<ITwoFactorAdministrationService, TwoFactorAdministrationService>();
        services.AddScoped<IConfirmationEmailFactory, ConfirmationEmailFactory>();
        services.AddScoped<IPasswordResetEmailFactory, PasswordResetEmailFactory>();
        services.AddScoped<IEmailChangeEmailFactory, EmailChangeEmailFactory>();
        services.AddScoped<ISecurityEventLog, SecurityEventLog>();
        services.AddScoped<IExternalIdentityVerifier, ExternalIdentityVerifier>();
        services.AddScoped<IExternalLoginsService, ExternalLoginsService>();

        // Not a port: the account lookup and claim generation nine of the adapters above share.
        services.AddScoped<IAppUserDirectory, AppUserDirectory>();

        AddExternalIdentityKeys(services);
        AddIdentityCore(services);
        AddJwtBearerAuthentication(services);
        AddDataProtection(services);

        return services;
    }

    private static void AddSeedingOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentitySeedOptions>()
            .Bind(configuration.GetSection(IdentitySeedOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton<IValidateOptions<IdentitySeedOptions>, IdentitySeedOptionsValidator>();
    }

    /// <summary>
    /// Binds the pooling and command-timeout policy this context shares with every other one. Both
    /// halves bind the same section into the same type on purpose: Npgsql pools per connection
    /// string, so two contexts deriving the bound separately would give a deployment two pools of
    /// that size instead of one.
    /// </summary>
    private static void AddDatabaseOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
    }

    private static void AddContext(IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AuthDbContext>((serviceProvider, options) =>
        {
            var database = serviceProvider.GetRequiredService<IOptions<DatabaseOptions>>().Value;

            options
                .UseNpgsql(WithMaxPoolSize(connectionString, database.MaxPoolSize), npgsql => npgsql
                    .EnableRetryOnFailure(
                        maxRetryCount: 5,
                        maxRetryDelay: TimeSpan.FromSeconds(10),
                        errorCodesToAdd: null)
                    .CommandTimeout(database.CommandTimeoutSeconds)
                    .MigrationsAssembly(typeof(AuthDbContext).Assembly.GetName().Name)
                    .MigrationsHistoryTable(
                        AuthDbContext.MigrationsHistoryTableName,
                        AuthDbContext.MigrationsHistorySchema))
                .AddCoreSavingInterceptors(serviceProvider);
        });
    }

    /// <summary>
    /// Applies the pool bound to the connection string rather than appending it as text, so a value
    /// a deployment already set in <c>ConnectionStrings:Default</c> is overridden consistently
    /// instead of appearing twice. The business half applies the same value the same way, which is
    /// what makes the two contexts share one pool.
    /// </summary>
    private static string WithMaxPoolSize(string connectionString, int maxPoolSize) =>
        new NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = maxPoolSize }.ConnectionString;

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

        services.AddOptions<IdentityTokenOptions>()
            .Bind(configuration.GetSection(IdentityTokenOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentityTokenOptions>, IdentityTokenOptionsValidator>();

        services.AddOptions<PasswordResetOptions>()
            .Bind(configuration.GetSection(PasswordResetOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<PasswordResetOptions>, PasswordResetOptionsValidator>();

        services.AddOptions<EmailChangeOptions>()
            .Bind(configuration.GetSection(EmailChangeOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<EmailChangeOptions>, EmailChangeOptionsValidator>();

        services.AddOptions<TwoFactorOptions>()
            .Bind(configuration.GetSection(TwoFactorOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TwoFactorOptions>, TwoFactorOptionsValidator>();

        // The one section that is allowed to be absent entirely: external sign-in is optional, and a
        // deployment that does not offer it boots with no providers and refuses every attempt.
        services.AddOptions<ExternalIdentityOptions>()
            .Bind(configuration.GetSection(ExternalIdentityOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ExternalIdentityOptions>, ExternalIdentityOptionsValidator>();

        // IdentitySeedOptions is deliberately absent: seeding is a persistence concern and the
        // persistence module binds and validates that section. The section name is unchanged.
    }

    /// <summary>
    /// The one place this module reaches out over HTTP, and the whole reason it is a typed client.
    /// <para>
    /// <c>AddHttpClient</c> is what puts <see cref="SigningKeyDirectory"/> inside the outbound budget
    /// each host installs on <c>IHttpClientFactory</c>'s defaults (<c>Common/Outbound/</c>): timeouts,
    /// retry on the safe verbs only, a circuit breaker and a concurrency bound, none of which this
    /// module names. The alternative shapes all escape it —
    /// <c>ConfigurationManager&lt;OpenIdConnectConfiguration&gt;</c> and
    /// <c>JwtBearerOptions.Backchannel</c> both build an <c>HttpClient</c> of their own unless handed
    /// one, and neither would have been caught by <c>NoType_ConstructsItsOwnHttpClient</c>.
    /// </para>
    /// <para>
    /// The cache is separate and a singleton because the client is transient by design: the factory
    /// hands each instance a pooled handler so sockets and DNS rotate, and a cache living on it would
    /// be a cache that never hit.
    /// </para>
    /// </summary>
    private static void AddExternalIdentityKeys(IServiceCollection services)
    {
        services.AddSingleton<CachedSigningKeys>();
        services.AddHttpClient<ISigningKeyDirectory, SigningKeyDirectory>();
    }

    private static void AddIdentityCore(IServiceCollection services)
    {
        services.AddIdentity<AppUser, AppRole>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddDefaultTokenProviders()
            // A provider of its own, not the "Default" one email confirmation resolves to — see
            // PasswordResetTokenProviderName for why sharing it would tie the two lifespans together.
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProviderName.Value)
            // Same reasoning again, for the email-change token — see EmailChangeTokenProviderName.
            .AddTokenProvider<EmailChangeTokenProvider>(EmailChangeTokenProviderName.Value);

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

                // Points ResetPasswordAsync/GeneratePasswordResetTokenAsync at the named provider
                // above instead of ASP.NET Identity's own "Default" — the value it and email
                // confirmation would otherwise both resolve to.
                identity.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderName.Value;

                // Same reasoning again: ChangeEmailAsync/GenerateChangeEmailTokenAsync default to
                // "Default" too, which would tie an email-change link to email confirmation's
                // one-day lifespan instead of its own, shorter one.
                identity.Tokens.ChangeEmailTokenProvider = EmailChangeTokenProviderName.Value;
            });

        // Every provider AddDefaultTokenProviders just registered shares this one options type, so
        // this is the single knob that currently exists for "how long is a minted token good for" —
        // see IdentityTokenOptions for why that is one setting and not one per provider.
        services.AddOptions<DataProtectionTokenProviderOptions>()
            .Configure<IOptions<IdentityTokenOptions>>(
                (tokenOptions, identityTokenOptions) => tokenOptions.TokenLifespan = identityTokenOptions.Value.Lifespan);

        // The password-reset provider's own lifespan, independent of the one just above.
        services.AddOptions<PasswordResetTokenProviderOptions>()
            .Configure<IOptions<PasswordResetOptions>>(
                (tokenOptions, passwordResetOptions) => tokenOptions.TokenLifespan = passwordResetOptions.Value.TokenLifespan);

        // The email-change provider's own lifespan, independent of both of the above.
        services.AddOptions<EmailChangeTokenProviderOptions>()
            .Configure<IOptions<EmailChangeOptions>>(
                (tokenOptions, emailChangeOptions) => tokenOptions.TokenLifespan = emailChangeOptions.Value.TokenLifespan);
    }

    /// <summary>
    /// Points the data-protection key ring at the shared database instead of the process's local,
    /// ephemeral one. <c>AddDefaultTokenProviders</c> mints email-confirmation and password-reset
    /// tokens through <c>DataProtectorTokenProvider</c>, which without this call keys itself from
    /// whatever the machine or container offers — nothing in a container that is rebuilt on every
    /// deploy, and nothing shared between replicas. Either way, a token issued by one process is
    /// rejected by another, and every redeploy invalidates every link already sent.
    /// </summary>
    private static void AddDataProtection(IServiceCollection services)
    {
        services.AddDataProtection()
            .PersistKeysToDbContext<AuthDbContext>()
            .SetApplicationName(DataProtectionApplicationName);
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
