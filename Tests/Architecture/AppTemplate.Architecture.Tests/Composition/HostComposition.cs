using AppTemplate.Application;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.UserProfiles;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Identity;
using AppTemplate.Infrastructure.InMemory;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AppTemplate.Architecture.Tests.Composition;

/// <summary>
/// Builds the container the API builds, from the same module entry points, so that a registration
/// which compiles but cannot be resolved fails here instead of on the first request.
/// <para>
/// Nothing is faked except the two things the host itself supplies rather than a module:
/// <see cref="ICurrentUser"/>, whose implementation reads an <c>HttpContext</c>, and
/// <see cref="IHostEnvironment"/>. Both are registered here exactly as <c>Program.cs</c> registers
/// them, so the graph under test is the production graph.
/// </para>
/// <para>
/// No database is needed. <c>AddDbContext</c> opens no connection when it is registered, and
/// resolving a <c>DbContext</c> only hands it its options — which is precisely why this test can
/// guard the whole container without any infrastructure. That includes the flush interceptor and the
/// aggregate tracker: both are plain scoped services, and the fact that they resolve here is what
/// proves the mapping pipeline is composed rather than merely written.
/// </para>
/// </summary>
internal static class HostComposition
{
    /// <summary>
    /// Long enough for HS256: <c>JwtOptionsValidator</c> rejects a key under 32 bytes, and every
    /// options section in the identity and email modules is validated with <c>ValidateOnStart</c>.
    /// </summary>
    internal const string JwtKey = "architecture-tests-signing-key-0123456789-abcdefghij";

    /// <summary>
    /// Strict on both counts. <c>ValidateOnBuild</c> checks that every constructor-registered
    /// service can be constructed; <c>ValidateScopes</c> makes capturing a scoped service in a
    /// singleton an error rather than a leak that shows up under load.
    /// </summary>
    internal static ServiceProviderOptions StrictValidation { get; } = new()
    {
        ValidateOnBuild = true,
        ValidateScopes = true,
    };

    /// <summary>
    /// Every setting the modules require, with values that satisfy their validators. The key names
    /// are the real ones, read from the options types rather than guessed: a test that configured a
    /// section under the wrong name would be validating defaults.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> ValidSettings { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Read by DefaultConnectionString.Require in the persistence module. Never opened here.
            ["ConnectionStrings:Default"] =
                "Host=localhost;Port=5432;Database=apptemplate_architecture_tests;Username=postgres;Password=postgres",

            ["Jwt:Key"] = JwtKey,
            ["Jwt:Issuer"] = "https://localhost/app-template",
            ["Jwt:Audience"] = "app-template-api",
            ["Jwt:RequireHttpsMetadata"] = "false",
            ["Jwt:AccessTokenLifetimeInMinutes"] = "15",

            ["RefreshToken:LifetimeInDays"] = "7",

            ["Identity:PasswordRequiredLength"] = "12",
            ["Identity:PasswordRequiredUniqueChars"] = "4",
            ["Identity:PasswordRequireDigit"] = "true",
            ["Identity:PasswordRequireLowercase"] = "true",
            ["Identity:PasswordRequireUppercase"] = "true",
            ["Identity:PasswordRequireNonAlphanumeric"] = "true",
            ["Identity:LockoutEnabled"] = "true",
            ["Identity:LockoutMaxFailedAccessAttempts"] = "5",
            ["Identity:LockoutDurationInMinutes"] = "15",
            ["Identity:RequireConfirmedEmail"] = "true",
            // IdentityPolicyOptionsValidator refuses to let this be turned off.
            ["Identity:RequireUniqueEmail"] = "true",

            // Off, which is what IdentitySeedOptionsValidator requires when no password is supplied.
            ["IdentitySeed:Enabled"] = "false",

            // A culture the runtime knows, and the one every template family must ship — see
            // EmailTemplateCoverageTests. A subject is deliberately not configured: it is the
            // <title> of the template that renders the body.
            ["Localization:DefaultCulture"] = "en",

            // Absolute, http(s), and carrying no fragment — the confirmation parameters become one.
            ["EmailConfirmation:ConfirmEmailUrl"] = "https://localhost:5001/confirm-email",

