using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.Accounts;
using AppTemplate.Application.Auth.Features.Accounts.Ports.EmailTokens;
using AppTemplate.Application.Auth.Features.Accounts.Ports.ReAuthentication;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalIdentity;
using AppTemplate.Application.Auth.Features.ExternalLogins.Ports.ExternalLogins;
using AppTemplate.Application.Auth.Features.Passkeys.Ports.Passkeys;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventLog;
using AppTemplate.Application.Auth.Features.SecurityEvents.Ports.SecurityEventQueries;
using AppTemplate.Application.Auth.Features.Sessions.Ports.Sessions;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
using AppTemplate.Application.Auth.Features.TwoFactor.Ports.TwoFactor;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Common.Options;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Providers;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Seeding;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.ExternalLogins.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Access.Queries;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Access.Tables;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Directories;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.TwoFactor.Adapters.Services;
using AppTemplate.Infrastructure.Auth.Features.TwoFactor.Configuration.Options;
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

/// <summary>Composes the authentication module: who a caller is, how that is proved, how long the
/// proof lasts, and the tables that record it. <b>It owns its store:</b> the account, role and grant
/// tables, the key ring and the <see cref="AuthDbContext"/> that maps them, with their own migrations
/// history in their own schema; beside that sits everything that talks to ASP.NET Identity. It names
/// no other module — the unit of work and the clock come from
/// <c>AppTemplate.Infrastructure.Core</c> — and its context attaches no save interceptor. It sends no
/// mail (<see cref="AccountEmailFactory"/> renders, the host delivers through
/// <see cref="IEmailSender"/>) and makes no authentication decision: the sequencing belongs to the
/// use cases in <c>AppTemplate.Application.Auth</c>.</summary>
public static class AuthModule
{
    /// <summary>
    /// The key ring's isolation namespace, shared between every <c>AddDataProtection</c> call this
    /// process makes, so a second one — a test host, say — cannot end up unkeyed by naming a
    /// different string.
    /// </summary>
    internal const string DataProtectionApplicationName = "AppTemplate";

    /// <summary>Registers ASP.NET Identity itself, bearer validation, and an adapter for each
    /// authentication port. Every configuration section is bound to an options type with a
    /// start-up validator, so the process refuses to boot on a missing signing key or a too-weak
    /// password policy. Idempotent, like <c>AddPersistenceModule</c>.</summary>
    /// <param name="services">The container being composed.</param>
    /// <param name="configuration">Must supply the <c>Default</c> connection string, which this module
    /// forwards to the persistence module rather than opening a connection of its own.</param>
    public static IServiceCollection AddAuthModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (AlreadyComposed(services))
        {
            return services;
        }

        AddValidatedOptions(services, configuration);

        AddSeedingOptions(services, configuration);
        AddDatabaseOptions(services, configuration);
        AddContext(services, DefaultConnectionString.Require(configuration));
        services.AddContextUnitOfWork<AuthDbContext>();
        services.AddSystemClock();

        services.TryAddScoped<IRefreshTokenTable, RefreshTokenTable>();
        services.TryAddScoped<IRefreshTokenFamilyTable, RefreshTokenFamilyTable>();
        services.TryAddScoped<ISpentPasskeyCeremonyTable, SpentPasskeyCeremonyTable>();

        // Constructible only once ASP.NET Identity is composed below: seeding hashes a password and
        // generates a security stamp rather than writing a row.
        services.TryAddScoped<IIdentitySeeder, IdentitySeeder>();

        services.AddScoped<IAccountsService, AccountsService>();
        services.AddScoped<IEmailTokensService, EmailTokensService>();
        services.AddScoped<ISessionTokensService, SessionTokensService>();
        services.AddScoped<ISessionsService, SessionsService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<IPasskeyService, PasskeyService>();
        services.AddScoped<IAccountAdministrationService, AccountAdministrationService>();
        services.AddScoped<IReAuthenticationService, ReAuthenticationService>();
        services.AddScoped<IExternalIdentityVerifier, ExternalIdentityVerifier>();
        services.AddScoped<IExternalLoginsService, ExternalLoginsService>();
        services.AddScoped<IAccountEmailFactory, AccountEmailFactory>();
        services.AddScoped<ISecurityEventLog, SecurityEventLog>();
        services.AddScoped<ISecurityEventQueries, SecurityEventQueries>();

        // A default, unlike IAuditActor: an event with no known origin is still worth keeping, so a
        // host without requests composes as it is. The API replaces it with the current request's.
        services.TryAddScoped<ISecurityEventOriginProvider, UnknownSecurityEventOriginProvider>();

        // Not ports: the account lookup and claim generation the adapters above share, and the
        // access-token signer SessionTokensService delegates to.
        services.AddScoped<IAppUserDirectory, AppUserDirectory>();
        services.AddScoped<AccessTokenIssuer>();

        AddExternalIdentityKeys(services);
        AddIdentityCore(services);
        AddJwtBearerAuthentication(services);
        AddDataProtection(services);

