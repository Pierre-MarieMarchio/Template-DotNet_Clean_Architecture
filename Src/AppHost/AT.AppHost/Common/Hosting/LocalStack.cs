using System.Text.RegularExpressions;

namespace AppTemplate.AppHost.Common.Hosting;

internal sealed partial class LocalStack
{
    private const string _healthPath = "/health";
    //#if (files)
    private const string _bucketName = "app-files";
    //#endif

    private readonly IDistributedApplicationBuilder _builder;
    private readonly ProjectFile _project;
    //#if (serverDatabase)
    private readonly IResourceBuilder<IResource> _database;
    //#endif
    private readonly ReferenceExpression _connectionString;
    private readonly IResourceBuilder<ContainerResource> _mailCatcher;
    private readonly IResourceBuilder<ParameterResource> _emailPassword;
    //#if (files)
    private readonly IResourceBuilder<ContainerResource> _objectStore;
    private readonly IResourceBuilder<ParameterResource> _storageSecretKey;
    //#endif
    //#if (localAuth)
    private readonly IResourceBuilder<ParameterResource> _jwtKey;
    private readonly IResourceBuilder<ParameterResource> _seedEnabled;
    private readonly IResourceBuilder<ParameterResource> _seedAdminPassword;
    //#endif
    /*#if (externalAuth)
    private readonly IResourceBuilder<ParameterResource> _authority;
    private readonly IResourceBuilder<ParameterResource> _audience;
    private readonly IResourceBuilder<ParameterResource> _interactiveClientId;
    private readonly IResourceBuilder<ParameterResource> _interactiveClientSecret;
    private readonly IResourceBuilder<ParameterResource> _browserClientId;
    #endif*/

    internal LocalStack(IDistributedApplicationBuilder builder, ProjectFile project)
    {
        _builder = builder;
        _project = project;

        var images = ContainerImages.FromCompose(project.Root);

        /*#if (sqlServer)
        var database = builder.AddSqlServer("db", port: 1433)
            .WithComposeImage(images)
            .WithEnvironment("MSSQL_MEMORY_LIMIT_MB", "1024");

        _database = database;
        _connectionString = ReferenceExpression.Create($"{database.Resource.ConnectionStringExpression};Database=appdb");
        #else*/
        var database = builder.AddPostgres("db", builder.AddParameter("db-username", "appuser"), port: 5432)
            .WithComposeImage(images)
            .WithEnvironment("POSTGRES_DB", "appdb")
            .WithEnvironment("POSTGRES_INITDB_ARGS", "--encoding=UTF8 --locale=C");

        _database = database;
        _connectionString = ReferenceExpression.Create(
            $"{database.Resource.ConnectionStringExpression};Database=appdb;Include Error Detail=true;Gss Encryption Mode=Disable");
        //#endif

        _mailCatcher = builder.AddContainer("mailpit", "axllent/mailpit")
            .WithComposeImage(images)
            .WithEnvironment("MP_SMTP_AUTH_ACCEPT_ANY", "true")
            .WithEnvironment("MP_SMTP_AUTH_ALLOW_INSECURE", "true")
            .WithEnvironment("MP_MAX_MESSAGES", "500")
            .WithHttpEndpoint(port: 8025, targetPort: 8025, name: "ui")
            .WithEndpoint(port: 1025, targetPort: 1025, scheme: "tcp", name: "smtp");

        _emailPassword = builder.AddParameter("email-password", string.Empty, secret: true);

        //#if (files)
        _storageSecretKey = builder.AddParameter("storage-secret-access-key", Generated(), secret: true, persist: true);

        _objectStore = builder.AddContainer("objectstore", "chrislusf/seaweedfs")
            .WithComposeImage(images)
            .WithArgs(
                "mini", "-dir=/data", "-s3.port=9000", $"-bucket={_bucketName}",
                "-s3.autoCreateBucket=false", "-master.telemetry=false",
                "-admin.ui=false", "-webdav=false", "-s3.port.iceberg=0", "-s3.port.lance=0")
            .WithEnvironment("AWS_ACCESS_KEY_ID", "app-template-dev")
            .WithEnvironment("AWS_SECRET_ACCESS_KEY", _storageSecretKey)
            .WithHttpEndpoint(port: 9000, targetPort: 9000, name: "s3")
            .WithHttpEndpoint(targetPort: 8888, name: "filer")
            .WithHttpHealthCheck("/healthz", endpointName: "s3")
            .WithHttpHealthCheck($"/buckets/{_bucketName}/", endpointName: "filer");
        //#endif

        //#if (localAuth)
        _jwtKey = builder.AddParameter("jwt-key", Generated(), secret: true, persist: true);
        _seedEnabled = builder.AddParameter("seed-enabled", "false");
        _seedAdminPassword = builder.AddParameter("seed-admin-password", Generated(), secret: true, persist: true);
        //#endif
        /*#if (externalAuth)
        _authority = builder.AddParameter("external-auth-authority");
        _audience = builder.AddParameter("external-auth-audience");
        _interactiveClientId = builder.AddParameter("external-auth-interactive-client-id", string.Empty);
        _interactiveClientSecret = builder.AddParameter("external-auth-interactive-client-secret", string.Empty, secret: true);
        _browserClientId = builder.AddParameter("external-auth-browser-client-id", string.Empty);
        #endif*/
    }

