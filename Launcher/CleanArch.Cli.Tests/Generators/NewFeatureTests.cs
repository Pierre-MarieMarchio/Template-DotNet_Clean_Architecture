using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.AddFeature;
using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;
using Xunit;
using static CleanArch.Cli.Generators.AddFeature.FeatureEdits;
using static CleanArch.Cli.Generators.AddFeature.FeatureGeneration;
using static CleanArch.Cli.Generators.AddFeature.FeaturePage;
using static CleanArch.Cli.Generators.AddFeature.FeatureReport;
using static CleanArch.Cli.Generators.AddFeature.NewFeature;
using static CleanArch.Cli.Generators.Shared.FeatureCode;

namespace CleanArch.Cli.Tests.Generators;

public sealed partial class NewFeatureTests
{
    internal const string Version = "1.2.3";

    [Fact]
    public void Parser() => Expectations.Hold(ParserCases);

    [Fact]
    public void Combination() => Expectations.Hold(CombinationCases);

    [Fact]
    public void NamingAndPrefix() => Expectations.Hold(NamingAndPrefixCases);

    [Fact]
    public void Subset() => Expectations.Hold(SubsetCases);

    [Fact]
    public void Applied() => Expectations.Hold(AppliedCases);

    [Fact]
    public void AllOrNothing() => Expectations.Hold(AllOrNothingCases);

    [Fact]
    public void Idempotence() => Expectations.Hold(IdempotenceCases);

    [Fact]
    public void DryRun() => Expectations.Hold(DryRunCases);

    [Fact]
    public void Precondition() => Expectations.Hold(PreconditionCases);

    [Fact]
    public void Step() => Expectations.Hold(StepCases);

    [Fact]
    public void NoOverwrite() => Expectations.Hold(NoOverwriteCases);

    [Fact]
    public void Manifest() => Expectations.Hold(ManifestCases);

    [Fact]
    public void Rollback() => Expectations.Hold(RollbackCases);

    [Fact]
    public void Reaction() => Expectations.Hold(ReactionCases);

    [Fact]
    public void Page() => Expectations.Hold(PageCases);

    [Fact]
    public void WithoutAnApi() => Expectations.Hold(WithoutTheApiCases);

    [Fact]
    public void WithoutFields() => Expectations.Hold(WithoutFieldsCases);

    [Fact]
    public void Provider() => Expectations.Hold(ProviderCases);

    [Fact]
    public void Instant() => Expectations.Hold(InstantCases);

    [Fact]
    public void TheFieldGrammar() => Expectations.Hold(FieldGrammarCases);

    [Fact]
    public void FieldEmission() => Expectations.Hold(FieldEmissionCases);

    [Fact]
    public void FieldAddition() => Expectations.Hold(FieldAdditionCases);

    [Fact]
    public void FieldFormatting() => Expectations.Hold(FieldFormattingCases);

    [Fact]
    public void FieldRefusal() => Expectations.Hold(FieldRefusalCases);

    [Fact]
    public void ManifestIdentity() => Expectations.Hold(ManifestIdentityCases);

    [Fact]
    public void FieldHandEdit() => Expectations.Hold(FieldHandEditCases);

    [Fact]
    public void FieldResume() => Expectations.Hold(FieldResumeCases);

    [Fact]
    public void FieldShape() => Expectations.Hold(FieldShapeCases);

    [Fact]
    public void TemplateGuard() => Expectations.Hold(TemplateGuardCases);

