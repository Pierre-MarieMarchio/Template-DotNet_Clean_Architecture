using System.Globalization;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Security;
using AppTemplate.Application;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Infrastructure.Email.Options;
using AppTemplate.Infrastructure.Identity.Options;
using AppTemplate.Infrastructure.InMemory;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Features.Identity.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The real API host, wired to a real PostgreSQL container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the entry-point type argument is <see cref="ApiControllerBase"/> and not
/// <c>Program</c>.</b> <c>Program</c> is the compiler-generated class behind top-level statements,
/// and it is <c>internal</c>. Making it visible would mean editing <c>AppTemplate.Api.csproj</c>, which this
/// project must not touch. <see cref="WebApplicationFactory{TEntryPoint}"/> only ever uses the type
/// argument to locate the assembly that owns the entry point, so any public type from the API works
/// and nothing about the host changes.
/// </para>
/// <para>
/// <b>Why configuration is supplied through environment variables.</b> <c>Program.cs</c> reads
/// <c>builder.Configuration</c> <em>while composing the container</em> —
/// <see cref="DefaultConnectionString.Require"/> runs inside <c>AddTodoListsModule</c>, before the
/// host is ever built. Environment variables are part of the default configuration chain and
/// outrank every <c>appsettings*.json</c> file, so they are visible at that moment with no
/// dependence on when a test hook happens to run. Every key each options validator requires is set
/// here, which also means the suite does not care whether the content root resolves to the API
/// project or to the test output directory.
/// </para>
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<ApiControllerBase>
{
    /// <summary>Well above the 32-byte HS256 floor <see cref="JwtOptions"/> enforces.</summary>
    private const string _signingKey = "integration-tests-signing-key-0123456789abcdef0123456789abcdef";

    /// <summary>
    /// Deliberately lower than the shipped default of 5, so the lockout test needs fewer requests
    /// than the per-IP authentication rate limit allows.
    /// </summary>
    public const int LockoutMaxFailedAccessAttempts = 3;

    /// <summary>The <c>Identity</c> policy the suite asserts against, not the laxer development one.</summary>
    public const int PasswordRequiredLength = 12;

    public ApiFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        ApplyConfiguration(connectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Runs after Program.cs has finished registering, which is what lets AddInMemoryModule's
        // remove-then-add replacement actually replace something.
        builder.ConfigureTestServices(services =>
        {
            // The clock and the email sender. Not fakes written here: the product project exists
            // precisely so every host substitutes them the same way.
            services.AddInMemoryModule();

            // Program.cs installs a JSON console provider. Dropping it keeps the suite's output
            // readable; the capturing provider below is what the tests assert against.
            services.RemoveAll<ILoggerProvider>();
            services.AddSingleton<CapturedLogs>();
            services.AddSingleton<ILoggerProvider>(serviceProvider =>
                new CapturingLoggerProvider(serviceProvider.GetRequiredService<CapturedLogs>()));

            // A second consumer for an event the product already consumes. Registered through the
            // module's own public entry point, so the test proves that mechanism rather than
            // bypassing it.
            services.AddSingleton<RecordedDomainEvents>();
            services.AddDomainEventConsumer<TodoItemCompletedDomainEvent, RecordingTodoItemCompletedConsumer>();

            // Gives each test its own rate-limit partition. See TestClientAddressStartupFilter.
            services.AddSingleton<IStartupFilter, TestClientAddressStartupFilter>();

            // The fixed-window limiter has no injectable clock: it catches up to real elapsed time
            // on the next acquire regardless of AutoReplenishment, so nothing short of the window
            // itself can stop a slow run from crossing a real one-minute boundary between one
            // request and the next and getting a false replenish. Widened far past anything a test
            // could plausibly take, which leaves the permit counts under test untouched.
            services.Replace(ServiceDescriptor.Singleton(new RateLimiterWindow(TimeSpan.FromHours(1))));

            ExposeTheReasonATokenWasRefused(services);
        });
    }

    /// <summary>
    /// Surfaces why bearer validation refused a token, in a header only this host writes.
    /// </summary>
    /// <remarks>
    /// The product deliberately tells an unauthenticated caller nothing beyond "a valid token is
    /// required": expired, wrong signature and revoked security stamp must be indistinguishable, and a
    /// test asserts exactly that. The cost is that a 401 nobody expected is undiagnosable, so the
    /// reason is published here — in the test host, never in the product — where only a test can read
    /// it.
    /// </remarks>
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

    private static void ApplyConfiguration(string connectionString)
    {
        var settings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ConnectionStrings__" + DefaultConnectionString.Name] = connectionString,

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

            // Validated even though InMemoryModule replaces the sender: ValidateOnStart does not
            // care which adapter ends up consuming the section. A loopback host is what lets
            // Security=None pass without the explicit insecure-transport opt-in.
            [$"{EmailOptions.SectionName}__Host"] = "localhost",
            [$"{EmailOptions.SectionName}__Port"] = "1025",
            [$"{EmailOptions.SectionName}__Security"] = "None",
            [$"{EmailOptions.SectionName}__AllowInsecureTransport"] = "false",
            [$"{EmailOptions.SectionName}__FromAddress"] = "no-reply@integration.test",
            [$"{EmailOptions.SectionName}__FromName"] = "AppTemplate (integration tests)",

            [$"{EmailConfirmationOptions.SectionName}__ConfirmEmailUrl"] =
                "https://client.integration.test/confirm-email",
            [$"{EmailConfirmationOptions.SectionName}__Subject"] = "Confirm your email address",

            // Required, and validated on start by whichever host composes the identity module — so
            // its absence stops the host before a single test runs, not the test that needs it.
            [$"{PasswordResetOptions.SectionName}__ResetPasswordUrl"] =
                "https://client.integration.test/reset-password",
            [$"{PasswordResetOptions.SectionName}__Subject"] = "Reset your password",

            [$"{EmailChangeOptions.SectionName}__ConfirmEmailChangeUrl"] =
                "https://client.integration.test/confirm-email-change",
            [$"{EmailChangeOptions.SectionName}__Subject"] = "Confirm your new email address",

            [$"{IdentitySeedOptions.SectionName}__Enabled"] = "false",

            // Information, because a test asserts on a log the product's domain-event consumer
            // writes at that level. The two noisiest categories are turned down.
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