    internal void AddHosts()
    {
        bool workerRunsTheJobs = _project.Presentation.Any(host => host.Framework == "worker");
        var api = _project.Presentation
            .Where(host => host.Framework == "api")
            .Select(host => AddApi(host, runsTheJobs: !workerRunsTheJobs))
            .SingleOrDefault();

        foreach (var host in _project.Presentation)
        {
            switch (host.Framework)
            {
                case "blazor-server":
                    AddPortal(host, api);
                    break;
                case "mcp":
                    AddMcpServer(host, api);
                    break;
                case "worker":
                    AddWorker(host, api);
                    break;
            }
        }

        foreach (var app in _project.WebApps)
        {
            AddWebApp(app, api ?? throw new InvalidOperationException(
                $"{ProjectFile.FileName} records the web app {app.Name}, which relays to the API, but no API."));
        }
    }

    private IResourceBuilder<ProjectResource> AddApi(PresentationProject host, bool runsTheJobs)
    {
        var api = AddHost("api", host)
            .WithHttpEndpoint(port: 8080, name: "http")
            .WithHttpHealthCheck(_healthPath);

        WaitForTheServices(api);

        return runsTheJobs ? WithJobs(api) : api;
    }

    private void AddPortal(PresentationProject host, IResourceBuilder<ProjectResource>? api)
    {
        var portal = AddHost($"presentation-{Kebab(host.Name)}", host)
            .WithHttpEndpoint(name: "http")
            .WithHttpHealthCheck(_healthPath);

        WaitForTheApiOrTheServices(portal, api);
        /*#if (externalAuth)

        portal
            .WithEnvironment("Authentication__External__Interactive__ClientId", _interactiveClientId)
            .WithEnvironment("Authentication__External__Interactive__ClientSecret", _interactiveClientSecret);
        #endif*/
    }

    private void AddMcpServer(PresentationProject host, IResourceBuilder<ProjectResource>? api)
    {
        var server = AddHost($"presentation-{Kebab(host.Name)}", host)
            .WithHttpEndpoint(name: "http")
            .WithHttpHealthCheck(_healthPath);

        WaitForTheApiOrTheServices(server, api);
        /*#if (externalAuth)

        server.WithEnvironment("Mcp__Resource", $"{server.GetEndpoint("http")}/mcp");
        #endif*/
    }

    private void AddWorker(PresentationProject host, IResourceBuilder<ProjectResource>? api) =>
        WaitForTheApiOrTheServices(
            WithJobs(AddHost("worker", host).WithEnvironment("DOTNET_ENVIRONMENT", "Development")),
            api);

    private void WaitForTheApiOrTheServices(IResourceBuilder<ProjectResource> host, IResourceBuilder<ProjectResource>? api)
    {
        if (api is null)
        {
            WaitForTheServices(host);
        }
        else
        {
            host.WaitFor(api);
        }
    }

    private void WaitForTheServices(IResourceBuilder<ProjectResource> host)
    {
        //#if (serverDatabase)
        host.WaitFor(_database);
        //#endif
        host.WaitForStart(_mailCatcher);
        //#if (files)
        host.WaitFor(_objectStore);
        //#endif
    }

