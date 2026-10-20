//#if (anyFeature || externalAuth)
using AppTemplate.Application;
//#endif
//#if (localAuth)
using AppTemplate.Application.Auth;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
using AppTemplate.Application.Auth.Features.Sessions.Ports.SessionTokens;
//#endif
using AppTemplate.Application.Core;
using AppTemplate.Application.Core.Common.Ports;
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
//#endif
using AppTemplate.Domain.Core.Common.Primitives;
//#if (localAuth)
using AppTemplate.Infrastructure.Auth;
//#endif
using AppTemplate.Infrastructure.Core;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.InMemory;
using AppTemplate.Infrastructure.Persistence;
//#if (files)
using AppTemplate.Infrastructure.Storage;
//#endif
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AppTemplate.Architecture.Tests.Composition;

internal static class HostComposition
{
    //#if (localAuth)
    internal const string JwtKey = "architecture-tests-signing-key-0123456789-abcdefghij";

    //#endif
    internal static ServiceProviderOptions StrictValidation { get; } = new()
    {
        ValidateOnBuild = true,
        ValidateScopes = true,
    };

    internal static IReadOnlyDictionary<string, string?> ValidSettings { get; } =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Default"] =
                "Host=localhost;Port=5432;Database=apptemplate_architecture_tests;Username=postgres;Password=postgres",

            //#if (localAuth)
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
            ["Identity:RequireUniqueEmail"] = "true",
            ["IdentitySeed:Enabled"] = "false",

            //#endif
            ["Localization:DefaultCulture"] = "en",

            //#if (localAuth)
            ["EmailConfirmation:ConfirmEmailUrl"] = "https://localhost:5001/confirm-email",

            ["EmailChange:ConfirmEmailChangeUrl"] = "https://localhost:5001/confirm-email-change",
            ["PasswordReset:ResetPasswordUrl"] = "https://localhost:5001/reset-password",

            //#endif
            ["Email:Host"] = "smtp.example.invalid",
            ["Email:Port"] = "587",
            ["Email:FromAddress"] = "no-reply@example.invalid",
            ["Email:FromName"] = "AppTemplate",
            ["Email:Security"] = "StartTls",
            //#if (files)

