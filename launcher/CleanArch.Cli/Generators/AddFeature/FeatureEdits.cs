using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;
using static CleanArch.Cli.Generators.Shared.CSharpNames;
using static CleanArch.Cli.Generators.Shared.FeatureCode;

namespace CleanArch.Cli.Generators.AddFeature;

internal static class FeatureEdits
{
    internal static string[] SweepMethod() =>
    [
        "public static IServiceCollection AddDeletedAccountSweep(this IServiceCollection services)",
        "{",
        "    ArgumentNullException.ThrowIfNull(services);",
        string.Empty,
        "    return services.AddUseCasePipeline();",
        "}",
    ];

    internal static string[] DirectoryDouble() =>
    [
        "internal sealed class ArchitectureTestUserAccountDirectory : IUserAccountDirectory",
        "{",
        "    public Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default) =>",
        "        Task.FromResult(true);",
        "}",
    ];

    internal static readonly Regex _fileNamespace = new(@"^namespace\s+([A-Za-z0-9_.]+);", RegexOptions.Multiline, TimeSpan.FromSeconds(2));

    internal static string NamespaceOf(List<Emission> plan, string type) =>
        plan.FirstOrDefault(emission => emission.Path.EndsWith($"/{type}.cs", StringComparison.Ordinal)) is { } declaring
            && _fileNamespace.Match(declaring.Content) is { Success: true } match
                ? match.Groups[1].Value
                : throw new InvalidOperationException($"No file of the plan declares {type}, which an edit names.");

    internal static TextEdits.Editor Edits(string root, FeatureSpec spec, List<Emission> plan)
    {
        var editor = new TextEdits.Editor(root);
        string[] hosts = ComposingHosts(root, spec);
        string composition = $"{ProjectOf(spec, "tests/Architecture", "Architecture.Tests")}/Composition/HostComposition.cs";

        EditContext(editor, spec, plan);
        EditApplicationModule(editor, spec, plan);
        EditPersistenceModule(editor, spec, plan);
        EditHosts(editor, spec, hosts, composition);
        EditModuleTests(editor, root, spec);

        if (spec.Reaction is { } adapter)
        {
            Reaction(editor, root, spec, hosts, adapter, composition);
        }

        if (WritesTheFieldTests(spec, plan))
        {
            string database = TestDatabasePath(spec);
            editor.Step = $"the schema of {database}";
            editor.Place(database, ["AppDbContext.PlatformSchema,"], before: true, [$"            AppDbContext.{spec.Features}Schema,"], $"AppDbContext.{spec.Features}Schema,");
        }

        if (spec.HasApi)
        {
            EditApiReference(editor, spec);
        }

        return editor;
    }

    private static void EditContext(TextEdits.Editor editor, FeatureSpec spec, List<Emission> plan)
    {
        string f = spec.Features;
        string a = spec.Type;
        string context = $"{PersistenceProject(spec)}/Common/Contexts/AppDbContext.cs";
        editor.Step = $"edit 1 ({context})";
        editor.Place(context, ["public const string PlatformSchema"], before: true, [$"    public const string {f}Schema = \"{Snake(f)}\";", string.Empty], $"public const string {f}Schema ");
        editor.Place(context, ["internal DbSet<IdempotencyRecord>"], before: true, [$"    internal DbSet<{a}Record> {f} => Set<{a}Record>();", string.Empty], $"internal DbSet<{a}Record> {f} ");
        editor.Place(
            context,
            ["modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());"],
            before: true,
            [$"        modelBuilder.ApplyConfiguration(new {a}RecordConfiguration());", string.Empty],
            $"modelBuilder.ApplyConfiguration(new {a}RecordConfiguration());");
        editor.Using(context, NamespaceOf(plan, $"{a}RecordConfiguration"));
        editor.Using(context, NamespaceOf(plan, $"{a}Record"));
    }