    private static IResourceBuilder<ProjectResource> WithJobs(IResourceBuilder<ProjectResource> host)
    {
        host
            .WithEnvironment("Jobs__Enabled", "true")
            .WithEnvironment("Jobs__Maintenance__Interval", "01:00:00")
            .WithEnvironment("Jobs__Maintenance__PurgeExpiredIdempotencyKeysEnabled", "true");
        //#if (localAuth)
        host.WithEnvironment("Jobs__Maintenance__PurgeExpiredRefreshTokensEnabled", "true");
        //#endif
        //#if (reminders)
        host
            .WithEnvironment("Jobs__Reminders__Interval", "00:01:00")
            .WithEnvironment("Jobs__Reminders__Enabled", "true");
        //#endif
        //#if (files)
        host
            .WithEnvironment("Jobs__Files__InspectDepositedFilesInterval", "00:01:00")
            .WithEnvironment("Jobs__Files__InspectDepositedFilesEnabled", "true")
            .WithEnvironment("Jobs__Files__PurgeAbandonedRegistrationsInterval", "01:00:00")
            .WithEnvironment("Jobs__Files__PurgeAbandonedRegistrationsEnabled", "true")
            .WithEnvironment("Jobs__Files__ReclaimOrphanedContentInterval", "12:00:00")
            .WithEnvironment("Jobs__Files__ReclaimOrphanedContentEnabled", "true");
        //#endif

        return host;
    }

    private void AddWebApp(WebApp app, IResourceBuilder<ProjectResource> api)
    {
        string workspace = Path.Combine(_project.Root, "Src", "Web");
        string context = PublishesTheDotNetClient(app) ? _project.Root : workspace;
        string dockerfile = Path.Combine(workspace, "apps", app.Name, "Dockerfile");

        var web = _builder
            .AddDockerfile($"web-{Kebab(app.Name)}", context, Path.GetRelativePath(context, dockerfile))
            .WithHttpEndpoint(port: app.Port, targetPort: 8080, name: "http")
            .WithHttpHealthCheck("/")
            .WithEnvironment("API_UPSTREAM", api.GetEndpoint("http"))
            .WaitFor(api);
        /*#if (externalAuth)

        if (app.Rendering == "ssr")
        {
            web
                .WithEnvironment("EXTERNAL_AUTH_AUTHORITY", _authority)
                .WithEnvironment("EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID", _interactiveClientId)
                .WithEnvironment("EXTERNAL_AUTH_INTERACTIVE_CLIENT_SECRET", _interactiveClientSecret)
                .WithEnvironment("EXTERNAL_AUTH_INTERACTIVE_SCOPE", string.Empty)
                .WithEnvironment("PUBLIC_ORIGIN", web.GetEndpoint("http"));
        }

        if (app.Framework == "blazor-wasm")
        {
            web
                .WithEnvironment("SIGN_IN_AUTHORITY", _authority)
                .WithEnvironment("SIGN_IN_CLIENT_ID", _browserClientId);
        }
        #endif*/
    }

    private static bool PublishesTheDotNetClient(WebApp app) => app.Framework == "blazor-wasm";