            ["Files:Storage:S3:BucketName"] = "app-template-architecture-tests",
            ["Files:Categories:document:AllowedMediaTypes:0"] = "application/pdf",
            ["Files:Categories:document:MaxSizeInBytes"] = "52428800",
            ["Files:Categories:document:UploadModes:0"] = "Direct",
            //#endif
        };

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

    internal static ServiceCollection ComposeApi(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        AddHostSuppliedServices(services, configuration);

        //#if (todoLists)
        services.AddTodoLists();
        //#endif
        //#if (reminders)
        services.AddReminders();
        //#endif
        //#if (files)
        services.AddFiles(configuration);
        //#endif
        //#if (notes)
        services.AddNotes();
        //#endif
        //#if (localAuth)
        services.AddAuthApplication();
        //#endif
        services.AddPurgeExpiredIdempotencyKeys();
        //#if (anyFeature)
        services.AddDeletedAccountSweep();
        //#endif
        services.AddCacheStore();
        services.AddPersistenceModule(configuration);
        //#if (localAuth)
        services.AddAuthModule(configuration);
        //#endif
        services.AddEmailModule(configuration);
        //#if (files)
        services.AddStorageModule(configuration);
        //#endif

        AddHostSuppliedAdapters(services);

        return services;
    }

    internal static ServiceCollection ComposeBackgroundOnly(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        AddHostSuppliedServices(services, configuration);

        //#if (todoLists)
        services.AddTodoLists();
        //#endif
        //#if (reminders)
        services.AddReminders();
        //#endif
        //#if (files)
        services.AddFiles(configuration);
        //#endif
        //#if (notes)
        services.AddNotes();
        //#endif
        //#if (localAuth)
        services.AddAuthApplication();
        //#endif
        services.AddPurgeExpiredIdempotencyKeys();
        //#if (anyFeature)
        services.AddDeletedAccountSweep();
        //#endif
        services.AddCacheStore();
        services.AddPersistenceModule(configuration);
        //#if (localAuth)
        services.AddAuthModule(configuration);
        //#endif
        services.AddEmailModule(configuration);
        //#if (files)
        services.AddStorageModule(configuration);
        //#endif

        AddBackgroundOnlyAdapters(services);

        return services;
    }

    //#if (externalAuth)
    internal const string ExternalIdentityNamespace = "AppTemplate.Application.Features.ExternalIdentity";

    internal static ServiceCollection ComposeApiWithExternalIdentity(IConfiguration configuration) =>
        WithExternalIdentity(ComposeApi(configuration), configuration);

    internal static ServiceCollection ComposeBackgroundOnlyWithExternalIdentity(IConfiguration configuration) =>
        WithExternalIdentity(ComposeBackgroundOnly(configuration), configuration);

    private static ServiceCollection WithExternalIdentity(ServiceCollection services, IConfiguration configuration)
    {
        services.AddExternalIdentity();
        services.AddExternalIdentityPersistence(configuration);

        return services;
    }

    //#endif
    internal static ServiceCollection ComposeTestHost(IConfiguration configuration)
    {
        var services = ComposeApi(configuration);

        services.AddInMemoryModule();

        return services;
    }

    //#if (localAuth)
    internal static ServiceCollection ComposeBackgroundOnlyWithoutAuthentication(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        AddHostSuppliedServices(services, configuration);

        //#if (todoLists)
        services.AddTodoLists();
        //#endif
        //#if (reminders)
        services.AddReminders();
        //#endif
        //#if (files)
        services.AddFiles(configuration);
        //#endif
        //#if (notes)
        services.AddNotes();
        //#endif
        services.AddPurgeExpiredIdempotencyKeys();
        //#if (anyFeature)
        services.AddDeletedAccountSweep();
        //#endif
        services.AddCacheStore();
        services.AddPersistenceModule(configuration);
        services.AddEmailModule(configuration);
        //#if (files)
        services.AddStorageModule(configuration);
        //#endif

        AddBackgroundOnlyAdapters(services);

        return services;
    }

    //#endif
    /*#if (localAuth)*/
    internal static ServiceCollection ComposeApiWithoutTheEmailModule(IConfiguration configuration)
    /*#else
    internal static ServiceCollection ComposeApiWithoutThePersistenceModule(IConfiguration configuration)
    #endif*/
    {
        var services = new ServiceCollection();

        AddHostSuppliedServices(services, configuration);

        //#if (todoLists)
        services.AddTodoLists();
        //#endif
        //#if (reminders)
        services.AddReminders();
        //#endif
        //#if (files)
        services.AddFiles(configuration);
        //#endif
        //#if (notes)
        services.AddNotes();
        //#endif
        //#if (localAuth)
        services.AddAuthApplication();
        //#endif
        services.AddPurgeExpiredIdempotencyKeys();
        //#if (anyFeature)
        services.AddDeletedAccountSweep();
        //#endif
        services.AddCacheStore();
        /*#if (localAuth)*/
        services.AddPersistenceModule(configuration);
        services.AddAuthModule(configuration);
        /*#else
        services.AddEmailModule(configuration);
        #endif*/

        AddHostSuppliedAdapters(services);

        return services;
    }

    //#if (localAuth)
    internal static ServiceCollection ComposeApiWithoutAuthentication(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        AddHostSuppliedServices(services, configuration);

        //#if (todoLists)
        services.AddTodoLists();
        //#endif
        //#if (reminders)
        services.AddReminders();
        //#endif
        //#if (files)
        services.AddFiles(configuration);
        //#endif
        //#if (notes)
        services.AddNotes();
        //#endif
        services.AddPurgeExpiredIdempotencyKeys();
        //#if (anyFeature)
        services.AddDeletedAccountSweep();
        //#endif
        services.AddCacheStore();
        services.AddPersistenceModule(configuration);
        services.AddEmailModule(configuration);
        //#if (files)
        services.AddStorageModule(configuration);
        //#endif

        AddHostSuppliedAdapters(services);

        return services;
    }

    //#endif
    private static void AddHostSuppliedServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.AddSingleton<IHostEnvironment>(new ArchitectureTestHostEnvironment());
        services.AddLogging();
    }

    private static void AddHostSuppliedAdapters(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, ArchitectureTestCurrentUser>();
        services.AddScoped<IAuditActor, ArchitectureTestAuditActor>();
        //#if (localAuth)
        services.AddScoped<ICurrentSession, ArchitectureTestCurrentSession>();
        //#endif
        //#if (reminders)
        services.AddScoped<IReminderRecipientDirectory, ArchitectureTestReminderRecipientDirectory>();
        //#endif
        services.AddScoped<ICurrentAccessToken, ArchitectureTestCurrentAccessToken>();
        //#if (anyFeature)
        services.AddScoped<IUserAccountDirectory, ArchitectureTestUserAccountDirectory>();
        //#endif
    }

    private static void AddBackgroundOnlyAdapters(IServiceCollection services)
    {
        services.AddScoped<ICurrentUser, ArchitectureTestBackgroundCurrentUser>();
        services.AddScoped<IAuditActor, ArchitectureTestBackgroundAuditActor>();
        //#if (localAuth)
        services.AddScoped<ICurrentSession, ArchitectureTestBackgroundCurrentSession>();
        //#endif
        //#if (reminders)
        services.AddScoped<IReminderRecipientDirectory, ArchitectureTestReminderRecipientDirectory>();
        //#endif
        services.AddScoped<ICurrentAccessToken, ArchitectureTestCurrentAccessToken>();
        //#if (anyFeature)
        services.AddScoped<IUserAccountDirectory, ArchitectureTestUserAccountDirectory>();
        //#endif
    }
}