            ["EmailChange:ConfirmEmailChangeUrl"] = "https://localhost:5001/confirm-email-change",
            ["PasswordReset:ResetPasswordUrl"] = "https://localhost:5001/reset-password",

            ["Email:Host"] = "smtp.example.invalid",
            ["Email:Port"] = "587",
            ["Email:FromAddress"] = "no-reply@example.invalid",
            ["Email:FromName"] = "AppTemplate",
            // Mandatory STARTTLS: EmailOptionsValidator rejects any mode that can fall back to
            // plaintext against a non-loopback host.
            ["Email:Security"] = "StartTls",

            // The bucket is the only required value: with no endpoint the module addresses AWS S3
            // itself, and with no credentials the SDK's own chain resolves an instance role — which
            // is the shape a deployment should be in, so it is the shape composed here.
            ["Storage:BucketName"] = "app-template-architecture-tests",
        };

    /// <summary>
    /// The valid settings, optionally with individual keys replaced — used to prove that an invalid
    /// section stops the container rather than surfacing at the first login.
    /// </summary>
    internal static IConfiguration Configuration(params KeyValuePair<string, string?>[] overrides)
    {
        var settings = new Dictionary<string, string?>(ValidSettings, StringComparer.Ordinal);

        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }

    /// <summary>
    /// The API's composition, module for module, in the order <c>Program.cs</c> uses.
    /// </summary>
    internal static ServiceCollection ComposeApi(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        AddHostSuppliedServices(services, configuration);

        services.AddApplicationLayer();
        services.AddPersistenceModule(configuration);
        services.AddIdentityModule(configuration);
        services.AddEmailModule(configuration);
        services.AddStorageModule(configuration);

        AddHostSuppliedAdapters(services);

        return services;
    }

    /// <summary>
    /// The worker's composition: the same five modules, and deliberately <em>without</em>
    /// <c>AddHttpContextAccessor</c>, because that is the one difference between the two hosts that
    /// can break a graph. A module gaining a dependency on <c>IHttpContextAccessor</c> leaves the
    /// API's container green and stops the worker at start-up, and this composition is the only
    /// thing in the rule set that notices.
    /// <para>
    /// The worker registers <c>BackgroundCurrentUser</c> for <see cref="ICurrentUser"/>, a class
    /// that throws rather than invent a principal, and <c>BackgroundAuditActor</c> for
    /// <see cref="IAuditActor"/>, which answers <c>null</c>. The stand-ins below have those same two
    /// behaviours, deliberately: a benign <see cref="ICurrentUser"/> here is what let a host that
    /// could commit nothing pass every rule in this project. Nothing here references
    /// <c>AppTemplate.Worker</c> — its own hosted services, options and telemetry stay outside this
    /// check, and <c>AppTemplate.Worker.UnitTests</c> covers them.
    /// </para>
    /// </summary>
    internal static ServiceCollection ComposeWorker(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        AddHostSuppliedServices(services, configuration);

        services.AddApplicationLayer();
        services.AddPersistenceModule(configuration);
        services.AddIdentityModule(configuration);
        services.AddEmailModule(configuration);
        services.AddStorageModule(configuration);

        AddWorkerSuppliedAdapters(services);

        return services;
    }

    /// <summary>
    /// The composition a test host uses: the API's modules, then the doubles that replace the clock
    /// and the mail relay. Composed here too, because <c>AppTemplate.Infrastructure.InMemory</c> is a product
    /// project and a double that cannot be resolved is as broken as an adapter that cannot.
    /// </summary>
    internal static ServiceCollection ComposeTestHost(IConfiguration configuration)
    {
        var services = ComposeApi(configuration);

        // After the real modules, which is the documented order: it removes and re-adds rather than
        // relying on last-registration-wins.
        services.AddInMemoryModule();

        return services;
    }

    /// <summary>
    /// The worker's composition with the identity module left out, which is the question every
    /// comment about this host's configuration surface answers wrongly. The written reason is
    /// <c>IRefreshTokenMaintenanceService</c>'s adapter; the operative one is that
    /// <c>EmailReminderNotifier</c> — the adapter behind the worker's <em>own</em> reminder loop —
    /// needs <see cref="IUserProfilesService"/> to find the address it notifies. Moving the
    /// maintenance adapter to another module would not free this host of anything.
    /// </summary>
    internal static ServiceCollection ComposeWorkerWithoutTheIdentityModule(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        AddHostSuppliedServices(services, configuration);

        services.AddApplicationLayer();
        services.AddPersistenceModule(configuration);
        services.AddEmailModule(configuration);
        services.AddStorageModule(configuration);

        AddWorkerSuppliedAdapters(services);

        return services;
    }

    /// <summary>
    /// The API's composition with one module left out, used to prove that the container test can
    /// actually fail. The application layer's sign-up use cases depend on <c>IEmailSender</c> and no
    /// other module implements it, so dropping the email module must break the graph.
    /// </summary>
    internal static ServiceCollection ComposeApiWithoutTheEmailModule(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        AddHostSuppliedServices(services, configuration);

        services.AddApplicationLayer();
        services.AddPersistenceModule(configuration);
        services.AddIdentityModule(configuration);

        AddHostSuppliedAdapters(services);

        return services;
    }

    /// <summary>
    /// What <c>WebApplicationBuilder</c> has already put in the container before <c>Program.cs</c>
    /// composes its first module.
    /// </summary>
    private static void AddHostSuppliedServices(IServiceCollection services, IConfiguration configuration)
    {
        // ASP.NET Core's own authentication configuration provider takes an IConfiguration
        // dependency, so this is a real part of the graph and not a convenience.
        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(new ArchitectureTestHostEnvironment());
        services.AddLogging();
    }

    /// <summary>
    /// The adapters the host owns rather than a module: <c>AppTemplate.Api</c> registers its own
    /// <c>CurrentUser</c>, which reads the ambient <c>HttpContext</c>.
    /// </summary>
    private static void AddHostSuppliedAdapters(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, ArchitectureTestCurrentUser>();
        services.AddScoped<IAuditActor, ArchitectureTestAuditActor>();
    }

    /// <summary>
    /// The adapters <c>AppTemplate.Worker</c> owns, with the behaviour that host gives them: an
    /// <see cref="ICurrentUser"/> that refuses to answer and an <see cref="IAuditActor"/> that
    /// answers nobody.
    /// </summary>
    private static void AddWorkerSuppliedAdapters(IServiceCollection services)
    {
        services.AddScoped<ICurrentUser, ArchitectureTestBackgroundCurrentUser>();
        services.AddScoped<IAuditActor, ArchitectureTestBackgroundAuditActor>();
    }
}