    private static void EditApplicationModule(TextEdits.Editor editor, FeatureSpec spec, List<Emission> plan)
    {
        string p = spec.Prefix;
        string f = spec.Features;
        string a = spec.Type;
        bool owner = spec.Access == "owner";
        string consumer = $"{(spec.Deletion == "anonymise" ? "Anonymise" : "Delete")}{a}OnAccountDeletedConsumer";
        string module = $"{ProjectOf(spec, "src/Application", "Application")}/ApplicationModule.cs";
        bool service = spec.Model == "record" || Flags(spec).Contains("change");
        editor.Step = $"edit 2 ({module})";
        editor.Place(
            module,
            ["private static void AddFeature("],
            before: true,
            [
                $"    public static IServiceCollection Add{f}(this IServiceCollection services)",
                "    {",
                "        ArgumentNullException.ThrowIfNull(services);",
                string.Empty,
                $"        services.AddFeature(\"{f}\");",
                .. service ? [$"        services.AddScoped<I{a}Service, {a}Service>();"] : Array.Empty<string>(),
                .. owner
                    ? [
                        string.Empty,
                        $"        services.AddDomainEventConsumer<UserAccountDeleted, {consumer}>();",
                        $"        services.AddScoped<IDeletedAccountDataPolicy, {consumer}>();",
                    ]
                    : Array.Empty<string>(),
                string.Empty,
                "        return services;",
                "    }",
                string.Empty,
            ],
            $"public static IServiceCollection Add{f}(");

        if (service)
        {
            editor.Using(module, NamespaceOf(plan, $"I{a}Service"));
        }

        if (owner)
        {
            editor.Using(module, NamespaceOf(plan, consumer));
            editor.Using(module, $"{p}.Application.Core");
            editor.Using(module, $"{p}.Application.Core.Common.Events");
            editor.Using(module, $"{p}.Application.Core.Common.Ports");
        }
    }

    private static void EditPersistenceModule(TextEdits.Editor editor, FeatureSpec spec, List<Emission> plan)
    {
        string f = spec.Features;
        string persistenceModule = $"{PersistenceProject(spec)}/PersistenceModule.cs";
        var adapters = Adapters(spec);
        editor.Step = $"edit 3 ({persistenceModule})";
        editor.Place(persistenceModule, ["AddIdempotencyFeature(services, configuration);"], before: true, [$"        Add{f}Feature(services);"], $"Add{f}Feature(services);");
        editor.Place(
            persistenceModule,
            ["private static void AddIdempotencyFeature("],
            before: true,
            [$"    private static void Add{f}Feature(IServiceCollection services)", "    {", .. adapters.Select(adapter => "        " + adapter.Line), "    }", string.Empty],
            $"private static void Add{f}Feature(");

        foreach (string space in adapters.SelectMany(adapter => adapter.Types).Select(type => NamespaceOf(plan, type)).Distinct(StringComparer.Ordinal))
        {
            editor.Using(persistenceModule, space);
        }
    }

    private static List<(string Line, string[] Types)> Adapters(FeatureSpec spec)
    {
        string a = spec.Type;
        var flags = Flags(spec);
        var adapters = new List<(string Line, string[] Types)>();

        if (spec.Model != "record")
        {
            adapters.Add(($"services.TryAddSingleton<I{a}Mapper, {a}Mapper>();", [$"I{a}Mapper", $"{a}Mapper"]));
            adapters.Add(($"AddScopedTracker<{a}Tracker, I{a}Tracker>(services);", [$"{a}Tracker", $"I{a}Tracker"]));
            adapters.Add(($"services.TryAddScoped<I{a}Repository, {a}Repository>();", [$"I{a}Repository", $"{a}Repository"]));
        }
        else if (flags.Contains("stage"))
        {
            adapters.Add(($"services.TryAddScoped<I{a}Store, {a}Store>();", [$"I{a}Store", $"{a}Store"]));
        }

        if (flags.Contains("rq"))
        {
            adapters.Add(($"services.TryAddScoped<I{a}Queries, {a}Queries>();", [$"I{a}Queries", $"{a}Queries"]));
        }

        return adapters;
    }

    private static void EditHosts(TextEdits.Editor editor, FeatureSpec spec, string[] hosts, string composition)
    {
        string f = spec.Features;

        foreach (string host in hosts)
        {
            string program = $"src/Presentation/{host}/Program.cs";
            editor.Step = $"edit 4 ({program})";
            editor.Place(
                program,
                ["builder.Services.AddPurgeExpiredIdempotencyKeys();", "builder.Services.AddCacheStore();"],
                before: true,
                [$"builder.Services.Add{f}();", string.Empty],
                $"builder.Services.Add{f}();");
            editor.Using(program, $"{spec.Prefix}.Application");
        }

        editor.Step = $"edit 5 ({composition})";
        editor.PlaceInEachMethod(composition, "services.AddPurgeExpiredIdempotencyKeys();", before: true, $"        services.Add{f}();");
        editor.Using(composition, $"{spec.Prefix}.Application");
    }

    private static void EditModuleTests(TextEdits.Editor editor, string root, FeatureSpec spec)
    {
        string moduleTests = $"{ProjectOf(spec, "tests/Application", "Application.UnitTests")}/ApplicationModuleTests.cs";

        if (File.Exists(System.IO.Path.Combine(root, moduleTests)))
        {
            editor.Step = $"edit 6 ({moduleTests})";
            editor.AppendToMethod(moduleTests, "ServiceCollection ComposeEveryFeature()", "services.Add", $"        services.Add{spec.Features}();");
        }
    }