internal sealed class ArchitectureTestCurrentUser : ICurrentUser
{
    private static readonly UserId _userId = UserId.Create(new("11111111-1111-1111-1111-111111111111"));

    public UserId? UserId => _userId;
}

//#if (localAuth)
internal sealed class ArchitectureTestCurrentSession : ICurrentSession
{
    private static readonly Guid _sessionId = new("22222222-2222-2222-2222-222222222222");

    public Guid? SessionId => _sessionId;

    public SessionClient Client => SessionClient.Unknown;
}

internal sealed class ArchitectureTestBackgroundCurrentSession : ICurrentSession
{
    public Guid? SessionId => throw new NotSupportedException(
        "A background scope has no current session. A use case that reads ICurrentSession.SessionId " +
        "cannot run unmodified from there.");

    public SessionClient Client => SessionClient.Unknown;
}

//#endif

internal sealed class ArchitectureTestAuditActor : IAuditActor
{
    private static readonly Guid _userId = new("11111111-1111-1111-1111-111111111111");

    public Guid? UserId => _userId;
}

internal sealed class ArchitectureTestBackgroundCurrentUser : ICurrentUser
{
    public UserId? UserId => throw new NotSupportedException(
        "A background scope has no current user. A use case that reads ICurrentUser.UserId cannot " +
        "run unmodified from there.");
}

//#if (reminders)
internal sealed class ArchitectureTestReminderRecipientDirectory : IReminderRecipientDirectory
{
    public Task<ReminderRecipient?> FindAsync(UserId ownerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<ReminderRecipient?>(null);
}
//#endif

internal sealed class ArchitectureTestCurrentAccessToken : ICurrentAccessToken
{
    public DateTimeOffset? IssuedAt => null;
}

//#if (anyFeature)
internal sealed class ArchitectureTestUserAccountDirectory : IUserAccountDirectory
{
    public Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);
}
//#endif

internal sealed class ArchitectureTestBackgroundAuditActor : IAuditActor
{
    public Guid? UserId => null;
}

internal sealed class ArchitectureTestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;

    public string ApplicationName { get; set; } = "AppTemplate.Architecture.Tests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
