//#if (accessKeys)
using AppTemplate.Application.Auth.Features.AccessKeys.Ports.AccessKeys;
//#endif
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountAdministration;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountEmails;
using AppTemplate.Application.Auth.Features.Accounts.Ports.AccountQueries;
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
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Common.Options;
//#if (accessKeys)
using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Access.Tables;
using AppTemplate.Infrastructure.Auth.Features.AccessKeys.Adapters.Services;
//#endif
using AppTemplate.Infrastructure.Auth.Features.Accounts.Access.Queries;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Auth;

public static class AuthModule
{
    internal const string DataProtectionApplicationName = "AppTemplate";

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
        //#if (accessKeys)
        services.TryAddScoped<IAccessKeyTable, AccessKeyTable>();
        //#endif

        services.TryAddScoped<IIdentitySeeder, IdentitySeeder>();

        services.AddScoped<IAccountsService, AccountsService>();
        services.AddScoped<IEmailTokensService, EmailTokensService>();
        services.AddScoped<ISessionTokensService, SessionTokensService>();
        services.AddScoped<ISessionsService, SessionsService>();
        services.AddScoped<ITwoFactorService, TwoFactorService>();
        services.AddScoped<IPasskeyService, PasskeyService>();
        //#if (accessKeys)
        services.AddScoped<IAccessKeysService, AccessKeysService>();
        //#endif
        services.AddScoped<PasskeyCeremonyStateIssuer>();
        services.AddScoped<IAccountAdministrationService, AccountAdministrationService>();
        services.AddScoped<IReAuthenticationService, ReAuthenticationService>();
        services.AddScoped<IExternalIdentityVerifier, ExternalIdentityVerifier>();
        services.AddScoped<IExternalLoginsService, ExternalLoginsService>();
        services.AddScoped<IAccountEmailFactory, AccountEmailFactory>();
        services.AddScoped<ISecurityEventLog, SecurityEventLog>();
        services.AddScoped<ISecurityEventQueries, SecurityEventQueries>();
        services.AddScoped<IAccountQueries, AccountQueries>();

        services.TryAddScoped<ISecurityEventOriginProvider, UnknownSecurityEventOriginProvider>();

        services.AddScoped<IAppUserDirectory, AppUserDirectory>();
        services.AddScoped<AccessTokenIssuer>();

        AddExternalIdentityKeys(services);
        AddIdentityCore(services);
        AddJwtBearerAuthentication(services);
        AddDataProtection(services);

        return services;
    }

    private static bool AlreadyComposed(IServiceCollection services) =>
        services.Any(descriptor => descriptor.ServiceType == typeof(IAccountsService));

    private static void AddSeedingOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdentitySeedOptions>()
            .Bind(configuration.GetSection(IdentitySeedOptions.SectionName))
            .ValidateOnStart();
        services.TryAddSingleton<IValidateOptions<IdentitySeedOptions>, IdentitySeedOptionsValidator>();
    }

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

            options.UseDatabase(connectionString, database, AuthDbContext.Migrations);
        });
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

        services.AddOptions<ExternalIdentityOptions>()
            .Bind(configuration.GetSection(ExternalIdentityOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ExternalIdentityOptions>, ExternalIdentityOptionsValidator>();

    }

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
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProviderName.Value)
            .AddTokenProvider<EmailChangeTokenProvider>(EmailChangeTokenProviderName.Value);

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

                identity.Tokens.PasswordResetTokenProvider = PasswordResetTokenProviderName.Value;

                identity.Tokens.ChangeEmailTokenProvider = EmailChangeTokenProviderName.Value;
            });

        services.AddOptions<DataProtectionTokenProviderOptions>()
            .Configure<IOptions<IdentityTokenOptions>>(
                (tokenOptions, identityTokenOptions) => tokenOptions.TokenLifespan = identityTokenOptions.Value.Lifespan);

        services.AddOptions<PasswordResetTokenProviderOptions>()
            .Configure<IOptions<PasswordResetOptions>>(
                (tokenOptions, passwordResetOptions) => tokenOptions.TokenLifespan = passwordResetOptions.Value.TokenLifespan);

        services.AddOptions<EmailChangeTokenProviderOptions>()
            .Configure<IOptions<EmailChangeOptions>>(
                (tokenOptions, emailChangeOptions) => tokenOptions.TokenLifespan = emailChangeOptions.Value.TokenLifespan);

        services.AddOptions<IdentityPasskeyOptions>()
            .Configure<IOptions<PasskeyOptions>>(
                (identityPasskeys, passkeys) => identityPasskeys.ServerDomain = passkeys.Value.ServerDomain);
    }

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