        return services;
    }

    /// <summary>
    /// <c>AddIdentity</c> and <c>AddAuthentication</c> have no <c>Try</c> form, so a second call
    /// would duplicate a scheme and a whole store.
    /// </summary>
    private static bool AlreadyComposed(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(IAccountsService));

    private static void AddSeedingOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentitySeedOptions>()
            .Bind(configuration.GetSection(IdentitySeedOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton<IValidateOptions<IdentitySeedOptions>, IdentitySeedOptionsValidator>();
    }

    /// <summary>Binds the pooling and command-timeout policy this context shares with every other
    /// one: Npgsql pools per connection string, so two contexts deriving the bound separately would
    /// give a deployment two pools of that size instead of one.</summary>
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
                        AuthDbContext.MigrationsHistorySchema));
        });
    }

    /// <summary>Applies the pool bound to the connection string rather than appending it as text,
    /// so a value already set in <c>ConnectionStrings:Default</c> is overridden consistently; the
    /// business half applies the same value the same way, which is what makes the two contexts share
    /// one pool.</summary>
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

        services.AddOptions<SecurityEventLogOptions>()
            .Bind(configuration.GetSection(SecurityEventLogOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SecurityEventLogOptions>, SecurityEventLogOptionsValidator>();

        services.AddOptions<PasskeyOptions>()
            .Bind(configuration.GetSection(PasskeyOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton<IValidateOptions<PasskeyOptions>, PasskeyOptionsValidator>();

        // The one section allowed to be absent: a deployment offering no external sign-in boots with
        // no providers and refuses every attempt.
        services.AddOptions<ExternalIdentityOptions>()
            .Bind(configuration.GetSection(ExternalIdentityOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ExternalIdentityOptions>, ExternalIdentityOptionsValidator>();

    }

    /// <summary>The one place this module reaches out over HTTP, and the whole reason it is a typed
    /// client: <c>AddHttpClient</c> puts <see cref="SigningKeyDirectory"/> inside the outbound budget
    /// each host installs on <c>IHttpClientFactory</c>'s defaults. The alternative shapes all escape
    /// it — <c>ConfigurationManager&lt;OpenIdConnectConfiguration&gt;</c> and
    /// <c>JwtBearerOptions.Backchannel</c> both build an <c>HttpClient</c> of their own unless handed
    /// one. The cache is separate and a singleton because the client is transient: a cache living on
    /// it would be a cache that never hit.</summary>
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
            // Providers of their own, not the "Default" one email confirmation resolves to: sharing
            // it would tie their lifespans together.
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProviderName.Value)
            .AddTokenProvider<EmailChangeTokenProvider>(EmailChangeTokenProviderName.Value);

        // After AddIdentity's own defaults, and from validated options rather than a section read
        // eagerly at composition time.
        services.AddOptions<IdentityOptions>()
            .Configure<IOptions<IdentityPolicyOptions>>((identity, policyAccessor) =>
            {
                var policy = policyAccessor.Value;

                identity.User.RequireUniqueEmail = policy.RequireUniqueEmail;
                identity.User.AllowedUserNameCharacters = string.Empty;
                identity.SignIn.RequireConfirmedAccount = false;
                identity.SignIn.RequireConfirmedEmail = policy.RequireConfirmedEmail;

                identity.Password.RequiredLength = policy.EffectivePasswordRequiredLength;
                identity.Password.RequiredUniqueChars = policy.PasswordRequiredUniqueChars;
                identity.Password.RequireDigit = policy.PasswordRequireDigit;
                identity.Password.RequireLowercase = policy.PasswordRequireLowercase;
                identity.Password.RequireUppercase = policy.PasswordRequireUppercase;
                identity.Password.RequireNonAlphanumeric = policy.PasswordRequireNonAlphanumeric;

                identity.Lockout.AllowedForNewUsers = policy.LockoutEnabled;
                identity.Lockout.MaxFailedAccessAttempts = policy.LockoutMaxFailedAccessAttempts;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(policy.LockoutDurationInMinutes);

                // Without these two, ResetPasswordAsync and ChangeEmailAsync resolve "Default" and
                // inherit email confirmation's one-day lifespan instead of their own.
                identity.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderName.Value;

                identity.Tokens.ChangeEmailTokenProvider = EmailChangeTokenProviderName.Value;
            });

        // Every provider AddDefaultTokenProviders registered shares this options type, so it is one
        // lifespan for all of them.
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

        // The one member of the WebAuthn posture a deployment sets; the rest of
        // IdentityPasskeyOptions keeps its own safe defaults — user verification required among
        // them, which is what lets a passkey stand in for the whole of a sign-in.
        services.AddOptions<IdentityPasskeyOptions>()
            .Configure<IOptions<PasskeyOptions>>(
                (identityPasskeys, passkeys) => identityPasskeys.ServerDomain = passkeys.Value.ServerDomain);
    }

    /// <summary>Points the data-protection key ring at the shared database instead of the process's
    /// local, ephemeral one: <c>AddDefaultTokenProviders</c> mints its tokens through
    /// <c>DataProtectorTokenProvider</c>, which without this keys itself from whatever the container
    /// offers — nothing shared between replicas, so a token issued by one process would be rejected by
    /// another and every redeploy invalidates every link already sent. A passkey ceremony's state is
    /// sealed with the same ring, so it opens on whichever instance the completing call reaches.</summary>
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