    private IResourceBuilder<ProjectResource> AddHost(string name, PresentationProject host)
    {
        var project = _builder.AddProject(name, _project.ProjectPath(host.Name), options => options.ExcludeLaunchProfile = true)
            .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
            .WithEnvironment("Database__MaxPoolSize", "20")
            .WithEnvironment("Database__CommandTimeoutSeconds", "30")
            .WithEnvironment("IdempotencyPurge__BatchSize", "1000")
            .WithEnvironment("Localization__DefaultCulture", "en")
            .WithEnvironment("ConnectionStrings__Default", _connectionString);

        //#if (localAuth)
        project
            .WithEnvironment("Jwt__Key", _jwtKey)
            .WithEnvironment("Jwt__Issuer", "AppTemplate.Api.Development")
            .WithEnvironment("Jwt__Audience", "AppTemplate.Client.Development")
            .WithEnvironment("Jwt__RequireHttpsMetadata", "false")
            .WithEnvironment("Jwt__AccessTokenLifetimeInMinutes", "60")
            .WithEnvironment("RefreshToken__LifetimeInDays", "7")
            .WithEnvironment("Identity__PasswordRequiredLength", "8")
            .WithEnvironment("Identity__PasswordRequiredUniqueChars", "1")
            .WithEnvironment("Identity__PasswordRequireDigit", "false")
            .WithEnvironment("Identity__PasswordRequireLowercase", "false")
            .WithEnvironment("Identity__PasswordRequireUppercase", "false")
            .WithEnvironment("Identity__PasswordRequireNonAlphanumeric", "false")
            .WithEnvironment("Identity__LockoutEnabled", "true")
            .WithEnvironment("Identity__LockoutMaxFailedAccessAttempts", "10")
            .WithEnvironment("Identity__LockoutDurationInMinutes", "1")
            .WithEnvironment("Identity__RequireConfirmedEmail", "true")
            .WithEnvironment("Identity__RequireUniqueEmail", "true")
            .WithEnvironment("EmailConfirmation__ConfirmEmailUrl", "http://localhost:8080/confirm-email")
            .WithEnvironment("PasswordReset__ResetPasswordUrl", "http://localhost:8080/reset-password")
            .WithEnvironment("EmailChange__ConfirmEmailChangeUrl", "http://localhost:8080/confirm-email-change")
            .WithEnvironment("IdentitySeed__Enabled", _seedEnabled)
            .WithEnvironment("IdentitySeed__AdminEmail", "administrator@localhost")
            .WithEnvironment("IdentitySeed__AdminPassword", _seedAdminPassword);
        //#endif
        /*#if (externalAuth)
        project
            .WithEnvironment("Authentication__External__Authority", _authority)
            .WithEnvironment("Authentication__External__Audience", _audience)
            .WithEnvironment("Authentication__External__RequireHttpsMetadata", "true")
            .WithEnvironment("Authentication__External__Roles__Source", "Local")
            .WithEnvironment("Authentication__External__Roles__ClaimPath", string.Empty)
            .WithEnvironment("Authentication__External__Roles__AdministratorRole", "Admin");
        #endif*/
        //#if (files)
        var storage = _objectStore.GetEndpoint("s3");

        project
            .WithEnvironment("Files__Storage__S3__BucketName", _bucketName)
            .WithEnvironment("Files__Storage__S3__Endpoint", storage)
            .WithEnvironment("Files__Storage__S3__PublicEndpoint", storage)
            .WithEnvironment("Files__Storage__S3__ForcePathStyle", "true")
            .WithEnvironment("Files__Storage__S3__AllowInsecureTransport", "true")
            .WithEnvironment("Files__Storage__S3__AccessKeyId", "app-template-dev")
            .WithEnvironment("Files__Storage__S3__SecretAccessKey", _storageSecretKey);
        //#endif

        var smtp = _mailCatcher.GetEndpoint("smtp");

        return project
            .WithEnvironment("Email__Host", $"{smtp.Property(EndpointProperty.Host)}")
            .WithEnvironment("Email__Port", $"{smtp.Property(EndpointProperty.Port)}")
            .WithEnvironment("Email__Security", "None")
            .WithEnvironment("Email__AllowInsecureTransport", "true")
            .WithEnvironment("Email__UserName", string.Empty)
            .WithEnvironment("Email__Password", _emailPassword)
            .WithEnvironment("Email__FromAddress", "no-reply@localhost")
            .WithEnvironment("Email__FromName", "AppTemplate (dev)")
            .WithEnvironment("Cors__AllowedOrigins__0", "http://localhost:4200")
            .WithEnvironment(ExportTelemetryToTheDashboard);
    }

    private static void ExportTelemetryToTheDashboard(EnvironmentCallbackContext context)
    {
        var variables = context.EnvironmentVariables;

        if (!variables.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out object? endpoint))
        {
            return;
        }

        variables["OpenTelemetry__Enabled"] = "true";
        variables["OpenTelemetry__OtlpEndpoint"] = endpoint;
        variables["OpenTelemetry__OtlpProtocol"] =
            variables.TryGetValue("OTEL_EXPORTER_OTLP_PROTOCOL", out object? protocol) && protocol is "http/protobuf"
                ? "HttpProtobuf"
                : "Grpc";

        if (variables.TryGetValue("OTEL_SERVICE_NAME", out object? serviceName))
        {
            variables["OpenTelemetry__ServiceName"] = serviceName;
        }
    }

    private static GenerateParameterDefault Generated() => new() { MinLength = 48, Special = false };

    internal static string Kebab(string name) =>
        Separators().Replace(LowerThenUpper().Replace(UpperRunThenWord().Replace(name, "$1-$2"), "$1-$2"), "-")
            .ToLowerInvariant();

    [GeneratedRegex("([A-Z]+)([A-Z][a-z])", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex UpperRunThenWord();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex LowerThenUpper();

    [GeneratedRegex(@"[.\s_-]+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Separators();
}