/// <summary>Stands in for <c>AppTemplate.Api.Common.CurrentUser</c>, which needs an HTTP request.</summary>
internal sealed class ArchitectureTestCurrentUser : ICurrentUser
{
    private static readonly Guid _userId = new("11111111-1111-1111-1111-111111111111");

    public Guid? UserId => _userId;
}

/// <summary>Stands in for <c>AppTemplate.Api.Common.Security.CurrentUserAuditActor</c>.</summary>
internal sealed class ArchitectureTestAuditActor : IAuditActor
{
    private static readonly Guid _userId = new("11111111-1111-1111-1111-111111111111");

    public Guid? UserId => _userId;
}

/// <summary>
/// Stands in for <c>AppTemplate.Worker.Common.Security.BackgroundCurrentUser</c>, throw included.
/// The container tests resolve it and never read the getter; a stand-in that answered a user id is
/// what hid the composition mistake this one exists to expose.
/// </summary>
internal sealed class ArchitectureTestBackgroundCurrentUser : ICurrentUser
{
    public Guid? UserId => throw new NotSupportedException(
        "The worker has no current user. A use case that reads ICurrentUser.UserId cannot run " +
        "unmodified from that host.");
}

/// <summary>Stands in for <c>AppTemplate.Worker.Common.Security.BackgroundAuditActor</c>.</summary>
internal sealed class ArchitectureTestBackgroundAuditActor : IAuditActor
{
    public Guid? UserId => null;
}

/// <summary>
/// Stands in for the environment the host provides. <c>IdentitySeeder</c> depends on it, so a
/// container built without it could not construct the seeder.
/// </summary>
internal sealed class ArchitectureTestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "AppTemplate.Architecture.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
