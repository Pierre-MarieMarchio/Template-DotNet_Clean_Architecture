//#if (localAuth)
using System.Globalization;
//#endif
//#if (!jobsWorker)
using AppTemplate.Api.Common.Jobs;
//#endif
using AppTemplate.Api.Common.Security;
//#if (localAuth)
using AppTemplate.Api.IntegrationTests.Auth;
//#endif
//#if (todoLists)
using AppTemplate.Application.Core;
//#endif
//#if (todoLists)
using AppTemplate.Domain.Features.TodoLists.Model.Events;
//#endif
//#if (localAuth)
using AppTemplate.Infrastructure.Auth.Common.Options;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Seeding;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Auth.Features.Passkeys.Configuration.Options;
using AppTemplate.Infrastructure.Auth.Features.Sessions.Configuration.Options;
//#endif
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Email.Common.Smtp;
using AppTemplate.Infrastructure.InMemory;
//#if (!publicApi)
using Microsoft.AspNetCore.Authentication.JwtBearer;
//#endif
using Microsoft.AspNetCore.Hosting;
//#if (localAuth)
using Microsoft.AspNetCore.Identity;
//#endif
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
/*#if (externalAuth)
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
#endif*/

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    //#if (localAuth)
    private const string _signingKey = "integration-tests-signing-key-0123456789abcdef0123456789abcdef";

    public const int LockoutMaxFailedAccessAttempts = 3;

    public const int PasswordRequiredLength = 12;

    //#endif
    public ApiFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        ApplyConfiguration(connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureTestServices(services =>
        {
            services.AddInMemoryModule();

            services.RemoveAll<ILoggerProvider>();
            services.AddSingleton<CapturedLogs>();
            services.AddSingleton<ILoggerProvider>(serviceProvider =>
                new CapturingLoggerProvider(serviceProvider.GetRequiredService<CapturedLogs>()));

            //#if (todoLists)
            services.AddSingleton<RecordedDomainEvents>();
            services.AddDomainEventConsumer<TodoItemCompletedDomainEvent, RecordingTodoItemCompletedConsumer>();
            //#endif

            services.AddSingleton<IStartupFilter, TestClientAddressStartupFilter>();

            services.PostConfigure<RateLimitingOptions>(options => options.Window = TimeSpan.FromHours(1));
            //#if (localAuth)

            ExposeTheReasonATokenWasRefused(services);

            services.AddSingleton<PasskeyCeremonyFailures>();
            services.Replace(ServiceDescriptor.Scoped<IPasskeyHandler<AppUser>, RecordingPasskeyHandler>());
            //#endif
            /*#if (externalAuth)

            TrustTheStandInProvider(services);
            #endif*/
        });
    }

    //#if (localAuth)
    internal const string AuthFailureHeader = "X-Test-Auth-Failure";

    private static void ExposeTheReasonATokenWasRefused(IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                var writeProblem = options.Events!.OnChallenge;

                options.Events.OnChallenge = context =>
                {
                    if (context.AuthenticateFailure is { } failure)
                    {
                        context.Response.Headers[AuthFailureHeader] =
                            failure.Message.ReplaceLineEndings(" ");
                    }

                    return writeProblem(context);
                };
            });

    //#endif
    /*#if (externalAuth)
    private static void TrustTheStandInProvider(IServiceCollection services) =>
        services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                options.Configuration = new OpenIdConnectConfiguration { Issuer = ExternalProvider.Issuer };
                options.Configuration.SigningKeys.Add(ExternalProvider.SigningKey);
            });

    #endif*/
    private static void ApplyConfiguration(string connectionString)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConnectionStrings__" + DefaultConnectionString.Name] = connectionString,

            //#if (localAuth)
            [$"{JwtOptions.SectionName}__Key"] = _signingKey,
            [$"{JwtOptions.SectionName}__Issuer"] = "AppTemplate.Api.IntegrationTests",
            [$"{JwtOptions.SectionName}__Audience"] = "AppTemplate.Client.IntegrationTests",
            [$"{JwtOptions.SectionName}__RequireHttpsMetadata"] = "false",
            [$"{JwtOptions.SectionName}__AccessTokenLifetimeInMinutes"] = "15",

            [$"{RefreshTokenOptions.SectionName}__LifetimeInDays"] = "7",

            [$"{IdentityPolicyOptions.SectionName}__PasswordRequiredLength"] =
                PasswordRequiredLength.ToString(CultureInfo.InvariantCulture),
            [$"{IdentityPolicyOptions.SectionName}__PasswordRequiredUniqueChars"] = "4",
            [$"{IdentityPolicyOptions.SectionName}__PasswordRequireDigit"] = "true",
            [$"{IdentityPolicyOptions.SectionName}__PasswordRequireLowercase"] = "true",
            [$"{IdentityPolicyOptions.SectionName}__PasswordRequireUppercase"] = "true",
            [$"{IdentityPolicyOptions.SectionName}__PasswordRequireNonAlphanumeric"] = "true",
            [$"{IdentityPolicyOptions.SectionName}__LockoutEnabled"] = "true",
            [$"{IdentityPolicyOptions.SectionName}__LockoutMaxFailedAccessAttempts"] =
                LockoutMaxFailedAccessAttempts.ToString(CultureInfo.InvariantCulture),
            [$"{IdentityPolicyOptions.SectionName}__LockoutDurationInMinutes"] = "15",
            [$"{IdentityPolicyOptions.SectionName}__RequireConfirmedEmail"] = "true",
            [$"{IdentityPolicyOptions.SectionName}__RequireUniqueEmail"] = "true",

            //#endif
            [$"{EmailOptions.SectionName}__Host"] = "localhost",
            [$"{EmailOptions.SectionName}__Port"] = "1025",
            [$"{EmailOptions.SectionName}__Security"] = "None",
            [$"{EmailOptions.SectionName}__AllowInsecureTransport"] = "false",
            [$"{EmailOptions.SectionName}__FromAddress"] = "no-reply@integration.test",
            [$"{EmailOptions.SectionName}__FromName"] = "AppTemplate (integration tests)",

            //#if (localAuth)
            [$"{EmailConfirmationOptions.SectionName}__ConfirmEmailUrl"] =
                "https://client.integration.test/confirm-email",
            [$"{EmailConfirmationOptions.SectionName}__Subject"] = "Confirm your email address",

            [$"{PasswordResetOptions.SectionName}__ResetPasswordUrl"] =
                "https://client.integration.test/reset-password",
            [$"{PasswordResetOptions.SectionName}__Subject"] = "Reset your password",

            [$"{EmailChangeOptions.SectionName}__ConfirmEmailChangeUrl"] =
                "https://client.integration.test/confirm-email-change",
            [$"{EmailChangeOptions.SectionName}__Subject"] = "Confirm your new email address",

            [$"{IdentitySeedOptions.SectionName}__Enabled"] = "false",

            [$"{PasskeyOptions.SectionName}__ServerDomain"] = PasskeyTestSupport.RpId,

            //#endif
            //#if (externalAuth)
            ["Authentication__External__Authority"] = ExternalProvider.Issuer,
            ["Authentication__External__Audience"] = ExternalProvider.Audience,
            ["Authentication__External__Roles__Source"] = "Claim",
            ["Authentication__External__Roles__ClaimPath"] = ExternalProvider.RolesClaimPath,
            ["Authentication__External__Roles__AdministratorRole"] = ExternalProvider.AdministratorRole,

            //#endif
            //#if (!jobsWorker)
            [$"{JobsOptions.SectionName}__Enabled"] = "false",

            //#endif
            ["Files__Storage__S3__BucketName"] = "apptemplate-integration-tests",

            ["Logging__LogLevel__Default"] = "Information",
            ["Logging__LogLevel__Microsoft.AspNetCore"] = "Warning",
            ["Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command"] = "Warning",
        };

        foreach (var setting in settings)
        {
            Environment.SetEnvironmentVariable(setting.Key, setting.Value);
        }
    }
}