    private static void EditApiReference(TextEdits.Editor editor, FeatureSpec spec)
    {
        List<string> section = RouteSection(spec);

        editor.Step = "the route section of docs/reference/api.md";
        editor.Section("docs/reference/api.md", "## Routes", [.. section, string.Empty], section[2]);

        if (Flags(spec).Contains("notfound"))
        {
            editor.Step = "the error-code row of docs/reference/api.md";
            editor.Row("docs/reference/api.md", "### Resources", FeatureReport.ErrorRow(spec), $"| `{CSharpNames.Lower(spec.Type)}.notFound` |");
        }
    }

    internal static void Reaction(TextEdits.Editor editor, string root, FeatureSpec spec, string[] hosts, string adapter, string composition)
    {
        string p = spec.Prefix;
        string jobsHost = JobsHost(root, p);
        string directory = adapter == "Auth" ? "AccountUserAccountDirectory" : "ExternalIdentityUserAccountDirectory";
        string module = adapter == "Auth" ? "services.AddAuthModule(configuration);" : "services.AddExternalIdentityPersistence(configuration);";

        string core = $"{ProjectOf(spec, "src/Application", "Application.Core")}/ApplicationCoreModule.cs";
        editor.Step = $"edit 7 ({core})";
        editor.Place(
            core,
            ["public static IServiceCollection AddUseCasePipeline("],
            before: true,
            [.. SweepMethod().Select(line => line.Length == 0 ? line : "    " + line), string.Empty],
            "public static IServiceCollection AddDeletedAccountSweep(");

        string program = $"src/Presentation/{jobsHost}/Program.cs";
        editor.Step = $"edit 8 ({program})";
        editor.Place(
            program,
            ["builder.Services.AddPurgeExpiredIdempotencyKeys();"],
            before: false,
            [string.Empty, "builder.Services.AddDeletedAccountSweep();"],
            "builder.Services.AddDeletedAccountSweep();");
        editor.Using(program, $"{p}.Application.Core");

        string jobs = $"src/Presentation/{jobsHost}/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs";
        editor.Step = $"edit 9 ({jobs})";
        editor.Place(jobs, ["services.AddMaintenanceJobs(configuration);"], before: false, ["        services.AddDeletedAccountSweepJobs(configuration);"], "services.AddDeletedAccountSweepJobs(");

        foreach (string host in hosts)
        {
            string hostComposition = HostComposition(root, host, adapter);
            editor.Step = $"edit 10 ({hostComposition})";
            editor.Place(
                hostComposition,
                [module],
                before: false,
                [string.Empty, $"        services.AddScoped<IUserAccountDirectory, {directory}>();"],
                "services.AddScoped<IUserAccountDirectory, ");
            editor.Using(hostComposition, $"{p}.Application.Core.Common.Ports");
        }

        editor.Step = $"edit 11 ({composition})";
        editor.PlaceInEachMethod(composition, "services.AddPurgeExpiredIdempotencyKeys();", before: false, "        services.AddDeletedAccountSweep();");
        editor.PlaceInEachMethod(
            composition,
            "services.AddScoped<ICurrentAccessToken, ArchitectureTestCurrentAccessToken>();",
            before: false,
            "        services.AddScoped<IUserAccountDirectory, ArchitectureTestUserAccountDirectory>();");
        editor.Place(
            composition,
            ["internal sealed class ArchitectureTestBackgroundAuditActor"],
            before: true,
            [.. DirectoryDouble(), string.Empty],
            "internal sealed class ArchitectureTestUserAccountDirectory");
    }

    internal static List<string> RouteSection(FeatureSpec spec)
    {
        string item = $"/{{{CSharpNames.Lower(spec.Type)}Id}}";

        (string Operation, string Row)[] rows =
        [
            ("list", "| GET | `/` | 200 | |"),
            ("get", $"| GET, HEAD | `{item}` | 200 | |"),
            ("create", "| POST | `/` | 201 | |"),
            ("update", $"| PUT | `{item}` | 200 | |"),
            ("delete", $"| DELETE | `{item}` | 204 | |"),
        ];

        return
        [
            $"### {char.ToUpperInvariant(spec.Features[0]) + Separated(spec.Features, ' ')[1..].ToLowerInvariant()}",
            string.Empty,
            $"Under `/api/v1/{Kebab(spec.Features)}`.",
            string.Empty,
            "| Method | Route | Success | Notes |",
            "|---|---|---|---|",
            .. rows.Where(row => spec.Has(row.Operation)).Select(row => row.Row),
        ];
    }
}