    internal static string NewTree(string scratch, bool reaction = true, string database = "postgresql")
    {
        string root = System.IO.Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        string[] directories =
        [
            "Src/Domain/A.Domain",
            "Src/Application/A.Application",
            "Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts",
            "Src/Presentation/A.Api",
            "Tests/Application/A.Application.UnitTests",
            "Tests/Domain/A.Domain.UnitTests",
            "Tests/Infrastructure/A.Infrastructure.Persistence.UnitTests",
            "Tests/Presentation/A.Api.UnitTests",
        ];

        foreach (string directory in directories)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(root, directory));
        }

        File.WriteAllText(
            System.IO.Path.Combine(root, "cleanarch.json"),
            $"{{\n  \"name\": \"Acme\",\n  \"options\": {{\n    \"database\": \"{database}\"\n  }}\n}}\n");

        foreach (var (path, content) in ComposableFiles())
        {
            Put(root, path, content);
        }

        if (reaction)
        {
            Put(root, "Src/Application/A.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs", "// the reaction this tree already ships\n");
        }
        else
        {
            Put(root, "Src/Application/A.Application.Core/ApplicationCoreModule.cs", _coreModule);
            Put(root, "Src/Presentation/A.Api/Features/Auth/Composition/AuthComposition.cs", "        services.AddAuthModule(configuration);\n");
            Put(root, "Src/Presentation/A.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs", "        services.AddMaintenanceJobs(configuration);\n");
        }

        return root;
    }

    private static void Put(string root, string relative, string content)
    {
        string full = System.IO.Path.Combine(root, relative);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private const string _coreModule =
        """
        namespace Acme.Application.Core;

        public static class ApplicationCoreModule
        {
            public static IServiceCollection AddPurgeExpiredIdempotencyKeys(this IServiceCollection services) =>
                services.AddUseCasePipeline();

            public static IServiceCollection AddUseCasePipeline(this IServiceCollection services) => services;
        }

        """;

    private const string _hostProgram =
        """
        using Acme.Application.Core;

        builder.Services.AddPurgeExpiredIdempotencyKeys();

        builder.Services.AddCacheStore();

        """;

    private static (string Path, string Content)[] ComposableFiles() =>
    [
        (
            "Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs",
            """
            using Microsoft.EntityFrameworkCore;

            namespace Acme.Infrastructure.Persistence.Common.Contexts;

            public sealed class AppDbContext : DbContext
            {
                public const string PlatformSchema = "platform";

                internal DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();

                protected override void OnModelCreating(ModelBuilder modelBuilder)
                {
                    modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());
                }
            }

            """),
        ("Src/Infrastructure/A.Infrastructure.Persistence/Migrations/AppDbContextModelSnapshot.cs", "// the initial migration's snapshot\n"),
        (
            "Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs",
            """
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Infrastructure.Persistence;

            public static class PersistenceModule
            {
                public static IServiceCollection AddPersistenceModule(this IServiceCollection services, IConfiguration configuration)
                {
                    AddIdempotencyFeature(services, configuration);

                    return services;
                }

                private static void AddIdempotencyFeature(IServiceCollection services, IConfiguration configuration)
                {
                }
            }

            """),
        (
            "Src/Application/A.Application/ApplicationModule.cs",
            """
            using Acme.Application.Core;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Application;

            public static class ApplicationModule
            {
                private static void AddFeature(this IServiceCollection services, string vertical)
                {
                }
            }

            """),
        ("Src/Presentation/A.Api/Program.cs", _hostProgram),
        (
            "Tests/Application/A.Application.UnitTests/ApplicationModuleTests.cs",
            """
            namespace Acme.Application.UnitTests;

            public sealed class ApplicationModuleTests
            {
                [Fact]
                public void EveryValidator_IsRegisteredOnce()
                {
                    var services = ComposeEveryFeature();
                }

                [Fact]
                public void AFeatureCall_RegistersItsOwnVerticalAndNoOther()
                {
                    var services = new ServiceCollection();
                    services.AddTodoLists();

                    services.ShouldNotBeEmpty();
                }

                private static ServiceCollection ComposeEveryFeature()
                {
                    var services = new ServiceCollection();

                    services.AddTodoLists();

                    return services;
                }
            }

            """),
        (
            "Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs",
            """
            using Acme.Application.Core;
            using Microsoft.Extensions.DependencyInjection;

            namespace Acme.Architecture.Tests.Composition;

            internal static class HostComposition
            {
                internal static ServiceCollection ComposeApi(IConfiguration configuration)
                {
                    services.AddPurgeExpiredIdempotencyKeys();
                    services.AddPersistenceModule(configuration);
                }

                internal static ServiceCollection ComposeBackgroundOnly(IConfiguration configuration)
                {
                    services.AddPurgeExpiredIdempotencyKeys();
                    services.AddPersistenceModule(configuration);
                }

                private static void AddHostSuppliedAdapters(IServiceCollection services)
                {
                    services.AddScoped<ICurrentAccessToken, ArchitectureTestCurrentAccessToken>();
                }

                private static void AddBackgroundOnlyAdapters(IServiceCollection services)
                {
                    services.AddScoped<ICurrentAccessToken, ArchitectureTestCurrentAccessToken>();
                }
            }

            internal sealed class ArchitectureTestBackgroundAuditActor : IAuditActor
            {
            }

            """),
        (
            "docs/README.md",
            """
            # Documentation

            ## Adding a page

            """),
        (
            "docs/reference/api.md",
            """
            # API

            ## Routes

            ### Health

            | Method | Route | Success | Notes |
            |---|---|---|---|
            | GET | `/health/live` | 200 | |

            ## Error codes

            ### Resources

            | `code` | Status | When |
            |---|---|---|
            | `domain.invariantViolated` | 409 | A domain rule refused the change |

            ### The request itself

            """),
    ];

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args) => Invoke(root, _ => 0, args);

    private static (int Code, string Output, string Error) Invoke(string root, Func<TextEdits.Command, int> run, params string[] args)
    {
        var (code, output, error) = GeneratorRun.Invoke(AddFeatureCommand.Build(GeneratorRun.Project(root), _ => new FakeExecutor(run), Version), args);

        return (code, output.ReplaceLineEndings("\n"), error.ReplaceLineEndings("\n"));
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : "<directory>",
                StringComparer.Ordinal);

    private static bool Same(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);

    private static void ParserCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        (string[] Args, string Fragment)[] refusals =
        [
            (["Widgets", "Widget", "--access", "owner"], "--deletion is required"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "keep"], "--deletion 'keep' is not a choice"),
            (["Widgets", "Widget", "--access", "shared", "--deletion", "delete"], "Leave --deletion out"),
            (["Widgets", "Widget", "--access", "everyone"], "is not a choice"),
            (["Widgets", "Widget", "--access", "owner", "--operations", "get,fetch"], "'fetch' is not an operation"),
            (["Widgets", "Widget", "--access", "owner", "--operations", "get,,list"], "empty entry"),
            (["Widgets", "Widget", "--access", "owner", "--operations", ""], "empty entry"),
            (["Widgets", "Widget", "--access", "owner", "--operations", "get,get"], "names 'get' twice"),
            (["Widgets", "Widget", "--access", "owner", "--model", "entity"], "--model 'entity' is not a choice"),
            (["Widgets", "Widget", "--access", "owner", "--concurrency", "strong"], "--concurrency 'strong' is not a choice"),
            (["widgets", "Widget", "--access", "owner"], "is not a folder name"),
            (["Widgets", "widget", "--access", "owner"], "is not a type name"),
            (["Widget", "Widget", "--access", "owner"], "are both 'Widget'"),
            (["Common", "Widget", "--access", "owner"], "already a segment of every namespace"),
            (["Widgets", "Event", "--access", "owner"], "C# keyword"),
            (["Widgets", new string('W', 41), "--access", "owner"], "longer than 40"),
        ];

        var before = Snapshot(root);

        foreach (var (args, fragment) in refusals)
        {
            var result = Invoke(root, args);
            string label = string.Join(' ', args);

            expect(result.Code == 1, $"[{label}] exited {result.Code}, not 1.");
            expect(result.Error.Contains(fragment, StringComparison.Ordinal), $"[{label}] did not say '{fragment}': {result.Error.Split('\n')[0]}");
            expect(result.Error.Contains("Nothing was written.", StringComparison.Ordinal), $"[{label}] did not say nothing was written.");
            expect(Same(before, Snapshot(root)), $"[{label}] changed the tree.");
        }

        var several = Invoke(root, "widgets", "widget", "--access", "everyone", "--model", "entity");

        expect(
            several.Error.Split('\n').Count(line => line.StartsWith("error:", StringComparison.Ordinal)) >= 4,
            "Several mistakes in one command were not all reported.");
    }

    private static void CombinationCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var before = Snapshot(root);

        (string[] Args, string Fragment)[] refusals =
        [
            (["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--operations", "create,list"], "create needs get"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--operations", "update", "--concurrency", "etag"], "etag needs get"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--operations", "delete", "--concurrency", "etag"], "etag needs get"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--operations", "get,create", "--concurrency", "etag"], "guards update and delete"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--model", "aggregate"], "needs --concurrency etag"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--model", "aggregate", "--concurrency", "none"], "needs --concurrency etag"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--model", "aggregate", "--concurrency", "etag", "--operations", "get,list"], "at least one of create, update or delete"),
        ];

        foreach (var (args, fragment) in refusals)
        {
            var result = Invoke(root, args);
            string label = string.Join(' ', args[2..]);

            expect(result.Code == 1, $"[{label}] exited {result.Code}, not 1.");
            expect(result.Error.Contains(fragment, StringComparison.Ordinal), $"[{label}] did not say '{fragment}'.");
            expect(Same(before, Snapshot(root)), $"[{label}] changed the tree.");
        }
    }

    private static void NamingAndPrefixCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        var result = Invoke(root, "TodoLists", "TodoList", "--access", "shared");

        expect(result.Code == 0, $"A plain shared feature was refused: {result.Error}");

        string[] expected =
        [
            "Src/Application/A.Application/Features/TodoLists/Services/ITodoListService.cs",
            "Src/Application/A.Application/Features/TodoLists/Ports/TodoListStore/ITodoListStore.cs",
            "Src/Application/A.Application/Features/TodoLists/Ports/TodoListQueries/ITodoListQueries.cs",
            "Src/Infrastructure/A.Infrastructure.Persistence/Features/TodoLists/Rows/Models/TodoListRecord.cs",
            "Src/Presentation/A.Api/Features/TodoLists/Controllers/TodoListsController.cs",
            "Tests/Application/A.Application.UnitTests/Features/TodoLists/Services/TodoListServiceTests.cs",
            ".feature-manifest/TodoLists.json",
        ];

        foreach (string path in expected)
        {
            expect(File.Exists(System.IO.Path.Combine(root, path)), $"{path} was not written.");
        }

        string controller = File.ReadAllText(System.IO.Path.Combine(root, expected[4]));

        expect(controller.Contains("[Route(\"api/v{version:apiVersion}/todo-lists\")]", StringComparison.Ordinal), "The route is not the kebab-case of the folder.");
        expect(controller.Contains("namespace Acme.Api.Features.TodoLists.Controllers;", StringComparison.Ordinal), "The controller's namespace is not its project and folders.");

        string firstOwner = NewTree(scratch, reaction: false);
        var owned = Invoke(firstOwner, "Orders", "Order", "--access", "owner", "--deletion", "delete");
        string sweep = System.IO.Path.Combine(
            firstOwner, "Src/Application/A.Application.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountData.cs");

        expect(
            owned.Code == 0 && File.Exists(sweep) && File.ReadAllText(sweep).Contains("\"acme.accounts.sweep-deleted-data\"", StringComparison.Ordinal),
            $"The deleted-account sweep of a project's first owner feature does not name its lease after the project: {owned.Error}");

        string[] leaks = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("AppTemplate", StringComparison.Ordinal))
            .Select(path => System.IO.Path.GetRelativePath(root, path))];

        expect(leaks.Length == 0, $"The template's own name leaked into: {string.Join(", ", leaks)}");

        string[] ownedLeaks = [.. Directory.EnumerateFiles(firstOwner, "*", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("AppTemplate", StringComparison.OrdinalIgnoreCase))
            .Select(path => System.IO.Path.GetRelativePath(firstOwner, path))];

        expect(ownedLeaks.Length == 0, $"The template's own name leaked, in some case, into a project's first owner feature: {string.Join(", ", ownedLeaks)}");

        expect(result.Output.Contains("AddTodoLists", StringComparison.Ordinal), "The composition steps do not name AddTodoLists.");
        expect(result.Output.Contains("TodoListsSchema", StringComparison.Ordinal), "The composition steps do not name the schema constant.");
        expect(result.Output.Contains("migration-add", StringComparison.Ordinal), "The migration step is not printed.");
        expect(result.Output.Contains("last-write-wins", StringComparison.Ordinal), "The last-write-wins consequence of --concurrency none is not printed.");
        expect(result.Output.Contains("dotnet run Tools/Tasks.cs -- openapi --no-build", StringComparison.Ordinal), "The OpenAPI step is not printed.");
        expect(
            !result.Output.Contains("api-client", StringComparison.Ordinal) && !result.Output.Contains("pnpm api", StringComparison.Ordinal),
            "A step is printed for a client and a web workspace the project does not hold.");

        string contractRoot = NewTree(scratch);

        Directory.CreateDirectory(System.IO.Path.Combine(contractRoot, "Src/Presentation/A.Presentation.Client"));
        Directory.CreateDirectory(System.IO.Path.Combine(contractRoot, "Src/Web/packages/api-client/scripts"));
        File.WriteAllText(System.IO.Path.Combine(contractRoot, "Src/Web/packages/api-client/scripts/generate.mjs"), "// the script pnpm api runs\n");

        var ran = new List<TextEdits.Command>();
        var contract = Invoke(contractRoot, Recording(ran), "Notes", "Note", "--access", "shared");

        expect(
            Launched(ran).SequenceEqual(
            [
                "dotnet run Tools/Tasks.cs migration-check",
                "dotnet run Tools/Tasks.cs docs-index",
                "dotnet run Tools/Tasks.cs build",
                "dotnet run Tools/Tasks.cs -- migration-add AddNotes --no-build",
                "dotnet run Tools/Tasks.cs -- openapi --no-build",
                "dotnet run Tools/Tasks.cs api-client",
                "pnpm api --no-build",
            ]),
            $"The check, the one build, then the migration, the OpenAPI reference, the client and the web schema from that build were not run in that order: {string.Join(" | ", Launched(ran))}");
        expect(ran[^1].WorkingDirectory == System.IO.Path.Combine(contractRoot, "Src", "Web"), "pnpm api did not run in Src/Web.");
        expect(contract.Output.Contains("Running cd Src/Web && pnpm api --no-build", StringComparison.Ordinal), "The web step was not announced.");

        string vowelRoot = NewTree(scratch);
        var vowel = Invoke(vowelRoot, "Invoices", "Invoice", "--access", "shared");
        string vowelTests = File.ReadAllText(System.IO.Path.Combine(
            vowelRoot, "Tests/Application/A.Application.UnitTests/Features/Invoices/Services/InvoiceServiceTests.cs"));

        expect(vowel.Code == 0, $"A feature named with a vowel was refused: {vowel.Error}");
        expect(
            vowelTests.Contains("GetAsync_AnInvoiceThatIsNotThere_IsNotFound", StringComparison.Ordinal) && !vowelTests.Contains("_AInvoice", StringComparison.Ordinal),
            "A test name does not give a feature named with a vowel the article 'An'.");
    }

    private static IEnumerable<string> Launched(List<TextEdits.Command> ran) =>
        ran.Select(command => string.Join(' ', [System.IO.Path.GetFileNameWithoutExtension(command.FileName), .. command.Arguments]));

    private static Func<TextEdits.Command, int> Recording(List<TextEdits.Command> ran, string? failing = null) =>
        command =>
        {
            ran.Add(command);

            return command.Display == failing ? 1 : 0;
        };

    private static void AppliedCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var before = Snapshot(root);
        var ran = new List<TextEdits.Command>();
        var result = Invoke(root, Recording(ran), "Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--model", "aggregate", "--concurrency", "etag");
        var after = Snapshot(root);

        expect(result.Code == 0, $"The applied run was refused: {result.Error}");

        (string Path, string Line)[] wiring =
        [
            ("Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "public const string WidgetsSchema = \"widgets\";"),
            ("Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "internal DbSet<WidgetRecord> Widgets => Set<WidgetRecord>();"),
            ("Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "modelBuilder.ApplyConfiguration(new WidgetRecordConfiguration());"),
            ("Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "using Acme.Infrastructure.Persistence.Features.Widgets.Rows.Configurations;"),
            ("Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "using Acme.Infrastructure.Persistence.Features.Widgets.Rows.Models;"),
            ("Src/Application/A.Application/ApplicationModule.cs", "public static IServiceCollection AddWidgets(this IServiceCollection services)"),
            ("Src/Application/A.Application/ApplicationModule.cs", "services.AddFeature(\"Widgets\");"),
            ("Src/Application/A.Application/ApplicationModule.cs", "services.AddScoped<IWidgetService, WidgetService>();"),
            ("Src/Application/A.Application/ApplicationModule.cs", "services.AddScoped<IDeletedAccountDataPolicy, DeleteWidgetOnAccountDeletedConsumer>();"),
            ("Src/Application/A.Application/ApplicationModule.cs", "using Acme.Application.Features.Widgets.Services.Consumers;"),
            ("Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs", "AddWidgetsFeature(services);"),
            ("Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs", "AddScopedTracker<WidgetTracker, IWidgetTracker>(services);"),
            ("Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs", "services.TryAddScoped<IWidgetRepository, WidgetRepository>();"),
            ("Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs", "using Acme.Domain.Features.Widgets.Repositories;"),
            ("Src/Presentation/A.Api/Program.cs", "builder.Services.AddWidgets();"),
            ("Src/Presentation/A.Api/Program.cs", "using Acme.Application;"),
            ("Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs", "using Acme.Application;"),
            ("docs/reference/api.md", "Under `/api/v1/widgets`."),
            ("docs/reference/api.md", "| `widget.notFound` | 404 | Missing, or another caller's |"),
        ];

        foreach (var (path, line) in wiring)
        {
            expect(Occurrences(root, path, line) == 1, $"The applied run did not leave '{line}' once in {path}.");
        }

        expect(
            Occurrences(root, "Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs", "services.AddWidgets();") == 2,
            "The feature was not composed once in each composition of the architecture tests.");

        string[] moduleTests = File.ReadAllLines(System.IO.Path.Combine(root, "Tests/Application/A.Application.UnitTests/ApplicationModuleTests.cs"));
        int composed = Array.FindIndex(moduleTests, line => line.Trim() == "services.AddWidgets();");

        expect(
            moduleTests.Count(line => line.Trim() == "services.AddWidgets();") == 1
                && composed > Array.FindIndex(moduleTests, line => line.Contains("ServiceCollection ComposeEveryFeature()", StringComparison.Ordinal))
                && moduleTests[composed - 1].Trim() == "services.AddTodoLists();",
            "The feature was not composed in ComposeEveryFeature alone, after the features it composes.");

        string[] usings = [.. File.ReadAllLines(System.IO.Path.Combine(root, "Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs"))
            .Where(line => line.StartsWith("using ", StringComparison.Ordinal))];

        expect(
            usings.SequenceEqual(usings.Order(StringComparer.OrdinalIgnoreCase)),
            $"The usings added are out of order: {string.Join(" ", usings)}");

        string api = File.ReadAllText(System.IO.Path.Combine(root, "docs/reference/api.md"));

        expect(
            api.IndexOf("### Widgets", StringComparison.Ordinal) is var section and > 0
                && section > api.IndexOf("### Health", StringComparison.Ordinal)
                && section < api.IndexOf("## Error codes", StringComparison.Ordinal),
            "The route section was not placed after the others under ## Routes.");

        using (var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(root, ".feature-manifest/Widgets.json"))))
        {
            var edits = document.RootElement.GetProperty("edits").EnumerateArray().ToList();

            string[] edited = [.. before.Keys.Where(path => after[path] != before[path]).Order(StringComparer.Ordinal)];

            expect(
                edited.SequenceEqual(edits.Select(edit => edit.GetProperty("path").GetString()!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
                $"The manifest records edits in other files than the run edited: {string.Join(", ", edited)}");

            foreach (string path in edited)
            {
                List<string> file = [.. File.ReadAllLines(System.IO.Path.Combine(root, path))];

                foreach (var edit in edits.Where(edit => edit.GetProperty("path").GetString() == path))
                {
                    string anchor = edit.GetProperty("anchor").GetString()!;
                    bool preceding = edit.GetProperty("position").GetString() == "before";
                    string[] lines = [.. edit.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!)];
                    int at = Enumerable.Range(0, file.Count).FirstOrDefault(
                        index => file[index] == anchor
                            && (preceding
                                ? index >= lines.Length && file.Skip(index - lines.Length).Take(lines.Length).SequenceEqual(lines)
                                : file.Skip(index + 1).Take(lines.Length).SequenceEqual(lines)),
                        -1);

                    expect(at >= 0, $"The manifest records lines {(preceding ? "before" : "after")} '{anchor}' in {path} that are not there.");

                    if (at >= 0)
                    {
                        file.RemoveRange(preceding ? at - lines.Length : at + 1, lines.Length);
                    }
                }

                expect(
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', file) + "\n"))) == before[path],
                    $"Undoing the edits the manifest records in {path} does not give back the file as it was.");
            }

            expect(
                edits.All(edit => edit.GetProperty("path").GetString() != "docs/README.md"),
                "The manifest records an edit of docs/README.md, whose pages docs-index lists instead.");
        }

        expect(
            Launched(ran).SequenceEqual(
            [
                "dotnet run Tools/Tasks.cs migration-check",
                "dotnet run Tools/Tasks.cs docs-index",
                "dotnet run Tools/Tasks.cs build",
                "dotnet run Tools/Tasks.cs -- migration-add AddWidgets --no-build",
                "dotnet run Tools/Tasks.cs -- openapi --no-build",
            ]),
            $"The applied run did not check, build once, then migrate and rewrite the OpenAPI reference from that build, in that order: {string.Join(" | ", Launched(ran))}");
        expect(ran.All(command => command.WorkingDirectory == root || command.WorkingDirectory.StartsWith(root, StringComparison.Ordinal)), "A step ran outside the tree.");
    }

    private static void AllOrNothingCases(string scratch, Action<bool, string> expect)
    {
        (string Path, string Line, string Edit)[] anchors =
        [
            ("Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "    public const string PlatformSchema = \"platform\";", "edit 1"),
            ("Src/Application/A.Application/ApplicationModule.cs", "    private static void AddFeature(this IServiceCollection services, string vertical)", "edit 2"),
            ("Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs", "        AddIdempotencyFeature(services, configuration);", "edit 3"),
            ("Src/Presentation/A.Api/Program.cs", "builder.Services.AddCacheStore();", "edit 4"),
            ("Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs", "        services.AddPurgeExpiredIdempotencyKeys();", "edit 5"),
            ("Src/Application/A.Application.Core/ApplicationCoreModule.cs", "    public static IServiceCollection AddUseCasePipeline(this IServiceCollection services) => services;", "edit 7"),
            ("Src/Presentation/A.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs", "        services.AddMaintenanceJobs(configuration);", "edit 9"),
            ("Src/Presentation/A.Api/Features/Auth/Composition/AuthComposition.cs", "        services.AddAuthModule(configuration);", "edit 10"),
            ("Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs", "internal sealed class ArchitectureTestBackgroundAuditActor : IAuditActor", "edit 11"),
            ("docs/reference/api.md", "## Error codes", "the route section"),
        ];

        foreach (var (path, line, edit) in anchors)
        {
            string root = WithPageTemplate(NewTree(scratch, reaction: false));
            string full = System.IO.Path.Combine(root, path);

            File.WriteAllLines(
                full,
                File.ReadAllLines(full).Where(candidate => candidate != line && !(edit == "edit 4" && candidate == "builder.Services.AddPurgeExpiredIdempotencyKeys();")));

            var before = Snapshot(root);
            var ran = new List<TextEdits.Command>();
            var result = Invoke(root, Recording(ran), "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

            expect(result.Code == 1, $"[{edit}] a missing anchor in {path} was not refused.");
            expect(Same(before, Snapshot(root)), $"[{edit}] a missing anchor in {path} left a file written or edited.");
            expect(ran.Count == 0, $"[{edit}] a missing anchor in {path} still ran {string.Join(", ", ran.Select(command => command.Display))}.");
            expect(
                result.Error.Contains($"error: {edit}", StringComparison.Ordinal) || result.Error.Contains(edit, StringComparison.Ordinal),
                $"[{edit}] the refusal does not name the edit that failed: {result.Error.Split('\n')[0]}");
            expect(result.Error.Contains("Nothing was written", StringComparison.Ordinal), $"[{edit}] the refusal does not say nothing was written.");
            expect(
                result.Output.Contains("Eleven edits", StringComparison.Ordinal) && result.Output.Contains(" 11. ", StringComparison.Ordinal),
                $"[{edit}] the refusal does not print the edits to make by hand.");
        }

        string gone = NewTree(scratch);

        File.Delete(System.IO.Path.Combine(gone, "Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs"));

        var goneBefore = Snapshot(gone);
        var missing = Invoke(gone, "Widgets", "Widget", "--access", "shared");

        expect(
            missing.Code == 1 && Same(goneBefore, Snapshot(gone)) && missing.Error.Contains("PersistenceModule.cs does not exist", StringComparison.Ordinal),
            "A shared file that is gone was not refused cleanly, naming it.");
    }

    private static void IdempotenceCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string program = System.IO.Path.Combine(root, "Src/Presentation/A.Api/Program.cs");

        File.WriteAllText(program, File.ReadAllText(program).Replace(
            "builder.Services.AddPurgeExpiredIdempotencyKeys();",
            "builder.Services.AddWidgets();\n\nbuilder.Services.AddPurgeExpiredIdempotencyKeys();",
            StringComparison.Ordinal));

        var first = Invoke(root, "Widgets", "Widget", "--access", "shared");

        expect(first.Code == 0, $"A feature whose registration a person had written was refused: {first.Error}");
        expect(Occurrences(root, "Src/Presentation/A.Api/Program.cs", "builder.Services.AddWidgets();") == 1, "An edit already in place was made a second time.");
        expect(first.Output.Contains("Already in place", StringComparison.Ordinal), "An edit already in place was not reported as left alone.");
        expect(
            !File.ReadAllText(System.IO.Path.Combine(root, ".feature-manifest/Widgets.json")).Contains("builder.Services.AddWidgets();", StringComparison.Ordinal),
            "An edit this run did not make is recorded in the manifest, so undoing the feature would remove a person's line.");

        var afterFirst = Snapshot(root);

        Put(root, "Src/Infrastructure/A.Infrastructure.Persistence/Migrations/20260101000000_AddWidgets.cs", "// the feature's migration\n");

        var ran = new List<TextEdits.Command>();
        var again = Invoke(root, Recording(ran), "Widgets", "Widget", "--access", "shared");
        var afterAgain = Snapshot(root);

        expect(again.Code == 0, $"Running the same command again failed: {again.Error}");
        expect(
            afterFirst.All(pair => afterAgain.TryGetValue(pair.Key, out string? hash) && hash == pair.Value),
            "Running the same command again changed a file.");
        expect(
            Launched(ran).SequenceEqual(["dotnet run Tools/Tasks.cs docs-index", "dotnet run Tools/Tasks.cs build", "dotnet run Tools/Tasks.cs -- openapi --no-build"]),
            $"Running the same command again did not resume with the index, the build and the OpenAPI reference from it alone: {string.Join(" | ", Launched(ran))}");

        foreach (string line in new[] { "builder.Services.AddWidgets();", "using Acme.Application;" })
        {
            expect(Occurrences(root, "Src/Presentation/A.Api/Program.cs", line) == 1, $"'{line}' is in Program.cs other than once after two runs.");
        }

        ran = [];
        var dry = Invoke(root, Recording(ran), "Widgets", "Widget", "--access", "shared", "--dry-run");

        expect(dry.Code == 0 && ran.Count == 0 && Same(afterAgain, Snapshot(root)), "A dry run over a feature already written ran or wrote something.");
    }

    private static void DryRunCases(string scratch, Action<bool, string> expect)
    {
        string root = WithPageTemplate(NewTree(scratch, reaction: false));
        var before = Snapshot(root);
        var ran = new List<TextEdits.Command>();
        var result = Invoke(root, Recording(ran), "Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--dry-run");

        expect(result.Code == 0, $"A dry run was refused: {result.Error}");
        expect(Same(before, Snapshot(root)), "A dry run wrote or edited a file.");
        expect(ran.Count == 0, $"A dry run ran {string.Join(", ", ran.Select(command => command.Display))}.");
        expect(
            result.Output.Contains("Eleven edits", StringComparison.Ordinal)
                && result.Output.Contains("  1. Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", StringComparison.Ordinal)
                && result.Output.Contains("dotnet run Tools/Tasks.cs -- migration-add AddWidgets --no-build", StringComparison.Ordinal)
                && result.Output.Contains("nothing was written and nothing was run", StringComparison.Ordinal),
            "A dry run did not print the edits and the steps a run makes.");
    }

    private static void PreconditionCases(string scratch, Action<bool, string> expect)
    {
        string pending = NewTree(scratch);
        var before = Snapshot(pending);
        var ran = new List<TextEdits.Command>();
        var refused = Invoke(pending, Recording(ran, failing: "dotnet run Tools/Tasks.cs migration-check"), "Widgets", "Widget", "--access", "shared");

        expect(refused.Code == 1 && Same(before, Snapshot(pending)), "A model change no migration holds did not stop the run before it wrote.");
        expect(
            ran.Select(command => command.Display).SequenceEqual(["dotnet run Tools/Tasks.cs migration-check"]),
            "A run refused for a pending model change ran more than the check.");
        expect(
            refused.Error.Contains("migration-add <Name>", StringComparison.Ordinal) && refused.Error.Contains("Nothing was written.", StringComparison.Ordinal),
            "The refusal for a pending model change does not say what to do.");

        string unmigrated = NewTree(scratch);

        Directory.Delete(System.IO.Path.Combine(unmigrated, "Src/Infrastructure/A.Infrastructure.Persistence/Migrations"), recursive: true);

        var bare = Snapshot(unmigrated);
        ran = [];
        var none = Invoke(unmigrated, Recording(ran), "Widgets", "Widget", "--access", "shared");

        expect(none.Code == 1 && Same(bare, Snapshot(unmigrated)) && ran.Count == 0, "A context without its initial migration did not stop the run before it wrote or ran.");
        expect(none.Error.Contains("has no initial migration", StringComparison.Ordinal) && none.Error.Contains("bootstrap", StringComparison.Ordinal), "The refusal for a missing initial migration does not say what to do.");

        string identity = NewTree(scratch);

        Directory.CreateDirectory(System.IO.Path.Combine(identity, "Src/Infrastructure/A.Infrastructure.Auth"));

        var withAuth = Invoke(identity, "Widgets", "Widget", "--access", "shared");

        expect(withAuth.Code == 1 && withAuth.Error.Contains("A.Infrastructure.Auth has no initial migration", StringComparison.Ordinal), "The identity context's missing initial migration was not refused.");
    }

    private static void StepCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var ran = new List<TextEdits.Command>();
        var result = Invoke(root, Recording(ran, failing: "dotnet run Tools/Tasks.cs -- migration-add AddWidgets --no-build"), "Widgets", "Widget", "--access", "shared");

        expect(result.Code == 1, "A failed step did not fail the command.");
        expect(
            ran.Select(command => command.Display).SequenceEqual(
                ["dotnet run Tools/Tasks.cs migration-check", "dotnet run Tools/Tasks.cs docs-index", "dotnet run Tools/Tasks.cs build", "dotnet run Tools/Tasks.cs -- migration-add AddWidgets --no-build"]),
            $"The steps did not stop at the one that failed: {string.Join(" | ", ran.Select(command => command.Display))}");
        expect(
            result.Error.Contains("then `dotnet run Tools/Tasks.cs build`", StringComparison.Ordinal)
                && result.Error.Contains("  dotnet run Tools/Tasks.cs -- migration-add AddWidgets --no-build\n  dotnet run Tools/Tasks.cs -- openapi --no-build\n", StringComparison.Ordinal)
                && result.Error.Contains("run `dotnet run Tools/Tasks.cs build` before it", StringComparison.Ordinal),
            $"A failed step does not say what is done and the commands that remain: {result.Error}");
        expect(File.Exists(System.IO.Path.Combine(root, ".feature-manifest/Widgets.json")), "A failed step undid the files written, which are consistent without it.");
    }

    private static void SubsetCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        (string Model, string Access, string Concurrency, string? Deletion)[] shapes =
        [
            ("record", "owner", "etag", "delete"),
            ("record", "owner", "none", "anonymise"),
            ("record", "shared", "none", null),
            ("aggregate", "owner", "etag", "anonymise"),
            ("aggregate", "shared", "etag", null),
        ];

        int sequence = 0;

        for (int mask = 1; mask < 1 << _operationNames.Length; mask++)
        {
            string[] operations = [.. _operationNames.Where((_, index) => (mask & (1 << index)) != 0)];

            foreach (var (model, access, concurrency, deletion) in shapes)
            {
                sequence++;

                string features = $"Items{sequence}";
                string type = $"Item{sequence}";
                string label = $"{model}/{access}/{concurrency}/{deletion ?? "none"}/{string.Join(',', operations)}";

                bool valid = Contradictions(operations, model, concurrency).Count == 0;
                var before = Snapshot(root);

                var arguments = new List<string>
                {
                    features,
                    type,
                    "--access", access,
                    "--model", model,
                    "--concurrency", concurrency,
                    "--operations", string.Join(',', operations),
                };

                if (deletion is not null)
                {
                    arguments.InsertRange(2, ["--deletion", deletion]);
                }

                var result = Invoke(root, [.. arguments]);

                var after = Snapshot(root);

                if (!valid)
                {
                    expect(result.Code == 1 && Same(before, after), $"[{label}] is contradictory and was not refused cleanly.");

                    continue;
                }

                expect(result.Code == 0, $"[{label}] was refused: {result.Error.Split('\n')[0]}");

                if (result.Code != 0)
                {
                    continue;
                }

                string[] created = [.. after.Keys.Except(before.Keys, StringComparer.Ordinal).Where(path => path.EndsWith(".cs", StringComparison.Ordinal))];

                expect(
                    before.All(pair => after[pair.Key] == pair.Value || _composingFiles.Contains(pair.Key, StringComparer.Ordinal)),
                    $"[{label}] edited a file that already existed and composes no feature: {string.Join(", ", before.Where(pair => after[pair.Key] != pair.Value && !_composingFiles.Contains(pair.Key, StringComparer.Ordinal)).Select(pair => pair.Key))}");

                SubsetExpectations(root, label, features, type, model, access, concurrency, deletion, operations, created, expect);
            }
        }
    }

    private static readonly string[] _composingFiles =
    [
        "Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs",
        "Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs",
        "Src/Application/A.Application/ApplicationModule.cs",
        "Src/Presentation/A.Api/Program.cs",
        "Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs",
        "Tests/Application/A.Application.UnitTests/ApplicationModuleTests.cs",
        "docs/reference/api.md",
    ];

    private static void SubsetExpectations(
        string root,
        string label,
        string features,
        string type,
        string model,
        string access,
        string concurrency,
        string? deletion,
        string[] operations,
        string[] created,
        Action<bool, string> expect)
    {
        bool Has(string operation) => operations.Contains(operation, StringComparer.Ordinal);

        bool record = model == "record";
        bool owner = access == "owner";
        bool etag = concurrency == "etag";
        bool writes = Has("create") || Has("update") || Has("delete");
        bool changes = Has("update") || Has("delete");

        bool Wrote(string suffix) => created.Any(path => path.EndsWith("/" + suffix, StringComparison.Ordinal));

        void Present(bool should, string suffix)
        {
            expect(Wrote(suffix) == should, $"[{label}] {suffix} should {(should ? "exist" : "be absent")}.");
        }

        string use(string operation, string name) => $"UseCases/{operation}/{name}/{name}.cs";

        string verb = deletion == "anonymise" ? "Anonymise" : "Delete";

        Present(record || changes, $"Services/I{type}Service.cs");
        Present(record && (writes || owner), $"Ports/{type}Store/I{type}Store.cs");
        Present(record && changes, $"Ports/{type}Store/{type}ChangeStatus.cs");
        Present(record && (writes || owner), $"Stores/{type}Store.cs");
        Present(Has("get") || Has("list") || owner || (record && (Has("create") || Has("update"))), $"Ports/{type}Queries/I{type}Queries.cs");
        Present(owner, $"Services/Consumers/{verb}{type}OnAccountDeletedConsumer.cs");
        Present(Has("list"), $"Model/Dtos/{type}SummaryDto.cs");
        Present(Has("list"), $"Model/Policies/{type}CollectionPolicy.cs");
        Present(Has("list"), $"Queries/{type}SortMap.cs");
        Present(Has("list"), $"Contracts/Requests/Get{features}Request.cs");
        Present(Has("get") || Has("create") || Has("update"), $"Model/Dtos/{type}Dto.cs");
        Present(Has("get") || Has("create") || Has("update"), $"Contracts/Responses/{type}Response.cs");
        Present(Has("create"), $"Contracts/Requests/Create{type}Request.cs");
        Present(Has("update"), $"Contracts/Requests/Update{type}Request.cs");
        Present(Has("get") || Has("create") || Has("update") || Has("list"), $"Contracts/Mapping/{type}ResponseMapping.cs");
        Present(true, $"Controllers/{features}Controller.cs");
        Present(true, $"Models/{type}Record.cs");
        Present(true, $"Configurations/{type}RecordConfiguration.cs");

        Present(!record, $"Model/Entities/{type}.cs");
        Present(!record, $"Mapping/{type}Mapper.cs");
        Present(!record, $"Tracking/{type}Tracker.cs");
        Present(!record, $"Repositories/{type}Repository.cs");
        Present(record || changes, $"Services/{type}Service.cs");
        Present(!record && Has("get"), use("Queries", $"Get{type}"));
        Present(!record && Has("list"), use("Queries", $"Get{features}"));
        Present(!record && Has("create"), use("Commands", $"Create{type}"));
        Present(!record && Has("update"), use("Commands", $"Update{type}"));
        Present(!record && Has("delete"), use("Commands", $"Delete{type}"));

        string Text(string suffix) =>
            created.Where(path => path.EndsWith("/" + suffix, StringComparison.Ordinal))
                .Select(path => File.ReadAllText(System.IO.Path.Combine(root, path)))
                .FirstOrDefault() ?? string.Empty;

        string controller = Text($"Controllers/{features}Controller.cs");

        void Contains(string text, string needle, bool should, string where)
        {
            expect(text.Contains(needle, StringComparison.Ordinal) == should, $"[{label}] {where} should {(should ? "contain" : "not contain")} '{needle}'.");
        }

        Contains(controller, "[HttpGet]", Has("list"), "the controller");
        Contains(controller, "[HttpGet(\"{", Has("get"), "the controller");
        Contains(controller, "[HttpHead(", Has("get"), "the controller");
        Contains(controller, "[HttpPost]", Has("create"), "the controller");
        Contains(controller, "[HttpPut(", Has("update"), "the controller");
        Contains(controller, "[HttpDelete(", Has("delete"), "the controller");
        Contains(controller, "ReadPrecondition", etag && changes, "the controller");
        Contains(controller, "[Idempotent]", Has("create"), "the controller");

        if (record)
        {
            string service = Text($"Services/I{type}Service.cs");
            string implementation = Text($"Services/{type}Service.cs");

            foreach (var (operation, method) in new[] { ("list", " ListAsync("), ("get", " GetAsync("), ("create", " CreateAsync("), ("update", " UpdateAsync("), ("delete", " DeleteAsync(") })
            {
                Contains(service, method, Has(operation), "the service interface");
                Contains(implementation, method, Has(operation), "the service");
            }

            string store = Text($"Ports/{type}Store/I{type}Store.cs");

            if (writes || owner)
            {
                Contains(store, " Add(", Has("create"), "the store port");
                Contains(store, "StageReplacementAsync(", Has("update"), "the store port");
                Contains(store, "StageRemovalAsync(", Has("delete"), "the store port");
                Contains(store, "ListDataOwnerIdsAsync", false, "the store port");
                Contains(store, deletion == "anonymise" ? "StageAnonymisationForOwnerAsync" : "StageRemovalForOwnerAsync", owner, "the store port");
            }
        }
        else
        {
            string repository = Text($"Repositories/I{type}Repository.cs");

            Contains(repository, "Task<", changes || owner, "the repository port");
            Contains(repository, " Add(", Has("create"), "the repository port");
            Contains(repository, " Remove(", Has("delete") || owner, "the repository port");
            Contains(repository, "GetForOwnerAsync", owner, "the repository port");
            Contains(repository, "GetAsync(UserId ownerId, Guid id,", owner && changes, "the repository port");
            Contains(repository, "GetAsync(Guid id,", !owner && changes, "the repository port");
            Contains(Text($"Repositories/{type}Repository.cs"), "candidate.OwnerId == ownerId.Value", owner && changes, "the repository");
            Contains(Text($"Services/{type}Service.cs"), "repository.GetAsync(userId.Value, ", owner && changes, "the access service");

            string aggregateText = Text($"Model/Entities/{type}.cs");
            int firstAssignment = aggregateText.IndexOf("Name = valid", StringComparison.Ordinal);
            int lastValidation = aggregateText.IndexOf("string validDescription = RequireDescription(", StringComparison.Ordinal);

            expect(
                !Has("update") || (lastValidation >= 0 && firstAssignment > lastValidation),
                $"[{label}] the aggregate's Update assigns before it has validated every input.");
            Contains(Text($"Commands/Update{type}/Update{type}.cs"), "DomainGuard.Try(() => ", Has("update"), "the update handler");
            Contains(Text($"Commands/Create{type}/Create{type}.cs"), "DomainGuard.Try(() => ", Has("create"), "the create handler");
        }

        string consumer = Text($"Services/Consumers/{verb}{type}OnAccountDeletedConsumer.cs");
        string queriesPort = Text($"Ports/{type}Queries/I{type}Queries.cs");

        Contains(consumer, "IUserAccountDirectory", owner, "the consumer");
        Contains(consumer, "accounts.ExistsAsync(owner", owner, "the consumer");
        Contains(consumer, "ListDataOwnerIdsAsync", owner, "the consumer");
        Contains(consumer, deletion == "anonymise" ? "Anonymise()" : "Remove(aggregate)", owner && !record, "the consumer");
        Contains(queriesPort, "ListDataOwnerIdsAsync", owner, "the queries port");

        if (owner && !record && deletion == "anonymise")
        {
            Contains(Text($"Model/Entities/{type}.cs"), "public void Anonymise()", true, "the aggregate");
        }

        string stored = Text($"Models/{type}Record.cs");

        Contains(stored, "uint Version", etag || !record, "the persistence record");
        Contains(stored, "Guid OwnerId", owner, "the persistence record");

        foreach (string path in created)
        {
            string text = File.ReadAllText(System.IO.Path.Combine(root, path));

            expect(!text.Contains("OwnerId", StringComparison.OrdinalIgnoreCase) || owner, $"[{label}] {path} names an owner in a shared feature.");
            expect(!text.Contains('%', StringComparison.Ordinal), $"[{label}] {path}: a '%' survived substitution.");

            foreach (string problem in FileProblems(root, path))
            {
                expect(false, $"[{label}] {problem}");
            }
        }

        foreach (string path in created.Where(path => !path.StartsWith("Tests/", StringComparison.Ordinal)))
        {
            string text = File.ReadAllText(System.IO.Path.Combine(root, path));

            expect(
                !Regex.IsMatch(text, @"Features\.(?!" + features + @"\b)[A-Za-z]+", RegexOptions.None, TimeSpan.FromSeconds(2)),
                $"[{label}] {path} reaches into another feature.");
        }
    }

    private static void NoOverwriteCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        var first = Invoke(root, "Gadgets", "Gadget", "--access", "owner", "--deletion", "delete");
        var afterFirst = Snapshot(root);
        var second = Invoke(root, "Gadgets", "Gadget", "--access", "owner", "--deletion", "delete");

        expect(first.Code == 0, "The first generation was refused.");
        expect(second.Code == 0 && second.Output.Contains("nothing is written again", StringComparison.Ordinal), "Generating the same feature twice did not say it had nothing to write.");
        expect(Same(afterFirst, Snapshot(root)), "Generating the same feature twice changed the tree.");

        var otherwise = Invoke(root, "Gadgets", "Gadget", "--access", "shared");

        expect(otherwise.Code == 1 && otherwise.Error.Contains("already exists", StringComparison.Ordinal), "Generating the same feature with other options was not refused.");
        expect(Same(afterFirst, Snapshot(root)), "The refused generation with other options changed the tree.");

        string other = NewTree(scratch);
        string inTheWay = System.IO.Path.Combine(other, "Src/Application/A.Application/Features/Widgets/Model/Errors/WidgetErrors.cs");

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(inTheWay)!);
        File.WriteAllText(inTheWay, "// somebody's own test\n");

        var before = Snapshot(other);
        var blocked = Invoke(other, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(blocked.Code == 1 && blocked.Error.Contains("none is ever overwritten", StringComparison.Ordinal), "A file in the way was not refused.");
        expect(Same(before, Snapshot(other)), "A refusal over an existing file changed the tree.");
        expect(File.ReadAllText(inTheWay) == "// somebody's own test\n", "An existing file was overwritten.");

        string named = NewTree(scratch);

        File.WriteAllText(
            System.IO.Path.Combine(named, "Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs"),
            "public const string WidgetsSchema = \"widgets\";\n");

        var snapshot = Snapshot(named);
        var collision = Invoke(named, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(collision.Code == 1 && collision.Error.Contains("already declares", StringComparison.Ordinal), "A name the context already declares was accepted.");
        expect(Same(snapshot, Snapshot(named)), "A name collision changed the tree.");
    }

    private static void ManifestCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var before = Snapshot(root);

        var result = Invoke(
            root, "Notes", "Note", "--access", "owner", "--deletion", "delete", "--concurrency", "etag", "--operations", "get,list,update");

        expect(result.Code == 0, $"The manifest case was refused: {result.Error}");

        string path = System.IO.Path.Combine(root, ".feature-manifest", "Notes.json");

        expect(File.Exists(path), "No manifest was written at .feature-manifest/Notes.json.");

        if (!File.Exists(path))
        {
            return;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var manifest = document.RootElement;
        var options = manifest.GetProperty("options");

        expect(manifest.GetProperty("schemaVersion").GetInt32() == 2, "The manifest does not have schema version 2, the one that records the edits.");
        expect(manifest.GetProperty("feature").GetString() == "Notes" && manifest.GetProperty("type").GetString() == "Note", "The manifest names the wrong feature.");
        expect(options.GetProperty("access").GetString() == "owner", "The manifest records the wrong access.");
        expect(options.GetProperty("concurrency").GetString() == "etag", "The manifest records the wrong concurrency.");
        expect(options.GetProperty("deletion").GetString() == "delete", "The manifest records the wrong deletion.");
        expect(options.GetProperty("model").GetString() == "record", "The manifest records the wrong model.");
        expect(
            string.Join(',', options.GetProperty("operations").EnumerateArray().Select(element => element.GetString())) == "get,list,update",
            "The manifest records the wrong operations.");

        var listed = manifest.GetProperty("files").EnumerateArray()
            .ToDictionary(file => file.GetProperty("path").GetString()!, file => file.GetProperty("sha256").GetString()!, StringComparer.Ordinal);

        var after = Snapshot(root);
        string[] created = [.. after.Keys.Except(before.Keys, StringComparer.Ordinal).Where(key => after[key] != "<directory>" && key != ".feature-manifest/Notes.json")];

        expect(created.Length > 0 && created.OrderBy(key => key, StringComparer.Ordinal).SequenceEqual(listed.Keys.OrderBy(key => key, StringComparer.Ordinal)),
            "The manifest does not list exactly the files that were created.");

        foreach (string key in created)
        {
            expect(listed.TryGetValue(key, out string? hash) && hash == after[key], $"The manifest's hash of {key} is not the hash of the file.");
        }

        expect(!listed.ContainsKey(".feature-manifest/Notes.json"), "The manifest lists itself.");
    }

    private static void RollbackCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        File.WriteAllText(System.IO.Path.Combine(root, "Src/Presentation/A.Api/Features"), "not a folder\n");

        var before = Snapshot(root);
        var result = Invoke(root, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(result.Code == 1 && result.Error.Contains("nothing was left behind", StringComparison.Ordinal), "A write that fails half way was not reported as rolled back.");
        expect(Same(before, Snapshot(root)), "A write that failed half way left files or folders behind.");

        string edited = NewTree(scratch);
        const string program = "Src/Presentation/A.Api/Program.cs";
        string full = System.IO.Path.Combine(edited, program);
        byte[] original = File.ReadAllBytes(full);
        var untouched = Snapshot(edited);
        bool restored = false;

        try
        {
            TextEdits.Commit(edited, [("Src/written.cs", [1])], [(program, original, [2]), ("Src", [], [3])]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            restored = Same(untouched, Snapshot(edited));
        }

        expect(restored, "An edit that could not be written did not put back the files edited before it, and remove the files written and the staged copy.");

        string tight = NewTree(scratch);
        string kept = System.IO.Path.Combine(tight, program);
        string taken = kept + ".~1";
        string spare = TextEdits.Spare(kept);

        File.WriteAllText(taken, "left by an interrupted run\n");

        string second = TextEdits.Spare(kept);
        var occupied = Snapshot(tight);
        bool setBack = false;

        try
        {
            TextEdits.Commit(tight, [], [], [program, "Src/absent.cs"]);
        }
        catch (IOException)
        {
            setBack = Same(occupied, Snapshot(tight));
        }

        expect(spare.Length - kept.Length <= 3, $"A temporary name costs {spare.Length - kept.Length} characters on a path Windows already limits to 260.");
        expect(second != taken && !File.Exists(second), "A temporary name reused one a run left behind.");
        expect(setBack, "A deletion that failed after another did not set the first file back, or touched a temporary name it found.");
    }

    private static void ReactionCases(string scratch, Action<bool, string> expect)
    {
        string shipped = NewTree(scratch);

        var present = Invoke(shipped, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(present.Code == 0, $"An owner feature into a tree with the reaction was refused: {present.Error.Split('\n')[0]}");
        expect(
            !File.Exists(System.IO.Path.Combine(shipped, "Src/Application/A.Application.Core/Common/Ports/IUserAccountDirectory.cs")),
            "A tree that ships the reaction had its account directory re-emitted.");
        expect(present.Output.Contains("Six edits", StringComparison.Ordinal), "A tree that ships the reaction was told to edit more than six places.");
        expect(!present.Output.Contains("AddDeletedAccountSweep", StringComparison.Ordinal), "A tree that ships the reaction was told to register its sweep again.");
        expect(
            !File.ReadAllText(System.IO.Path.Combine(shipped, "Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs")).Contains("AddDeletedAccountSweep", StringComparison.Ordinal),
            "A tree that ships the reaction had its sweep registered again.");

        string bare = NewTree(scratch, reaction: false);
        var bareTree = Snapshot(bare);

        var first = Invoke(bare, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(first.Code == 0, $"The first owner feature into a bare tree was refused: {first.Error.Split('\n')[0]}");

        string[] reaction =
        [
            "Src/Application/A.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs",
            "Src/Application/A.Application.Core/Common/Ports/IUserAccountDirectory.cs",
            "Src/Application/A.Application.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountData.cs",
            "Src/Presentation/A.Api/Features/BackgroundJobs/Jobs/DeletedAccountSweepJobsExtensions.cs",
            "Src/Presentation/A.Api/Features/BackgroundJobs/Jobs/DeletedAccountSweepOptions.cs",
            "Src/Presentation/A.Api/Features/Auth/Composition/AccountUserAccountDirectory.cs",
        ];

        foreach (string path in reaction)
        {
            expect(File.Exists(System.IO.Path.Combine(bare, path)), $"{path} was not written with the first owner feature.");

            foreach (string problem in FileProblems(bare, path))
            {
                expect(false, $"[bare] {problem}");
            }
        }

        expect(first.Output.Contains("Eleven edits", StringComparison.Ordinal), "The reaction's registrations were not printed as edits.");
        expect(first.Output.Contains("AddDeletedAccountSweep", StringComparison.Ordinal), "The sweep registration was not printed.");

        (string Path, string Line, int Count)[] wiring =
        [
            ("Src/Application/A.Application.Core/ApplicationCoreModule.cs", "public static IServiceCollection AddDeletedAccountSweep(this IServiceCollection services)", 1),
            ("Src/Presentation/A.Api/Program.cs", "builder.Services.AddDeletedAccountSweep();", 1),
            ("Src/Presentation/A.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs", "services.AddDeletedAccountSweepJobs(configuration);", 1),
            ("Src/Presentation/A.Api/Features/Auth/Composition/AuthComposition.cs", "services.AddScoped<IUserAccountDirectory, AccountUserAccountDirectory>();", 1),
            ("Src/Presentation/A.Api/Features/Auth/Composition/AuthComposition.cs", "using Acme.Application.Core.Common.Ports;", 1),
            ("Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs", "services.AddDeletedAccountSweep();", 2),
            ("Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs", "services.AddScoped<IUserAccountDirectory, ArchitectureTestUserAccountDirectory>();", 2),
            ("Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs", "internal sealed class ArchitectureTestUserAccountDirectory : IUserAccountDirectory", 1),
            ("Src/Application/A.Application/ApplicationModule.cs", "services.AddDomainEventConsumer<UserAccountDeleted, DeleteWidgetOnAccountDeletedConsumer>();", 1),
            ("Src/Application/A.Application/ApplicationModule.cs", "using Acme.Application.Core.Common.Events;", 1),
        ];

        foreach (var (path, line, count) in wiring)
        {
            expect(Occurrences(bare, path, line) == count, $"The first owner feature did not leave '{line}' {count} time(s) in {path}.");
        }

        int eleventh = first.Output.IndexOf(" 11. ", StringComparison.Ordinal);
        string hostEdit = eleventh < 0 ? string.Empty : first.Output[eleventh..];

        expect(
            first.Output.Contains(" 11. Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs: services.AddDeletedAccountSweep();", StringComparison.Ordinal),
            "The architecture tests' composition was not printed as the eleventh edit.");
        string[] hostLines =
        [
            "AddHostSuppliedAdapters",
            "AddBackgroundOnlyAdapters",
            "services.AddScoped<IUserAccountDirectory, ArchitectureTestUserAccountDirectory>();",
            "internal sealed class ArchitectureTestUserAccountDirectory : IUserAccountDirectory",
        ];

        expect(
            hostLines.All(text => hostEdit.Contains(text, StringComparison.Ordinal)),
            "The eleventh edit does not name both adapter methods, the double's registration and its declaration.");
        expect(first.Output.Contains(_removalRefusal, StringComparison.Ordinal), "The first owner feature did not say that remove-feature holds it back while another one uses the shared files.");
        ExpectRemovable(bare, bareTree, "Widgets", 6, expect);

        var second = Invoke(bare, "Sprockets", "Sprocket", "--access", "owner", "--deletion", "anonymise");

        expect(second.Code == 0, $"The second owner feature was refused: {second.Error.Split('\n')[0]}");
        expect(
            !File.ReadAllText(System.IO.Path.Combine(bare, "Src/Application/A.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs"))
                .Contains("Sprockets", StringComparison.Ordinal),
            "The second owner feature rewrote a file the first one had created.");
        expect(second.Output.Contains("Six edits", StringComparison.Ordinal), "The second owner feature was told to edit more than six places.");
        expect(!second.Output.Contains(_removalRefusal, StringComparison.Ordinal), "A feature that brought no shared file was told that remove-feature holds it back.");

        string other = NewTree(scratch, reaction: false);

        var shared = Invoke(other, "Widgets", "Widget", "--access", "shared");

        expect(shared.Code == 0, $"A shared feature into a bare tree was refused: {shared.Error.Split('\n')[0]}");
        expect(!File.Exists(System.IO.Path.Combine(other, "Src/Application/A.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs")), "A shared feature brought the reaction with it.");
        expect(shared.Output.Contains("Six edits", StringComparison.Ordinal), "A shared feature was told to edit more than six places.");

        string external = NewTree(scratch, reaction: false);

        Directory.Delete(System.IO.Path.Combine(external, "Src/Presentation/A.Api/Features/Auth"), recursive: true);
        Put(external, "Src/Presentation/A.Api/Features/ExternalIdentity/Composition/ExternalAuthenticationComposition.cs", "        services.AddExternalIdentityPersistence(configuration);\n");

        var externalTree = Snapshot(external);
        var mapped = Invoke(external, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        ExpectRemovable(external, externalTree, "Widgets", 6, expect);

        expect(mapped.Code == 0, $"An owner feature into an externally authenticated bare tree was refused: {mapped.Error.Split('\n')[0]}");
        expect(
            File.Exists(System.IO.Path.Combine(external, "Src/Presentation/A.Api/Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory.cs")),
            "The external-identity account directory was not written.");
        expect(mapped.Output.Contains("ExternalAuthenticationComposition", StringComparison.Ordinal), "The external composition was not named for the registration.");

        string presented = NewTree(scratch, reaction: false);
        string portal = System.IO.Path.Combine(presented, "Src/Presentation/A.Portal");

        Put(presented, "Src/Presentation/A.Portal/Program.cs", "builder.Services.AddCookieSessions(builder.Configuration);\n\nbuilder.Services.AddCacheStore();\n");
        Put(presented, "Src/Presentation/A.Portal/Features/Auth/Composition/CookieSessionComposition.cs", "        services.AddAuthModule(configuration);\n");

        var presentedTree = Snapshot(presented);
        var hosted = Invoke(presented, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");
        string portalDirectory = System.IO.Path.Combine(portal, "Features/Auth/Composition/AccountUserAccountDirectory.cs");

        expect(hosted.Code == 0, $"An owner feature into a tree with a presentation host was refused: {hosted.Error.Split('\n')[0]}");
        expect(
            File.Exists(portalDirectory)
                && File.ReadAllText(portalDirectory).Contains("namespace Acme.Portal.Features.Auth.Composition;", StringComparison.Ordinal),
            "The presentation host did not get an account directory in its own namespace.");
        expect(
            hosted.Output.Contains("Src/Presentation/A.Portal/Program.cs the same", StringComparison.Ordinal),
            "The feature's registration was not printed for the presentation host, which composes the features itself.");
        expect(
            hosted.Output.Contains("Src/Presentation/A.Portal/Features/Auth/Composition/CookieSessionComposition.cs", StringComparison.Ordinal),
            "The directory's registration was not printed for the presentation host's composition.");
        expect(
            Occurrences(presented, "Src/Presentation/A.Portal/Program.cs", "builder.Services.AddWidgets();") == 1
                && Occurrences(presented, "Src/Presentation/A.Portal/Features/Auth/Composition/CookieSessionComposition.cs", "services.AddScoped<IUserAccountDirectory, AccountUserAccountDirectory>();") == 1,
            "The presentation host, which composes the features itself, was not given the feature's registration and the directory's.");
        ExpectRemovable(presented, presentedTree, "Widgets", 7, expect);

        string desktop = System.IO.Path.Combine(presented, "Src/Presentation/A.Desk");

        Directory.CreateDirectory(desktop);
        File.WriteAllText(System.IO.Path.Combine(desktop, "Program.cs"), "return 0;\n");
        File.WriteAllText(
            System.IO.Path.Combine(desktop, "Acme.Desk.csproj"),
            "<Project><ItemGroup><ProjectReference Include=\"..\\A.Presentation.Client\\Acme.Presentation.Client.csproj\" /></ItemGroup></Project>\n");

        var withClient = Invoke(presented, "Gadgets", "Gadget", "--access", "owner", "--deletion", "delete");

        expect(withClient.Code == 0, $"An owner feature into a tree with a client presentation was refused: {withClient.Error.Split('\n')[0]}");
        expect(
            !withClient.Output.Contains("A.Desk", StringComparison.Ordinal)
                && !Directory.Exists(System.IO.Path.Combine(desktop, "Features")),
            "A presentation that calls the API through the .NET client was given the feature's registration or an adapter, as if it composed features.");

        string working = NewTree(scratch, reaction: false);
        string worker = System.IO.Path.Combine(working, "Src/Presentation/A.Worker");

        Directory.Delete(System.IO.Path.Combine(working, "Src/Presentation/A.Api/Features/BackgroundJobs"), recursive: true);
        Put(working, "Src/Presentation/A.Worker/Program.cs", _hostProgram);
        Put(working, "Src/Presentation/A.Worker/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs", "        services.AddMaintenanceJobs(configuration);\n");
        Put(working, "Src/Presentation/A.Worker/Features/Auth/Composition/WorkerAuthComposition.cs", "        services.AddAuthModule(configuration);\n");

        var workingTree = Snapshot(working);
        var swept = Invoke(working, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");
        string sweepJobs = System.IO.Path.Combine(worker, "Features/BackgroundJobs/Jobs/DeletedAccountSweepJobsExtensions.cs");

        expect(swept.Code == 0, $"An owner feature into a tree whose jobs run in a worker was refused: {swept.Error.Split('\n')[0]}");
        expect(
            File.Exists(sweepJobs)
                && File.ReadAllText(sweepJobs).Contains("namespace Acme.Worker.Features.BackgroundJobs.Jobs;", StringComparison.Ordinal)
                && File.ReadAllText(sweepJobs).Contains("using Acme.Worker.Features.Maintenance.Jobs;", StringComparison.Ordinal),
            "The deleted-account sweep's jobs were not written into the host that runs the jobs, in its namespace.");
        expect(
            !Directory.Exists(System.IO.Path.Combine(working, "Src/Presentation/A.Api/Features/BackgroundJobs")),
            "The deleted-account sweep's jobs were written into the API, which runs no job in this tree.");
        expect(
            swept.Output.Contains("  8. Src/Presentation/A.Worker/Program.cs: builder.Services.AddDeletedAccountSweep();", StringComparison.Ordinal)
                && swept.Output.Contains("  9. Src/Presentation/A.Worker/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs:", StringComparison.Ordinal),
            "The sweep's registrations were not printed for the host that runs the jobs.");
        expect(
            swept.Output.Contains("Src/Presentation/A.Worker/Program.cs the same", StringComparison.Ordinal)
                && swept.Output.Contains("Src/Presentation/A.Worker/Features/Auth/Composition/WorkerAuthComposition.cs", StringComparison.Ordinal),
            "The worker, which composes the features its passes call, was not given the feature's registration and the directory's.");
        ExpectRemovable(working, workingTree, "Widgets", 7, expect);

        string publicApi = NewTree(scratch, reaction: false);

        Directory.Delete(System.IO.Path.Combine(publicApi, "Src/Presentation/A.Api/Features/Auth"), recursive: true);

        var before = Snapshot(publicApi);
        var refused = Invoke(publicApi, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(refused.Code == 1, "An owner feature into a tree with no accounts was not refused.");
        expect(refused.Error.Contains("neither the local accounts", StringComparison.Ordinal), "The refusal did not name the missing account directory.");
        expect(Same(before, Snapshot(publicApi)), "The refused tree was written to.");
    }

    private static string WithPageTemplate(string root)
    {
        string shipped = System.IO.Path.Combine(Repository.Root, _pageTemplate);
        string copy = System.IO.Path.Combine(root, _pageTemplate);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(copy)!);
        File.Copy(shipped, copy);

        return root;
    }

    private static string WithoutTheApi(string root, params string[] hosts)
    {
        Directory.Delete(System.IO.Path.Combine(root, "Src/Presentation/A.Api"), recursive: true);
        Directory.Delete(System.IO.Path.Combine(root, "Tests/Presentation/A.Api.UnitTests"), recursive: true);
        File.Delete(System.IO.Path.Combine(root, "docs/reference/api.md"));

        foreach (string host in hosts)
        {
            Put(root, $"Src/Presentation/{host}/Program.cs", _hostProgram);
        }

        return root;
    }

    private static void WithoutTheApiCases(string scratch, Action<bool, string> expect)
    {
        var apiWords = new Regex(@"\bApi\b|api\.md|OpenAPI|HTTP|If-Match|ETag|Controller|/api/v1|Tests/Integration", RegexOptions.None, TimeSpan.FromSeconds(2));

        string[][] combinations =
        [
            ["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--concurrency", "etag"],
            ["Tags", "Tag", "--access", "shared", "--concurrency", "none"],
            ["Invoices", "Invoice", "--access", "owner", "--deletion", "anonymise", "--model", "aggregate", "--concurrency", "etag"],
        ];

        foreach (string[] combination in combinations)
        {
            string label = string.Join(' ', combination);
            string root = WithPageTemplate(WithoutTheApi(NewTree(scratch), "A.Portal", "A.Worker"));
            var before = Snapshot(root);
            var result = Invoke(root, combination);

            expect(result.Code == 0, $"[{label}] without the API was refused: {result.Error.Split('\n')[0]}");

            var after = Snapshot(root);
            string[] written = [.. after.Keys.Except(before.Keys, StringComparer.Ordinal).Where(key => after[key] != "<directory>")];

            expect(
                written.Any(path => path.StartsWith($"Src/Application/A.Application/Features/{combination[0]}/", StringComparison.Ordinal))
                    && written.Any(path => path.StartsWith($"Src/Infrastructure/A.Infrastructure.Persistence/Features/{combination[0]}/", StringComparison.Ordinal))
                    && written.Any(path => path.StartsWith("Tests/", StringComparison.Ordinal))
                    && written.Contains(FeatureManifest.PathOf(combination[0]), StringComparer.Ordinal)
                    && written.Contains(PagePath(combination[0]), StringComparer.Ordinal),
                $"[{label}] without the API did not write its application, persistence, tests, manifest and page.");
            expect(
                !written.Any(path => path.Contains("/A.Api/", StringComparison.Ordinal) || path.Contains("/Contracts/", StringComparison.Ordinal)),
                $"[{label}] without the API wrote an HTTP layer: {string.Join(", ", written.Where(path => path.Contains("/A.Api/", StringComparison.Ordinal)))}");
            expect(!apiWords.IsMatch(result.Output), $"[{label}] without the API printed an edit or a row about it: {apiWords.Match(result.Output).Value}");
            expect(
                result.Output.Contains($"  4. Src/Presentation/A.Portal/Program.cs: builder.Services.Add{combination[0]}();", StringComparison.Ordinal)
                    && result.Output.Contains("     and Src/Presentation/A.Worker/Program.cs the same", StringComparison.Ordinal),
                $"[{label}] without the API did not send the composition to the hosts that hold the feature.");
            expect(
                Occurrences(root, "Src/Presentation/A.Portal/Program.cs", $"builder.Services.Add{combination[0]}();") == 1
                    && Occurrences(root, "Src/Presentation/A.Worker/Program.cs", $"builder.Services.Add{combination[0]}();") == 1
                    && Occurrences(root, "Src/Presentation/A.Portal/Program.cs", $"using Acme.Application;") == 1,
                $"[{label}] without the API did not compose the feature in each host's Program.cs.");

            string page = System.IO.Path.Combine(root, PagePath(combination[0]));

            expect(
                File.Exists(page) && !apiWords.IsMatch(File.ReadAllText(page)) && File.ReadAllText(page).Contains("- list: a page of ", StringComparison.Ordinal),
                $"[{label}] without the API wrote a page about endpoints.");
        }

        string bare = WithPageTemplate(WithoutTheApi(NewTree(scratch, reaction: false), "A.Portal", "A.Worker"));
        string composition = "Src/Presentation/A.Portal/Features/ExternalIdentity/Composition/ExternalSignInComposition.cs";

        Put(bare, composition, "        services.AddExternalIdentityPersistence(configuration);\n");
        Put(bare, "Src/Presentation/A.Worker/Features/ExternalIdentity/Composition/WorkerExternalIdentityComposition.cs", "        services.AddExternalIdentityPersistence(configuration);\n");
        Put(bare, "Src/Presentation/A.Worker/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs", "        services.AddMaintenanceJobs(configuration);\n");

        var reacted = Invoke(bare, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(reacted.Code == 0, $"A first owner feature without the API was refused: {reacted.Error.Split('\n')[0]}");
        expect(
            File.Exists(System.IO.Path.Combine(bare, "Src/Presentation/A.Portal/Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory.cs"))
                && File.Exists(System.IO.Path.Combine(bare, "Src/Presentation/A.Worker/Features/BackgroundJobs/Jobs/DeletedAccountSweepJobsExtensions.cs"))
                && !Directory.Exists(System.IO.Path.Combine(bare, "Src/Presentation/A.Api")),
            "A first owner feature without the API did not read the identity from the hosts, or wrote into the API.");
        expect(
            reacted.Output.Contains($" 10. {composition}: services.AddScoped<IUserAccountDirectory, ExternalIdentityUserAccountDirectory>();", StringComparison.Ordinal)
                && !apiWords.IsMatch(reacted.Output),
            $"A first owner feature without the API did not name the host composition, or named the API: {apiWords.Match(reacted.Output).Value}");

        string hostless = WithoutTheApi(NewTree(scratch));
        var hostlessBefore = Snapshot(hostless);
        var refused = Invoke(hostless, "Widgets", "Widget", "--access", "shared");

        expect(
            refused.Code == 1 && refused.Error.Contains("no host would compose the feature", StringComparison.Ordinal) && Same(hostlessBefore, Snapshot(hostless)),
            "A tree with neither the API nor a host was not refused cleanly.");
    }

    private static void PageCases(string scratch, Action<bool, string> expect)
    {
        string pagePath = PagePath("TodoLists");

        string root = WithPageTemplate(NewTree(scratch));
        var result = Invoke(
            root, "TodoLists", "TodoList", "--access", "owner", "--deletion", "anonymise", "--concurrency", "etag", "--operations", "get,list,update");

        expect(result.Code == 0, $"The page case was refused: {result.Error}");

        string full = System.IO.Path.Combine(root, pagePath);

        expect(File.Exists(full), $"{pagePath} was not written from {_pageTemplate}.");

        if (!File.Exists(full))
        {
            return;
        }

        byte[] bytes = File.ReadAllBytes(full);
        string text = Encoding.UTF8.GetString(bytes);
        string template = File.ReadAllText(System.IO.Path.Combine(root, _pageTemplate));

        expect(bytes is not [0xEF, 0xBB, 0xBF, ..], "The page starts with a UTF-8 BOM, which no Markdown file of the tree has.");
        expect(!text.Contains('\r', StringComparison.Ordinal) && text.EndsWith('\n') && !text.EndsWith("\n\n", StringComparison.Ordinal), "The page is not LF only, ending in one newline.");
        expect(!Regex.IsMatch(text, "<[A-Za-z]+>", RegexOptions.None, TimeSpan.FromSeconds(2)), "A placeholder of the page template survived.");
        expect(
            text.StartsWith("---\ntype: feature\ntitle: Todo lists\ndescription: What a caller can do with todo lists, who may, and how the feature works.\n---\n\n# Todo lists\n", StringComparison.Ordinal),
            "The page does not open with its frontmatter, a feature titled with the feature's name in words, and that title as its heading.");

        string[] headings = [.. template.Split('\n').Where(line => line.StartsWith("## ", StringComparison.Ordinal))];

        expect(
            headings.Length > 0 && headings.SequenceEqual(text.Split('\n').Where(line => line.StartsWith("## ", StringComparison.Ordinal)), StringComparer.Ordinal),
            "The page does not keep the template's headings in their order.");

        string[] present =
        [
            "- `GET /api/v1/todo-lists`: a page of todo lists",
            "- `GET /api/v1/todo-lists/{todoListId}`: a todo list",
            "- `PUT /api/v1/todo-lists/{todoListId}`: replaces a todo list",
            "on their own todo lists only",
            "are anonymised",
            "If-Match",
            "`Tests/Application/A.Application.UnitTests/Features/TodoLists`",
        ];

        foreach (string needle in present)
        {
            expect(text.Contains(needle, StringComparison.Ordinal), $"The page does not say '{needle}'.");
        }

        expect(!text.Contains("POST", StringComparison.Ordinal) && !text.Contains("DELETE", StringComparison.Ordinal), "The page lists an operation the feature was not generated with.");

        using (var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(root, ".feature-manifest", "TodoLists.json"))))
        {
            var manifest = document.RootElement;

            expect(manifest.TryGetProperty("page", out var page) && page.GetString() == pagePath, "The manifest does not record the page.");
            expect(
                manifest.GetProperty("files").EnumerateArray().All(file => file.GetProperty("path").GetString() != pagePath),
                "The page is among the hashed files, so editing it would hold remove-feature back.");
        }

        expect(result.Output.Contains($"Feature page: {pagePath}", StringComparison.Ordinal), "The page written was not reported.");
        expect(result.Output.Contains("docs-index lists it in the index of docs/features", StringComparison.Ordinal), "The index that lists the page was not named.");
        expect(result.Output.Contains("Under `/api/v1/todo-lists`.", StringComparison.Ordinal), "The route section of docs/reference/api.md was not printed.");
        expect(result.Output.Contains("| GET, HEAD | `/{todoListId}` | 200 | |", StringComparison.Ordinal), "The row of the get route was not printed.");

        string shared = WithPageTemplate(NewTree(scratch));
        var sharedResult = Invoke(shared, "Widgets", "Widget", "--access", "shared");
        string sharedText = sharedResult.Code == 0 ? File.ReadAllText(System.IO.Path.Combine(shared, PagePath("Widgets"))) : string.Empty;

        expect(sharedResult.Code == 0, $"The shared page case was refused: {sharedResult.Error}");

        foreach (string needle in new[] { "every authenticated caller, on the same widgets", "the last write wins", "- `POST /api/v1/widgets`: creates a widget", "- `DELETE /api/v1/widgets/{widgetId}`: deletes a widget" })
        {
            expect(sharedText.Contains(needle, StringComparison.Ordinal), $"The shared page does not say '{needle}'.");
        }

        string written = WithPageTemplate(NewTree(scratch));
        string own = System.IO.Path.Combine(written, PagePath("Widgets"));

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(own)!);
        File.WriteAllText(own, "# Widgets, as the team wrote them\n");

        var kept = Invoke(written, "Widgets", "Widget", "--access", "shared");

        expect(kept.Code == 0, $"A page already written held the feature back: {kept.Error}");
        expect(File.ReadAllText(own) == "# Widgets, as the team wrote them\n", "A page the team wrote was overwritten.");
        expect(kept.Output.Contains("already exists, so it was left as it is", StringComparison.Ordinal), "A page left as it was is not reported.");
        expect(
            !File.ReadAllText(System.IO.Path.Combine(written, ".feature-manifest", "Widgets.json")).Contains("\"page\"", StringComparison.Ordinal),
            "A page the generator did not write is recorded, so remove-feature would delete it.");

        string bare = NewTree(scratch);
        var none = Invoke(bare, "Widgets", "Widget", "--access", "shared");

        expect(none.Code == 0, $"A tree without the page template was refused: {none.Error}");
        expect(!Directory.Exists(System.IO.Path.Combine(bare, "docs/features")), "A page was written without a page template.");
        expect(none.Output.Contains($"'{_pageTemplate}' is not in this tree", StringComparison.Ordinal), "A missing page template is not reported.");
    }

    private static readonly Regex[] _removableSharedPaths =
    [
        new(@"^Src/Application/[^/]+\.Application\.Core/Common/Ports/(IDeletedAccountDataPolicy|IUserAccountDirectory)\.cs$", RegexOptions.None, TimeSpan.FromSeconds(2)),
        new(@"^Src/Application/[^/]+\.Application\.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountData\.cs$", RegexOptions.None, TimeSpan.FromSeconds(2)),
        new(@"^Src/Presentation/[^/]+/Features/BackgroundJobs/Jobs/DeletedAccountSweep(JobsExtensions|Options)\.cs$", RegexOptions.None, TimeSpan.FromSeconds(2)),
        new(@"^Src/Presentation/[^/]+/Features/Auth/Composition/AccountUserAccountDirectory\.cs$", RegexOptions.None, TimeSpan.FromSeconds(2)),
        new(@"^Src/Presentation/[^/]+/Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory\.cs$", RegexOptions.None, TimeSpan.FromSeconds(2)),
    ];

    private static int Occurrences(string root, string path, string line)
    {
        string full = System.IO.Path.Combine(root, path);

        return File.Exists(full) ? File.ReadAllLines(full).Count(candidate => candidate.Trim() == line) : -1;
    }

    private static void ExpectRemovable(string root, Dictionary<string, string> before, string features, int count, Action<bool, string> expect)
    {
        var outside = Snapshot(root)
            .Where(pair => pair.Value != "<directory>" && !before.ContainsKey(pair.Key))
            .Select(pair => pair.Key)
            .Where(path => !path.Contains($"/Features/{features}/", StringComparison.Ordinal) && !path.StartsWith(".feature-manifest/", StringComparison.Ordinal))
            .ToList();

        expect(outside.Count == count, $"The reaction wrote {outside.Count} file(s) outside {features}' folders, not {count}.");

        foreach (string path in outside.Where(path => !_removableSharedPaths.Any(pattern => pattern.IsMatch(path))))
        {
            expect(false, $"{path} is written with the first owner feature but RemoveFeature.cs's _sharedPaths would refuse it.");
        }
    }

    private const string _everyType =
        "Title:string(120), Nickname:string(40)?, Notes:text?, Summary:text(500), Count:int>=0<=1000, Total:long?>-5, "
        + "Budget:decimal(12,2)>=0, Ratio:decimal(5,4)?, Paid:bool, Due:DateOnly, StartsAt:DateTimeOffset, Reference:Guid?";

    private static string FieldTree(string scratch)
    {
        string root = NewTree(scratch);

        Put(root, "Tests/Integration/A.Api.IntegrationTests/Infrastructure/TestDatabase.cs", "        [\n            AppDbContext.PlatformSchema,\n        ];\n");

        return root;
    }

    private static void FieldGrammarCases(string scratch, Action<bool, string> expect)
    {
        (string Text, string Declarations)[] accepted =
        [
            ("Title:string(120)", "Title:string(120)"),
            (" Notes : text ? ", "Notes:text(2000)?"),
            ("Body:text(65536)", "Body:text(65536)"),
            ("Budget:decimal>=0", "Budget:decimal(18,2)>=0"),
            ("Ratio:decimal(5,4)?>-1<1", "Ratio:decimal(5,4)?>-1<1"),
            ("Price:decimal(28,0)", "Price:decimal(28,0)"),
            ("Count:int>=1<=10, Total:long>-5, Paid:bool", "Count:int>=1<=10, Total:long>-5, Paid:bool"),
            ("Due:DateOnly?, At:DateTimeOffset, Reference:Guid?", "Due:DateOnly?, At:DateTimeOffset, Reference:Guid?"),
            ("Big:long<=9000000000", "Big:long<=9000000000"),
            ("Code:string(400)", "Code:string(400)"),
        ];

        foreach (var (text, declarations) in accepted)
        {
            var problems = new List<string>();
            var fields = FieldGrammar.Parse(text, FieldProfile.Feature, problems);

            expect(
                fields is not null && string.Join(", ", fields.Select(field => field.Declaration)) == declarations,
                $"[--fields {text}] was not read as {declarations}: {(fields is null ? string.Join(" | ", problems) : string.Join(", ", fields.Select(field => field.Declaration)))}");
        }

        (string Text, string Fragment)[] refused =
        [
            ("", "--fields is empty"),
            ("Title", "expected ':' after the field name 'Title'"),
            ("Title:", "at character 7: expected a type"),
            ("Title:string", "string needs its maximum length"),
            ("Title:string(0)", "a string is 1 to 400 characters long"),
            ("Title:string(401)", "a string is 1 to 400 characters long"),
            ("Title:string(12", "expected ')'"),
            ("Title:string(x)", "expected a whole number"),
            ("Notes:text(65537)", "a text is 1 to 65,536 characters long"),
            ("Budget:decimal(29,2)", "1 to 28 digits in all"),
            ("Budget:decimal(4,5)", "1 to 28 digits in all"),
            ("Budget:decimal(4)", "',' between the precision and the scale"),
            ("State:enum", "enum is not a field type"),
            ("At:DateTime", "Use DateTimeOffset"),
            ("Rate:double", "Use decimal"),
            ("Title:String(10)", "written as listed, case included"),
            ("Title:string(10)>=1", "only int, long and decimal take a bound"),
            ("Due:DateOnly>=1", "relative to the clock"),
            ("Count:int>=1.5", "is not a whole number an int holds"),
            ("Count:int<=3000000000", "is not a whole number an int holds"),
            ("Count:int>=-2147483648", "bounds nothing"),
            ("Count:int>=5<=4", "no value satisfies the bounds"),
            ("Count:int>5<6", "no value satisfies the bounds"),
            ("Count:int>=1>=2", "already has a lower bound"),
            ("Count:int>=x", "expected a number after '>='"),
            ("Budget:decimal(5,2)>=0.001", "does not fit decimal(5,2)"),
            ("Budget:decimal(5,2)<=1000", "does not fit decimal(5,2)"),
            ("title:int", "is not a field name"),
            ("Due_Date:int", "is not a field name"),
            ("Class:int", "C# reserves that word"),
            ("Value:int", "C# reserves that word"),
            ("Id:int", "a column or a member the feature already declares"),
            ("CreatedAt:DateTimeOffset", "a column or a member the feature already declares"),
            ("Guid:int", "names a type the generated code calls by name"),
            ("Title:string(10), TitleMaxLength:int", "a constant, a method or a local of Title"),
            ("Title:string(10), RequireTitle:int", "a constant, a method or a local of Title"),
            ("Title:string(10), TITLE:int", "the same name but for its case"),
            ("Title:string(10) Count:int", "expected a comma before the next field"),
            ("Title:string(10),,Count:int", "expected a field name"),
            ("Title:string(10),", "expected a field name, PascalCase, as Title, and found the end of the list"),
            ("Title:string(10)\"); File.Delete(\"x", "at character 17: expected a comma"),
            ("Tiétle:int", "expected ':' after the field name 'Ti'"),
            (new string('A', 41) + ":int", "longer than 40 characters"),
        ];

        string root = FieldTree(scratch);
        var before = Snapshot(root);

        foreach (var (text, fragment) in refused)
        {
            var result = Invoke(root, "Widgets", "Widget", "--access", "shared", "--fields", text);

            expect(result.Code == 1, $"[--fields {text}] exited {result.Code}, not 1.");
            expect(result.Error.Contains(fragment, StringComparison.Ordinal), $"[--fields {text}] did not say '{fragment}': {result.Error.Split('\n')[0]}");
            expect(Same(before, Snapshot(root)), $"[--fields {text}] changed the tree.");
        }

        (string[] Args, string Fragment)[] conflicts =
        [
            (["Widgets", "Widget", "--access", "shared", "--fields", "Widget:int>0"], "a property cannot share it"),
            (["Widgets", "Widget", "--access", "shared", "--fields", "WidgetId:Guid"], "a property cannot share it"),
            (["Widgets", "Widget", "--access", "shared", "--fields", "WidgetDto:Guid"], "a property cannot share it"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "anonymise", "--fields", "Code:string(16)"], "Give it string(17)"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "anonymise", "--fields", "Count:int>=0"], "there is nothing to anonymise"),
            (["Widgets", "Widget", "--access", "shared", "--fields", "Paid:bool, Count:int, Due:DateOnly?"], "no field has a rule to check"),
            ([new string('W', 39) + "s", new string('W', 39), "--access", "owner", "--deletion", "delete", "--fields", new string('T', 20) + ":string(10)"], "PostgreSQL cuts a name at 63"),
        ];

        foreach (var (args, fragment) in conflicts)
        {
            var result = Invoke(root, args);
            string label = string.Join(' ', args[2..]);

            expect(result.Code == 1 && result.Error.Contains(fragment, StringComparison.Ordinal), $"[{label}] was not refused with '{fragment}': {result.Error.Split('\n')[0]}");
            expect(Same(before, Snapshot(root)), $"[{label}] changed the tree.");
        }

        var anonymised = Invoke(root, "--dry-run", "Widgets", "Widget", "--access", "owner", "--deletion", "anonymise", "--fields", "Code:string(17), Count:int>=0");

        expect(anonymised.Code == 0, $"A string(17) was refused with --deletion anonymise: {anonymised.Error}");
        expect(anonymised.Output.Contains("keeps Count", StringComparison.Ordinal), "The report does not name the field --deletion anonymise keeps.");
        expect(Same(before, Snapshot(root)), "A --dry-run with --fields changed the tree.");
    }

    private static void FieldEmissionCases(string scratch, Action<bool, string> expect)
    {
        string aggregateRoot = FieldTree(scratch);
        var aggregate = Invoke(aggregateRoot, "Invoices", "Invoice", "--access", "owner", "--deletion", "anonymise", "--model", "aggregate", "--concurrency", "etag", "--fields", _everyType);

        expect(aggregate.Code == 0, $"The aggregate with every field type was refused: {aggregate.Error}");

        string recordRoot = FieldTree(scratch);
        var record = Invoke(recordRoot, "Orders", "Order", "--access", "owner", "--deletion", "delete", "--fields", _everyType);

        expect(record.Code == 0, $"The record with every field type was refused: {record.Error}");

        string Read(string root, string path) =>
            File.Exists(System.IO.Path.Combine(root, path)) ? File.ReadAllText(System.IO.Path.Combine(root, path)) : $"<{path} was not written>";

        (string Root, string Path, string[] Present, string[] Absent)[] expectations =
        [
            (aggregateRoot, "Src/Domain/A.Domain/Features/Invoices/Model/Entities/Invoice.cs",
                [
                    "public const int TitleMaxLength = 120;", "public const int NotesMaxLength = 2_000;", "public const int CountMaximum = 1000;",
                    "public const long TotalExclusiveMinimum = -5;", "public const decimal BudgetMinimum = 0m;", "public const int RatioScale = 4;",
                    "private const decimal _budgetCeiling = 1e10m;", "public string? Nickname { get; private set; }", "RequireStartsAt(startsAt),",
                    "return value.ToUniversalTime();", "private static DateTimeOffset RequireStartsAt(DateTimeOffset value)", "Nickname = null;", "Title = AnonymisedText;",
                    "Summary = string.Empty;", "if (decimal.Round(value, BudgetScale) != value || Math.Abs(value) >= _budgetCeiling)",
                    "throw new DomainException(\"An invoice title is required.\");", "if (value == default)", "if (present <= TotalExclusiveMinimum)",
                    "            paid,",
                ],
                ["Name", "Description"]),
            (aggregateRoot, "Src/Application/A.Application/Features/Invoices/UseCases/Commands/Create%A%/CreateInvoiceCommandValidator.cs".Replace("%A%", "Invoice", StringComparison.Ordinal),
                [
                    ".PrecisionScale(Invoice.BudgetPrecision, Invoice.BudgetScale, ignoreTrailingZeros: true)",
                    ".GreaterThan(Invoice.TotalExclusiveMinimum).WithMessage(\"An invoice total must be greater than -5.\")",
                    ".When(command => command.Nickname is not null)", ".NotEmpty().WithMessage(\"An invoice due is required.\")",
                ],
                ["RuleFor(command => command.Paid)", "RuleFor(command => command.Reference)"]),
            (aggregateRoot, "Src/Infrastructure/A.Infrastructure.Persistence/Features/Invoices/Rows/Configurations/InvoiceRecordConfiguration.cs",
                [
                    ".HasPrecision(Invoice.BudgetPrecision, Invoice.BudgetScale)", ".HasMaxLength(Invoice.NotesMaxLength);",
                    "IX_Invoices_OwnerId_Title_Id", "IX_Invoices_OwnerId_StartsAt_Id", "builder.Property(record => record.Reference);",
                ],
                ["IX_Invoices_OwnerId_Notes_Id", "IX_Invoices_OwnerId_Budget_Id", "IX_Invoices_OwnerId_Nickname_Id", "IX_Invoices_OwnerId_Paid_Id"]),
            (aggregateRoot, "Src/Application/A.Application/Features/Invoices/Model/Policies/InvoiceCollectionPolicy.cs",
                ["SortableField.Keyset(TitleField),", "SortableField.OffsetOnly(CountField),", "SortableField.OffsetOnly(DueField),", "SortableField.Keyset(StartsAtField),"],
                ["RatioField", "NotesField", "ReferenceField"]),
            (aggregateRoot, "Src/Application/A.Application/Features/Invoices/UseCases/Queries/GetInvoices/GetInvoices.cs",
                ["InvoiceCollectionPolicy.CreatedAtField, InvoiceCollectionPolicy.StartsAtField);"], []),
            (aggregateRoot, "Src/Presentation/A.Api/Features/Invoices/Contracts/Requests/CreateInvoiceRequest.cs",
                ["using System.Text.Json.Serialization;", "[property: JsonRequired] int Count,", "string? Notes,", "decimal? Ratio,", "<param name=\"Budget\">Required, with at most 10 digits before the decimal point and 2 after it, at least 0.</param>"],
                ["JsonRequired] string", "JsonRequired] decimal?"]),
            (aggregateRoot, "Src/Application/A.Application/Features/Invoices/Model/Dtos/InvoiceSummaryDto.cs", ["string Title,", "DateTimeOffset StartsAt,"], ["Notes", "Summary,"]),
            (aggregateRoot, "Src/Infrastructure/A.Infrastructure.Persistence/Features/Invoices/Access/Queries/InvoiceSortMap.cs",
                ["ApplyStartsAtKeyset(source, term.Direction, cursor)", "InvoiceCollectionPolicy.CountField => term.Direction == SortDirection.Ascending"],
                ["ApplyCountKeyset"]),
            (aggregateRoot, "Tests/Integration/A.Api.IntegrationTests/Features/Invoices/InvoiceFieldTests.cs",
                ["TitleBlank_Answers400_WithTheValidationProblem()", "EveryDateTimeOffset_SentWithAnOffset_IsKeptAsTheSameInstant_InUtc()"], []),
            (aggregateRoot, "Tests/Integration/A.Api.IntegrationTests/Infrastructure/TestDatabase.cs", ["AppDbContext.InvoicesSchema,"], []),
            (recordRoot, "Src/Infrastructure/A.Infrastructure.Persistence/Features/Orders/Access/Stores/OrderStore.cs",
                ["StartsAt = content.StartsAt.ToUniversalTime(),", "record.StartsAt = content.StartsAt.ToUniversalTime();"], ["AnonymisedText"]),
            (recordRoot, "Src/Application/A.Application/Features/Orders/Services/OrderService.cs",
                ["content with { Title = content.Title.Trim(), Nickname = content.Nickname?.Trim() };", "OrderCollectionPolicy.CreatedAtField, OrderCollectionPolicy.StartsAtField);"], []),
            (recordRoot, "Src/Application/A.Application/Features/Orders/Model/Policies/OrderInputPolicy.cs", ["public const int SummaryMaxLength = 500;"], ["_budgetCeiling"]),
            (recordRoot, "Src/Application/A.Application/Features/Orders/Model/Dtos/OrderContentDtoValidator.cs", [".Must(value => value.Trim().Length <= OrderInputPolicy.TitleMaxLength)"], []),
        ];

        foreach (var (root, path, present, absent) in expectations)
        {
            string text = Read(root, path);

            foreach (string fragment in present)
            {
                expect(text.Contains(fragment, StringComparison.Ordinal), $"{path} does not hold '{fragment}'.");
            }

            foreach (string fragment in absent)
            {
                expect(!text.Contains(fragment, StringComparison.Ordinal), $"{path} holds '{fragment}', which these fields do not ask for.");
            }
        }

        foreach (string root in new[] { aggregateRoot, recordRoot })
        {
            string[] legacy = [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => path.Contains($"{System.IO.Path.DirectorySeparatorChar}Features{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(path => File.ReadAllText(path).Contains("Description", StringComparison.Ordinal))
                .Select(path => System.IO.Path.GetRelativePath(root, path))];

            expect(legacy.Length == 0, $"--fields still wrote the default Description into: {string.Join(", ", legacy)}");
        }

        using (var document = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(aggregateRoot, ".feature-manifest/Invoices.json"))))
        {
            var top = document.RootElement;
            string[] declarations = [.. top.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("declaration").GetString()!)];

            expect(
                string.Join(", ", declarations) == string.Join(", ", FieldGrammar.Parse(_everyType, FieldProfile.Feature, [])!.Select(field => field.Declaration)),
                $"The manifest does not record the fields as declared: {string.Join(", ", declarations)}");
            expect(
                top.GetProperty("edits").EnumerateArray().Any(edit => edit.GetProperty("path").GetString()!.EndsWith("TestDatabase.cs", StringComparison.Ordinal)),
                "The manifest does not record the TestDatabase edit, so remove-feature could not undo it.");
        }

        var before = Snapshot(aggregateRoot);
        var again = Invoke(aggregateRoot, "Invoices", "Invoice", "--access", "owner", "--deletion", "anonymise", "--model", "aggregate", "--concurrency", "etag", "--fields", _everyType);

        expect(again.Code == 0 && again.Output.Contains("nothing is written again", StringComparison.Ordinal), "A second run with the same --fields did not resume.");
        expect(Same(before, Snapshot(aggregateRoot)), "A second run with the same --fields wrote something.");

        var other = Invoke(aggregateRoot, "Invoices", "Invoice", "--access", "owner", "--deletion", "anonymise", "--model", "aggregate", "--concurrency", "etag", "--fields", "Title:string(120)");

        expect(other.Code == 1 && other.Error.Contains("already exists", StringComparison.Ordinal), "A second run with other --fields was not refused.");
        expect(Same(before, Snapshot(aggregateRoot)), "A second run with other --fields wrote something.");

        string withoutIntegration = NewTree(scratch);
        var plain = Invoke(withoutIntegration, "Tags", "Tag", "--access", "shared", "--fields", "Label:string(60)");

        expect(plain.Code == 0 && plain.Output.Contains("Tests/Integration/A.Api.IntegrationTests' is not in this tree", StringComparison.Ordinal), $"A tree without the integration project did not skip the field test: {plain.Error}");
    }

    private static readonly (string Group, string Digest)[] _withoutFieldsDigests =
    [
        ("record, owner, delete", "27a93843db9c4c24156282c4f4dcdb4116d0ae89bd6742267dbdd7dfebe1ba99"),
        ("record, owner, anonymise", "61919d29133b0234a3ebb4f3319c5945037b29bd745cc7ea747440133b44794e"),
        ("record, shared", "78f1a9dda6255f71f49a06520c7a08946a5e55d9aab44e0d903c4718637cd981"),
        ("aggregate, owner, delete", "9fb7d8fae221e8ba6c938f811a94a3e82b468eaf2d84d55ce061c52d33d1b01c"),
        ("aggregate, owner, anonymise", "b75cd337189f8fa7ceea03b159f7a29aee35dc71ed9a51d60a8ea03f292cbf44"),
        ("aggregate, shared", "cde473dce83dd44af2dfa21c48105bf56508fdd9cd1cd7188b765309f735dc54"),
        ("record, owner, delete, the project's first owner feature", "f9df11152b8c8393401a8a9e2c1e51bc1f76b0140c4bb542237a5f1ed44eeb78"),
        ("record, owner, delete, on SQL Server", "27a93843db9c4c24156282c4f4dcdb4116d0ae89bd6742267dbdd7dfebe1ba99"),
        ("record, owner, delete, on SQLite", "cf57998851c5f9d075f51351b09e479e1532cbf032ee9b643f672b82a4d2b8fd"),
        ("record, owner, delete, without the API", "0e4f85486a903bfb582e1dd07d6de601d917fa047edd20f56aeca0c73bac0468"),
    ];

    private static void InstantCases(string scratch, Action<bool, string> expect)
    {
        const string decoded = "var key = parsed.ToUniversalTime();";

        string plain = NewTree(scratch);
        var widgets = Invoke(plain, "Widgets", "Widget", "--access", "shared");
        string widgetMap = File.ReadAllText(System.IO.Path.Combine(plain, "Src/Infrastructure/A.Infrastructure.Persistence/Features/Widgets/Access/Queries/WidgetSortMap.cs"));

        expect(
            widgets.Code == 0 && Occurrences(widgetMap, decoded) == 1 && !widgetMap.Contains("out DateTimeOffset key", StringComparison.Ordinal),
            $"The created-at cursor key a client sends is not decoded in UTC, which PostgreSQL requires: {widgets.Error}");

        string fielded = FieldTree(scratch);
        var invoices = Invoke(fielded, "Invoices", "Invoice", "--access", "owner", "--deletion", "anonymise", "--model", "aggregate", "--concurrency", "etag", "--fields", _everyType);
        string invoiceMap = File.ReadAllText(System.IO.Path.Combine(fielded, "Src/Infrastructure/A.Infrastructure.Persistence/Features/Invoices/Access/Queries/InvoiceSortMap.cs"));
        string apiTests = File.ReadAllText(System.IO.Path.Combine(fielded, "Tests/Integration/A.Api.IntegrationTests/Features/Invoices/InvoiceFieldTests.cs"));

        expect(
            invoices.Code == 0 && Occurrences(invoiceMap, decoded) == 2 && !invoiceMap.Contains("out DateTimeOffset key", StringComparison.Ordinal),
            $"A cursor key of a DateTimeOffset field or of the creation instant is not decoded in UTC: {invoices.Error}");
        expect(
            apiTests.Contains("ACursorKey_SentWithANonZeroOffset_ResumesAfterTheSameInstant()", StringComparison.Ordinal)
                && apiTests.Contains("first.ToOffset(TimeSpan.FromHours(2))", StringComparison.Ordinal)
                && apiTests.Contains("using Acme.Application.Core.Common.Collections;", StringComparison.Ordinal),
            "The integration test of a cursor key sent with an offset was not written.");

        static int Occurrences(string text, string part) => (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;
    }

    private static void ProviderCases(string scratch, Action<bool, string> expect)
    {
        const string test = "Tests/Infrastructure/A.Infrastructure.Persistence.UnitTests/Features/Widgets/Rows/Configurations/WidgetRecordConfigurationTests.cs";
        string[] inItsSchema = ["public void TheTable_LivesInItsOwnSchema()", "Entity().GetTableName().ShouldBe(\"Widgets\");", "Entity().GetSchema().ShouldBe(AppDbContext.WidgetsSchema);"];
        string[] prefixed = ["public void TheTable_IsPrefixedWithTheWidgetsSchema()", "Entity().GetTableName().ShouldBe($\"{AppDbContext.WidgetsSchema}_Widgets\");", "Entity().GetSchema().ShouldBeNull();"];

        foreach (var (database, present, absent) in new (string, string[], string[])[] { ("postgresql", inItsSchema, prefixed), ("sqlserver", inItsSchema, prefixed), ("sqlite", prefixed, inItsSchema) })
        {
            foreach (string[] fields in new[] { Array.Empty<string>(), ["--fields", "Title:string(120), Budget:decimal>=0"] })
            {
                string root = NewTree(scratch, database: database);
                var result = Invoke(root, ["Widgets", "Widget", "--access", "shared", .. fields]);
                string written = File.Exists(System.IO.Path.Combine(root, test)) ? File.ReadAllText(System.IO.Path.Combine(root, test)) : string.Empty;

                expect(
                    result.Code == 0 && present.All(line => written.Contains(line, StringComparison.Ordinal)) && !absent.Any(line => written.Contains(line, StringComparison.Ordinal)),
                    $"[{database}{(fields.Length == 0 ? string.Empty : ", --fields")}] the table test does not hold where {database} keeps the feature's table: {result.Error}{written}");
            }
        }

        foreach (string projectFile in new[] { "{\n  \"name\": \"Acme\"\n}\n", "{\n  \"name\": \"Acme\",\n  \"options\": { \"database\": \"oracle\" }\n}\n" })
        {
            string root = NewTree(scratch);

            File.WriteAllText(System.IO.Path.Combine(root, "cleanarch.json"), projectFile);

            var before = Snapshot(root);
            var refused = Invoke(root, "Widgets", "Widget", "--access", "shared");

            expect(
                refused.Code == 1 && Same(before, Snapshot(root)) && refused.Error.Contains("records no options.database the generator knows", StringComparison.Ordinal),
                $"A project file that does not say which database the project uses was not refused before anything was written: {refused.Error}");
        }
    }

    private static void WithoutFieldsCases(string scratch, Action<bool, string> expect)
    {
        foreach (var (group, digest) in WithoutFieldsDigests(scratch))
        {
            string recorded = _withoutFieldsDigests.Single(entry => entry.Group == group).Digest;

            expect(
                digest == recorded,
                $"[{group}] without --fields, the files, the edits, the manifest and the report are no longer those this "
                + $"generator wrote before --fields existed: digest {digest}, recorded {recorded}. A change to that output "
                + "is a change for every project that runs add feature: make it on purpose, then record the new digest.");
        }
    }

    private static string? Option(string[] options, string name) => Array.IndexOf(options, name) is >= 0 and var at ? options[at + 1] : null;

    private static IEnumerable<(string Group, string Digest)> WithoutFieldsDigests(string scratch)
    {
        string[][] subsets =
        [
            .. Enumerable.Range(1, (1 << _operationNames.Length) - 1)
                .Select(mask => _operationNames.Where((_, index) => (mask & (1 << index)) != 0).ToArray()),
        ];

        (string Group, string[] Options, bool Reaction, bool Api, string Database)[] groups =
        [
            ("record, owner, delete", ["--access", "owner", "--deletion", "delete"], true, true, "postgresql"),
            ("record, owner, anonymise", ["--access", "owner", "--deletion", "anonymise"], true, true, "postgresql"),
            ("record, shared", ["--access", "shared"], true, true, "postgresql"),
            ("aggregate, owner, delete", ["--access", "owner", "--deletion", "delete", "--model", "aggregate"], true, true, "postgresql"),
            ("aggregate, owner, anonymise", ["--access", "owner", "--deletion", "anonymise", "--model", "aggregate"], true, true, "postgresql"),
            ("aggregate, shared", ["--access", "shared", "--model", "aggregate"], true, true, "postgresql"),
            ("record, owner, delete, the project's first owner feature", ["--access", "owner", "--deletion", "delete"], false, true, "postgresql"),
            ("record, owner, delete, without the API", ["--access", "owner", "--deletion", "delete"], true, false, "postgresql"),
            ("record, owner, delete, on SQL Server", ["--access", "owner", "--deletion", "delete"], true, true, "sqlserver"),
            ("record, owner, delete, on SQLite", ["--access", "owner", "--deletion", "delete"], true, true, "sqlite"),
        ];

        foreach (var (group, options, reaction, api, database) in groups)
        {
            string root = NewTree(scratch, reaction, database);

            if (!api)
            {
                WithoutTheApi(root, "A.Worker");
            }

            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            void Feed(string text) => digest.AppendData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n") + "\u0000"));

            foreach (string[] subset in subsets)
            {
                foreach (string concurrency in new[] { "none", "etag" })
                {
                    var problems = new List<string>();
                    var request = new AddFeatureRequest(
                        "Widgets", "Widget", options[Array.IndexOf(options, "--access") + 1], Option(options, "--deletion"), string.Join(',', subset), Option(options, "--model"), concurrency, Fields: null);

                    if (Check(request, System.IO.Path.GetFullPath(root), GeneratorRun.Project(root), problems) is not { } spec)
                    {
                        continue;
                    }

                    var plan = Plan(spec, root, out var skipped);
                    var editor = Edits(root, spec, plan);
                    var manifest = ManifestOf(spec, plan, null, editor, Version);

                    using var report = new StringWriter();
                    Report(report, root, spec, plan, manifest.Path, skipped, ReportMode.DryRun);
                    ReportPage(report, spec, null, "no page", ReportMode.DryRun);

                    Feed($"{string.Join(',', subset)} {concurrency}");

                    foreach (var emission in plan)
                    {
                        Feed(emission.Path);
                        Feed(emission.Content);
                    }

                    Feed(manifest.Content);

                    foreach (var (path, _, bytes) in TextEdits.Replacements(editor))
                    {
                        Feed(path);
                        Feed(Encoding.UTF8.GetString(bytes));
                    }

                    Feed(report.ToString());
                }
            }

            yield return (group, Convert.ToHexStringLower(digest.GetHashAndReset()));
        }
    }

    private static void TemplateGuardCases(Action<bool, string> expect)
    {
        bool unknownFlagRefused;

        try
        {
            Template.Evaluate("get&nonsense", new HashSet<string>(StringComparer.Ordinal) { "get" });
            unknownFlagRefused = false;
        }
        catch (InvalidOperationException)
        {
            unknownFlagRefused = true;
        }

        expect(unknownFlagRefused, "A condition naming a flag that does not exist evaluated instead of failing, so a typo would silently drop a block.");
    }

    private static IEnumerable<string> FileProblems(string root, string relative)
    {
        string full = System.IO.Path.Combine(root, relative);
        byte[] bytes = File.ReadAllBytes(full);

        if (bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF)
        {
            yield return $"{relative}: no UTF-8 BOM, which the build turns into an error that does not say so.";
        }

        string text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (text.Contains('\r', StringComparison.Ordinal))
        {
            yield return $"{relative}: a CR, where this tree is LF only.";
        }

        if (text.EndsWith("\n\n", StringComparison.Ordinal) || !text.EndsWith('\n'))
        {
            yield return $"{relative}: does not end in exactly one newline.";
        }

        string[] segments = relative.Replace('\\', '/').Split('/');
        int projectIndex = Array.FindIndex(segments, segment => segment.Contains('.', StringComparison.Ordinal));
        segments[projectIndex] = ProjectInFolder(GeneratorRun.Project(root).File.Name, segments[projectIndex]);
        string expected = string.Join('.', segments[projectIndex..^1]);

        if (!text.Contains($"namespace {expected};", StringComparison.Ordinal))
        {
            yield return $"{relative}: its namespace is not '{expected}', which is its project plus its folders.";
        }

        string stem = System.IO.Path.GetFileNameWithoutExtension(relative);
        bool useCaseFile = segments.Length >= 3 && segments[^2] == stem && segments[^3] is "Commands" or "Queries";
        string message = stem + (segments.Length >= 3 && segments[^3] == "Commands" ? "Command" : "Query");
        string[] expectedTypes = useCaseFile ? [message, message + "Handler"] : [stem];
        string[] declared = [.. Regex.Matches(
                text,
                @"^(?:public |internal )(?:sealed |static |abstract )*(?:class|interface|record|enum)\s+([A-Za-z0-9_]+)",
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(2))
            .Select(match => match.Groups[1].Value)];

        if (!declared.SequenceEqual(expectedTypes, StringComparer.Ordinal))
        {
            yield return $"{relative}: declares {(declared.Length == 0 ? "nothing" : string.Join(", ", declared))} "
                + $"where the file is named for {string.Join(" and ", expectedTypes)}.";
        }

        foreach (string problem in CommentProblems(relative, text))
        {
            yield return problem;
        }

        foreach (string problem in PrivateMethodProblems(relative, text))
        {
            yield return problem;
        }
    }

    private static readonly Regex _privateMethodDeclaration = new(
        @"^\s*private\s+(?:static\s+)?(?:async\s+)?[^=;()]+?\s([A-Z][A-Za-z0-9]*)\(",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(2));

    private static readonly Lazy<HashSet<string>> _templatePrivateMethods = new(() =>
        [.. typeof(FeatureTemplates)
            .GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Where(property => property.PropertyType == typeof(string))
            .SelectMany(property => _privateMethodDeclaration.Matches((string)property.GetValue(null)!))
            .Select(match => match.Groups[1].Value.Replace("%", string.Empty, StringComparison.Ordinal))]);

    private static IEnumerable<string> PrivateMethodProblems(string relative, string text)
    {
        if (_templatePrivateMethods.Value.Count == 0)
        {
            yield return "No feature template declares a private method, so the dead-code rule compares nothing: read the templates where FeatureTemplates keeps them.";
        }

        var declared = _privateMethodDeclaration.Matches(text)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string name in _templatePrivateMethods.Value)
        {
            int calls = Regex.Count(text, $@"(?<![.\w]){name}\b(?!\s*=)", RegexOptions.None, TimeSpan.FromSeconds(2));

            if (declared.Contains(name) && calls < 2)
            {
                yield return $"{relative}: declares the private method '{name}' and never calls it: dead code in every project it reaches.";
            }
            else if (!declared.Contains(name) && calls > 0)
            {
                yield return $"{relative}: calls '{name}', which it does not declare, so it does not compile.";
            }
        }
    }

    private static readonly HashSet<string> _documentationElements =
        new(["summary", "param", "response", "c", "see", "paramref"], StringComparer.Ordinal);

    private static IEnumerable<string> CommentProblems(string relative, string text)
    {
        string[] folders = relative.Replace('\\', '/').Split('/')[..^1];
        int api = Array.FindIndex(folders, folder => folder.EndsWith(".Api", StringComparison.Ordinal));
        int contracts = Array.IndexOf(folders, "Contracts");
        bool documented = api >= 0
            && (folders[^1] == "Controllers" || (contracts > api && Array.IndexOf(folders, "Mapping", contracts) < 0));

        foreach (var (line, comment) in Comments(text))
        {
            bool documentation = comment.StartsWith("///", StringComparison.Ordinal) && !comment.StartsWith("////", StringComparison.Ordinal);

            if (!documentation || !documented)
            {
                yield return $"{relative}:{line}: a comment, where generated code carries none outside the API's documentation: {comment}";
            }
            else if (Regex.Matches(comment, "</?([A-Za-z]+)", RegexOptions.None, TimeSpan.FromSeconds(2))
                         .Select(match => match.Groups[1].Value)
                         .FirstOrDefault(element => !_documentationElements.Contains(element)) is { } element)
            {
                yield return $"{relative}:{line}: <{element}> is not an element of the API's documentation: {comment}";
            }
        }
    }

    private static List<(int Line, string Text)> Comments(string text)
    {
        var comments = new List<(int Line, string Text)>();
        int position = 0;

        char At(int offset) => position + offset < text.Length ? text[position + offset] : '\0';

        int LineOf(int index) => text.AsSpan(0, Math.Min(index, text.Length)).Count('\n') + 1;

        void Code(bool insideHole)
        {
            int depth = 0;

            while (position < text.Length)
            {
                char current = text[position];

                if (current == '/' && At(1) is '/' or '*')
                {
                    int end = At(1) == '/' ? LineEnd(text, position) : CommentEnd(text, position);

                    comments.Add((LineOf(position), text[position..end].Split('\n')[0].TrimEnd()));
                    position = end;
                }
                else if (current is '"' or '$' or '@' && StringAhead())
                {
                    Literal();
                }
                else if (current == '\'')
                {
                    position += At(1) == '\\' ? 3 : 2;

                    while (position < text.Length && text[position] is not '\'' and not '\n')
                    {
                        position++;
                    }

                    position++;
                }
                else
                {
                    if (insideHole && current == '{')
                    {
                        depth++;
                    }
                    else if (insideHole && current == '}' && depth-- == 0)
                    {
                        return;
                    }

                    position++;
                }
            }
        }

        bool StringAhead()
        {
            int index = position;

            while (index < text.Length && text[index] is '$' or '@')
            {
                index++;
            }

            return index < text.Length && text[index] == '"';
        }

        void Literal()
        {
            int dollars = 0;
            bool verbatim = false;

            while (text[position] is '$' or '@')
            {
                dollars += text[position] == '$' ? 1 : 0;
                verbatim |= text[position] == '@';
                position++;
            }

            int quotes = 0;

            while (At(quotes) == '"')
            {
                quotes++;
            }

            if (!verbatim && quotes >= 3)
            {
                string closing = new('"', quotes);

                for (position += quotes; position < text.Length;)
                {
                    if (string.CompareOrdinal(text, position, closing, 0, quotes) == 0)
                    {
                        position += quotes;
                        return;
                    }

                    if (dollars > 0 && text[position] == '{')
                    {
                        int run = 0;

                        while (At(run) == '{')
                        {
                            run++;
                        }

                        position += run;

                        if (run >= dollars)
                        {
                            Code(insideHole: true);

                            for (int closed = 0; closed < dollars && At(0) == '}'; closed++)
                            {
                                position++;
                            }
                        }

                        continue;
                    }

                    position++;
                }

                return;
            }

            for (position++; position < text.Length;)
            {
                char current = text[position];

                if (verbatim && current == '"' && At(1) == '"')
                {
                    position += 2;
                }
                else if (current == '"')
                {
                    position++;
                    return;
                }
                else if (!verbatim && current == '\\')
                {
                    position += 2;
                }
                else if (!verbatim && current == '\n')
                {
                    return;
                }
                else if (dollars > 0 && current == '{' && At(1) == '{')
                {
                    position += 2;
                }
                else if (dollars > 0 && current == '{')
                {
                    position++;
                    Code(insideHole: true);
                    position++;
                }
                else
                {
                    position++;
                }
            }
        }

        Code(insideHole: false);

        return comments;
    }

    private static int LineEnd(string text, int position) => text.IndexOf('\n', position) is var newline and >= 0 ? newline : text.Length;

    private static int CommentEnd(string text, int position) =>
        text.IndexOf("*/", position + 2, StringComparison.Ordinal) is var close and >= 0 ? close + 2 : text.Length;
}
