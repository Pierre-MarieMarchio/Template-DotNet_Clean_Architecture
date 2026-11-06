using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;

namespace CleanArch.Cli.Generators.AddFeature;

internal static partial class NewFeature
{
    internal const string Generator = "dotnet cleanarch add feature";

    internal const string ScriptGenerator = "Tools/NewFeature.cs";

    internal const string _pageTemplate = "docs/_page-templates/feature.md";

    internal const string _removalRefusal = "remove-feature refuses to remove this feature while another owner feature still uses these files.";

    private const string _usage =
        "Usage: dotnet cleanarch add feature <Features> <Feature> --access owner|shared\n"
        + "           [--deletion delete|anonymise]\n"
        + "           [--operations get,list,create,update,delete]\n"
        + "           [--model record|aggregate]\n"
        + "           [--concurrency none|etag]\n"
        + "           [--fields \"<Name:type>, ...\"]\n"
        + "           [--dry-run]\n"
        + "  <Features>     the folder, PascalCase and plural, e.g. Widgets\n"
        + "  <Feature>      the type, PascalCase and singular, e.g. Widget\n"
        + "  --access       required. owner: every operation is scoped to the caller's id.\n"
        + "                 shared: every authenticated caller sees the same records.\n"
        + "  --deletion     required with --access owner. The fate of the feature's rows when the\n"
        + "                 account that owns them is deleted: delete removes them, anonymise\n"
        + "                 rewrites what identifies the owner and keeps the rows.\n"
        + "                 Refused with --access shared, which owns no user data.\n"
        + "  --operations   default: all five. create needs get.\n"
        + "  --model        default: record, a service over a store, no domain model.\n"
        + "                 aggregate: one use case per operation over a domain aggregate.\n"
        + "  --concurrency  default: none, last write wins. etag: a persisted version, ETag and If-Match.\n"
        + "                 aggregate needs etag.\n"
        + "  --fields       default: Name and Description. The fields, Name:type, comma separated; a type is\n"
        + "                 string(n) (n up to 400), text or text(n) (2000 by default, up to 65536), int, long,\n"
        + "                 decimal or decimal(p,s) (18,2 by default), bool, DateOnly, DateTimeOffset or Guid;\n"
        + "                 ? after it makes the field nullable, and >=, <=, > or < with a number bounds an int,\n"
        + "                 a long or a decimal: --fields \"Title:string(120), Notes:text?, Budget:decimal>=0\".\n"
        + "                 Each field is checked by the validator and by the aggregate, stored in a column of\n"
        + "                 its length or precision, and carried by the contracts and the tests.\n"
        + "  --dry-run      print the files and the edits, and write and run nothing.\n"
        + "  Without --dry-run it writes the feature's files, makes every edit that composes it, each\n"
        + "  placed beside a line it finds, and records them in the manifest; when one of those lines is\n"
        + "  missing it writes nothing. It refuses while a context has no initial migration or the\n"
        + "  business model has a change no migration holds, then builds, adds the Add<Features>\n"
        + "  migration and rewrites the OpenAPI reference and the clients written from it.";

    internal static readonly string[] _operationNames = ["get", "list", "create", "update", "delete"];

    private static readonly string[] _optionNames =
        ["--access", "--deletion", "--operations", "--model", "--concurrency", "--fields", "--root"];

    private static readonly string[] _reservedNames =
    [
        "Access", "Api", "Application", "Auth", "Common", "Contracts", "Controllers", "Core", "Dtos",
        "Domain", "Entities", "Errors", "Features", "Infrastructure", "Model", "Models", "Persistence",
        "Policies", "Ports", "Presentation", "Rows", "Services", "UseCases",
    ];

    internal sealed record Emission(string Path, string Content);

    internal sealed record Request(
        string? Features,
        string? Type,
        string? Access,
        string? Deletion,
        string? Operations,
        string? Model,
        string? Concurrency,
        string? Fields,
        string Root,
        bool DryRun);

    internal sealed record Spec(
        string Prefix,
        string Features,
        string Type,
        string Access,
        string Deletion,
        string[] Operations,
        string Model,
        string Concurrency,
        string? Reaction,
        bool HasApi,
        IReadOnlyList<Field>? Fields = null,
        bool TablePrefixes = false)
    {
        internal bool Has(string operation) => Operations.Contains(operation, StringComparer.Ordinal);
    }

    internal static int Run(string[] args, TextWriter output, TextWriter error, Func<TextEdits.Command, int> run, string version)
    {
        var problems = new List<string>();
        var request = Parse(args, problems);

        string root = Path.GetFullPath(request.Root);
        var spec = problems.Count == 0 ? Check(request, root, problems) : null;

        if (spec is null)
        {
            return Refuse(error, problems);
        }

        if (AlreadyGenerated(root, spec))
        {
            output.WriteLine(
                $"{ManifestPath(spec)} records {spec.Features} with these options already: its files and edits are written, "
                + "so nothing is written again.");

            return request.DryRun ? 0 : Finish(root, spec, output, error, run, resumed: true);
        }

        var plan = Plan(spec, root, out var skipped);

        problems.AddRange(Conflicts(root, spec, plan));

        if (problems.Count > 0)
        {
            return Refuse(error, problems);
        }

        var page = PageOf(root, spec, plan, out string? unwritten);
        var editor = Edits(root, spec, plan);
        var manifest = ManifestOf(spec, plan, page, editor, version);

        if (editor.Failures.Count > 0)
        {
            Report(output, root, spec, plan, manifest.Path, skipped, Mode.Refused);
            ReportPage(output, spec, page, unwritten, Mode.Refused);

            foreach (string failure in editor.Failures)
            {
                error.WriteLine($"error: {failure}");
            }

            error.WriteLine(
                "Nothing was written: every edit is made or none is. Put back the line each failed edit is placed beside, "
                + "or write the feature by hand from the edits above.");

            return 1;
        }

        if (request.DryRun)
        {
            Report(output, root, spec, plan, manifest.Path, skipped, Mode.DryRun);
            ReportPage(output, spec, page, unwritten, Mode.DryRun);
            output.WriteLine();
            output.WriteLine("--dry-run: nothing was written and nothing was run.");

            return 0;
        }

        if (Preconditions(root, spec, output, run) is { } refusal)
        {
            error.WriteLine($"error: {refusal}");
            error.WriteLine("Nothing was written.");

            return 1;
        }

        try
        {
            TextEdits.Commit(
                root,
                [.. plan.Select(Created), .. page is null ? Array.Empty<(string, byte[])>() : [Created(page)], Created(manifest)],
                [.. TextEdits.Replacements(editor)]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"error: nothing was left behind, but the write failed: {exception.Message}");

            return 1;
        }

        Report(output, root, spec, plan, manifest.Path, skipped, Mode.Applied);
        ReportPage(output, spec, page, unwritten, Mode.Applied);

        if (editor.Kept.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"Already in place, so left as they were and not recorded: {string.Join("; ", editor.Kept.Distinct(StringComparer.Ordinal))}.");
        }

        return Finish(root, spec, output, error, run, resumed: false);
    }

    internal enum Mode
    {
        Applied,
        DryRun,
        Refused,
    }

    private static (string Path, byte[] Bytes) Created(Emission emission) => (emission.Path, Bytes(emission));

    private static bool AlreadyGenerated(string root, Spec spec)
    {
        if (FeatureManifest.Read(root, spec.Features, out _) is not { Options: { } options } manifest)
        {
            return false;
        }

        static string? Text(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

        return manifest.SchemaVersion == FeatureManifest.EditsVersion
            && manifest.Type == spec.Type
            && Text(options["access"]) == spec.Access
            && Text(options["model"]) == spec.Model
            && Text(options["concurrency"]) == spec.Concurrency
            && (Text(options["deletion"]) ?? string.Empty) == spec.Deletion
            && options["operations"] is JsonArray operations && operations.Select(Text).SequenceEqual(spec.Operations)
            && (manifest.Fields is { } fields
                ? spec.Fields is not null && fields.Select(field => Text(field?["declaration"])).SequenceEqual(spec.Fields.Select(field => field.Declaration))
                : spec.Fields is null);
    }

    private static string TestDatabasePath(Spec spec) => $"{IntegrationTests(spec)}/Infrastructure/TestDatabase.cs";

    private static bool WritesTheFieldTests(Spec spec, List<Emission> plan) =>
        plan.Any(emission => emission.Path.StartsWith(IntegrationTests(spec) + "/", StringComparison.Ordinal));

    private static string IntegrationTests(Spec spec) => ProjectOf(spec, "Tests/Integration", "Api.IntegrationTests");

    private static string PersistenceProject(Spec spec) => ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence");

    private static string? Preconditions(string root, Spec spec, TextWriter output, Func<TextEdits.Command, int> run)
    {
        string auth = ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Auth");
        string[] contexts =
        [
            PersistenceProject(spec),
            .. Directory.Exists(System.IO.Path.Combine(root, auth)) ? [auth] : Array.Empty<string>(),
        ];

        string[] missing = [.. contexts.Where(context => !HasInitialMigration(root, context))];

        if (missing.Length > 0)
        {
            return $"{string.Join(" and ", missing)} has no initial migration, so the migration of {spec.Features} would have "
                + "none to follow. Run `dotnet run Tools/Tasks.cs bootstrap`, or `dotnet run Tools/Tasks.cs migration-initial`, "
                + "and commit the migrations first (docs/guides/migrations.md).";
        }

        var check = TextEdits.Task(root, "migration-check");

        output.WriteLine($"Checking that no model change waits for a migration: {check.Display}");
        output.Flush();

        return run(check) == 0
            ? null
            : $"`{check.Display}` failed: the business model holds a change no migration holds yet, or it could not be checked. "
                + $"The migration of {spec.Features} would carry that change too, so put it in a migration of its own with "
                + "`dotnet run Tools/Tasks.cs migration-add <Name>`, or undo it, then run this again.";
    }

    private static bool HasInitialMigration(string root, string context)
    {
        string folder = System.IO.Path.Combine(root, context, "Migrations");

        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*ModelSnapshot.cs").Any();
    }

    private static int Finish(string root, Spec spec, TextWriter output, TextWriter error, Func<TextEdits.Command, int> run, bool resumed)
    {
        string migrations = System.IO.Path.Combine(root, PersistenceProject(spec), "Migrations");
        bool migrated = resumed
            && Directory.Exists(migrations)
            && Directory.EnumerateFiles(migrations, $"*_Add{spec.Features}.cs").Any();

        string done = resumed
            ? "the feature's files and edits, written by an earlier run"
            : "the feature's files, its edits and its manifest, which are consistent with each other";

        return TextEdits.RunAll(Steps(root, spec, migrated), run, output, error, done) ? 0 : 1;
    }

    private static List<TextEdits.Command> Steps(string root, Spec spec, bool migrated = false) =>
    [
        TextEdits.Task(root, "docs-index"),
        TextEdits.Task(root, "build"),
        .. migrated ? Array.Empty<TextEdits.Command>() : [TextEdits.FromLastBuild(root, "migration-add", $"Add{spec.Features}")],
        .. spec.HasApi ? TextEdits.ContractSteps(root, FolderOf(spec.Prefix, "Presentation.Client"), built: true) : [],
    ];

    private static string[] SweepMethod() =>
    [
        "public static IServiceCollection AddDeletedAccountSweep(this IServiceCollection services)",
        "{",
        "    ArgumentNullException.ThrowIfNull(services);",
        string.Empty,
        "    return services.AddUseCasePipeline();",
        "}",
    ];

    private static string[] DirectoryDouble() =>
    [
        "internal sealed class ArchitectureTestUserAccountDirectory : IUserAccountDirectory",
        "{",
        "    public Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default) =>",
        "        Task.FromResult(true);",
        "}",
    ];

    private static int Refuse(TextWriter error, List<string> problems)
    {
        foreach (string problem in problems)
        {
            error.WriteLine($"error: {problem}");
        }

        error.WriteLine();
        error.WriteLine(_usage);
        error.WriteLine();
        error.WriteLine("Nothing was written.");

        return 1;
    }

    internal static Request Parse(string[] args, List<string> problems)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        bool dryRun = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            if (!argument.StartsWith('-'))
            {
                positional.Add(argument);

                continue;
            }

            if (argument == "--dry-run")
            {
                dryRun = true;

                continue;
            }

            string name = argument;
            string? value = null;
            int equals = argument.IndexOf('=', StringComparison.Ordinal);

            if (equals > 0)
            {
                name = argument[..equals];
                value = argument[(equals + 1)..];
            }

            if (!_optionNames.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"'{name}' is not an option. The options are {string.Join(", ", _optionNames[..^1])} and --dry-run.");

                continue;
            }

            if (value is null)
            {
                if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    problems.Add($"'{name}' needs a value.");

                    continue;
                }

                value = args[++index];
            }

            if (!options.TryAdd(name, value))
            {
                problems.Add($"'{name}' is given twice; it takes one value.");
            }
        }

        if (positional.Count != 2)
        {
            problems.Add(
                $"Expected two names, <Features> and <Feature>, and found {positional.Count}. "
                + "The folder is the plural and the type is the singular: Widgets Widget.");
        }

        string? Option(string name) => options.TryGetValue(name, out string? value) ? value : null;

        return new Request(
            positional.Count > 0 ? positional[0] : null,
            positional.Count > 1 ? positional[1] : null,
            Option("--access"),
            Option("--deletion"),
            Option("--operations"),
            Option("--model"),
            Option("--concurrency"),
            Option("--fields"),
            Option("--root") ?? ".",
            dryRun);
    }

    internal static Spec? Check(Request request, string root, List<string> problems)
    {
        string features = request.Features ?? string.Empty;
        string type = request.Type ?? string.Empty;

        problems.AddRange(NameProblems(features, type));

        string access = request.Access ?? string.Empty;

        if (request.Access is null)
        {
            problems.Add(
                "--access is required and has no default. Say who may see a record: "
                + "--access owner (each caller sees their own) or --access shared (every authenticated caller sees all).");
        }
        else if (access is not ("owner" or "shared"))
        {
            problems.Add($"--access '{access}' is not a choice. It is owner or shared.");
        }

        string deletion = request.Deletion ?? string.Empty;

        if (access == "owner")
        {
            if (request.Deletion is null)
            {
                problems.Add(
                    "--deletion is required with --access owner and has no default. Say what the feature does "
                    + "with its rows when the account that owns them is deleted: --deletion delete or "
                    + "--deletion anonymise.");
            }
            else if (deletion is not ("delete" or "anonymise"))
            {
                problems.Add($"--deletion '{deletion}' is not a choice. It is delete or anonymise.");
            }
        }
        else if (access == "shared" && request.Deletion is not null)
        {
            problems.Add(
                "--deletion names the fate of user data and --access shared holds none: every authenticated "
                + "caller reaches the same rows, which no account owns. Leave --deletion out.");
        }

        string model = request.Model ?? "record";

        if (model is not ("record" or "aggregate"))
        {
            problems.Add($"--model '{model}' is not a choice. It is record or aggregate.");
        }

        string concurrency = request.Concurrency ?? "none";

        if (concurrency is not ("none" or "etag"))
        {
            problems.Add($"--concurrency '{concurrency}' is not a choice. It is none or etag.");
        }

        string[]? operations = ParseOperations(request.Operations, problems);
        var fields = request.Fields is null ? null : FieldGrammar.Parse(request.Fields, FieldProfile.Feature, problems);

        if (problems.Count > 0 || operations is null)
        {
            return null;
        }

        problems.AddRange(Contradictions(operations, model, concurrency));

        if (problems.Count > 0)
        {
            return null;
        }

        string prefix;
        bool tablePrefixes;

        try
        {
            prefix = ProductPrefix(root);
            tablePrefixes = TablePrefixes(root);
        }
        catch (InvalidOperationException exception)
        {
            problems.Add(exception.Message);

            return null;
        }

        string? reaction = null;
        bool hasApi = Directory.Exists(System.IO.Path.Combine(root, "Src", "Presentation", FolderOf(prefix, "Api")));

        if (access == "owner")
        {
            string policyPort = System.IO.Path.Combine(
                root, "Src", "Application", FolderOf(prefix, "Application.Core"), "Common", "Ports", "IDeletedAccountDataPolicy.cs");

            if (!File.Exists(policyPort))
            {
                string[] identityHosts = hasApi ? [FolderOf(prefix, "Api")] : [.. PresentationHosts(root, prefix)];

                reaction = identityHosts.Select(host => IdentityFeature(root, host)).FirstOrDefault(found => found is not null);

                if (reaction is null)
                {
                    problems.Add(
                        "This tree ships none of the account-deleted reaction, and it has neither the local accounts "
                        + "(Features/Auth) nor an external-identity mapping (Features/ExternalIdentity) to read whether "
                        + "an id still names an account, so the reaction's precondition has no authority to re-derive "
                        + "from. Scaffold the feature into a tree this template generated, or write an "
                        + "IUserAccountDirectory adapter by hand first.");
                }
            }
        }

        return new Spec(prefix, features, type, access, deletion, operations, model, concurrency, reaction, hasApi, fields, tablePrefixes);
    }

    private static string? IdentityFeature(string root, string host)
    {
        string features = System.IO.Path.Combine(root, "Src", "Presentation", host, "Features");

        return Directory.Exists(System.IO.Path.Combine(features, "Auth")) ? "Auth"
            : Directory.Exists(System.IO.Path.Combine(features, "ExternalIdentity")) ? "ExternalIdentity"
            : null;
    }

    private static string[]? ParseOperations(string? text, List<string> problems)
    {
        if (text is null)
        {
            return _operationNames;
        }

        string[] named = [.. text.Split(',', StringSplitOptions.TrimEntries)];
        bool valid = true;

        if (named.Any(name => name.Length == 0))
        {
            problems.Add("--operations has an empty entry. It is a comma-separated list: get,list,create,update,delete.");
            valid = false;
        }

        foreach (string name in named.Where(name => name.Length > 0 && !_operationNames.Contains(name, StringComparer.Ordinal)))
        {
            problems.Add($"--operations '{name}' is not an operation. They are {string.Join(", ", _operationNames)}.");
            valid = false;
        }

        foreach (string name in named.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            problems.Add($"--operations names '{name}' twice.");
            valid = false;
        }

        return valid ? [.. _operationNames.Where(name => named.Contains(name, StringComparer.Ordinal))] : null;
    }

    internal static List<string> Contradictions(string[] operations, string model, string concurrency)
    {
        var problems = new List<string>();

        bool Has(string operation) => operations.Contains(operation, StringComparer.Ordinal);

        bool writes = Has("create") || Has("update") || Has("delete");
        bool changes = Has("update") || Has("delete");

        if (Has("create") && !Has("get"))
        {
            problems.Add(
                "create needs get: a created resource answers 201 with a Location, and that Location is "
                + "the route of the get operation. Add get to --operations, or leave create out.");
        }

        if (concurrency == "etag" && changes && !Has("get"))
        {
            problems.Add(
                "--concurrency etag needs get: a caller conditions an update or a delete on the ETag it "
                + "read, and only get publishes it from a fresh read. Add get to --operations.");
        }

        if (model == "record" && concurrency == "etag" && !changes)
        {
            problems.Add(
                "--concurrency etag guards update and delete and these operations select neither, so the "
                + "version would be stored and published for nothing. Add update or delete, or use --concurrency none.");
        }

        if (model == "aggregate" && concurrency == "none")
        {
            problems.Add(
                "--model aggregate needs --concurrency etag. An aggregate is loaded whole and written whole, "
                + "so a write that overwrites a newer one can break a rule each write kept alone, and the "
                + "tracker that writes it refuses to work without a version. Pass --concurrency etag, or "
                + "--model record if last write wins is what the feature wants.");
        }

        if (model == "aggregate" && !writes)
        {
            problems.Add(
                "--model aggregate needs at least one of create, update or delete: a feature that only reads "
                + "has no state to protect, which is what an aggregate is for. Use --model record.");
        }

        return problems;
    }

    private static List<string> NameProblems(string features, string type)
    {
        var problems = new List<string>();

        if (!CSharpNames.PascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a folder name: <Features> is PascalCase and plural, e.g. Widgets.");
        }

        if (!CSharpNames.PascalCase.IsMatch(type))
        {
            problems.Add($"'{type}' is not a type name: <Feature> is PascalCase and singular, e.g. Widget.");
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        if (string.Equals(features, type, StringComparison.Ordinal))
        {
            problems.Add(
                $"<Features> and <Feature> are both '{features}'. The folder is the plural and the "
                + "type is the singular, Widgets and Widget, because the layout rules read them apart.");
        }

        if (features.Length > 40 || type.Length > 40)
        {
            problems.Add("Names longer than 40 characters make a path that some tools cannot open. Shorten them.");
        }

        foreach (string name in new[] { features, type }.Distinct(StringComparer.Ordinal))
        {
            if (_reservedNames.Contains(name, StringComparer.Ordinal))
            {
                problems.Add(
                    $"'{name}' is already a segment of every namespace in this tree "
                    + $"({string.Join(", ", _reservedNames.Take(6))}, ...). Choose a name that is not one.");
            }
        }

        if (CSharpNames.Keywords.Contains(CSharpNames.Lower(type), StringComparer.Ordinal))
        {
            problems.Add(
                $"'{type}' becomes the C# keyword '{CSharpNames.Lower(type)}' once its first letter is lowered, which is "
                + "how the generated parameters are named. Choose another name.");
        }

        return problems;
    }

    internal static string ProductPrefix(string root)
    {
        string projectFile = System.IO.Path.Combine(root, "cleanarch.json");

        if (!File.Exists(projectFile))
        {
            throw new InvalidOperationException(
                $"No cleanarch.json in '{root}', the file that records the project's name. Run this from the root of a "
                + "tree generated by this template, or name that root with --root.");
        }

        using var document = JsonDocument.Parse(File.ReadAllText(projectFile));

        return document.RootElement.TryGetProperty("name", out JsonElement name) && name.GetString() is { Length: > 0 } prefix
            ? prefix
            : throw new InvalidOperationException($"'{projectFile}' records no name.");
    }

    private static bool TablePrefixes(string root)
    {
        string projectFile = System.IO.Path.Combine(root, "cleanarch.json");

        using var document = JsonDocument.Parse(File.ReadAllText(projectFile));

        string? database = document.RootElement.TryGetProperty("options", out JsonElement options)
            && options.TryGetProperty("database", out JsonElement recorded)
                ? recorded.GetString()
                : null;

        return database switch
        {
            "postgresql" or "sqlserver" => false,
            "sqlite" => true,
            _ => throw new InvalidOperationException(
                $"'{projectFile}' records no options.database the generator knows (postgresql, sqlserver or sqlite), "
                + "and the feature's table test depends on it: the provider decides whether a schema holds the table or prefixes its name."),
        };
    }

    private static bool IsVowel(char letter) => "aeiou".Contains(char.ToLowerInvariant(letter), StringComparison.Ordinal);

    private static string Kebab(string pascal) => Separated(pascal, '-');

    internal static string Snake(string pascal) => Separated(pascal, '_');

    internal static string Separated(string pascal, char separator) =>
        string.Concat(pascal.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? separator + char.ToLowerInvariant(character).ToString()
                : char.ToLowerInvariant(character).ToString()));

    private static IEnumerable<string> PresentationHosts(string root, string prefix)
    {
        string presentation = System.IO.Path.Combine(root, "Src", "Presentation");

        return Directory.Exists(presentation)
            ? Directory.GetDirectories(presentation)
                .Where(folder => File.Exists(System.IO.Path.Combine(folder, "Program.cs")))
                .Where(folder => !IsClientPresentation(folder))
                .Select(System.IO.Path.GetFileName)
                .OfType<string>()
                .Where(host => host != FolderOf(prefix, "Api"))
                .Order(StringComparer.Ordinal)
            : [];
    }

    private static string[] ComposingHosts(string root, Spec spec) =>
        spec.HasApi
            ? [.. PresentationHosts(root, spec.Prefix).Prepend(FolderOf(spec.Prefix, "Api"))]
            : [.. PresentationHosts(root, spec.Prefix)];

    private static string JobsHost(string root, string prefix)
    {
        string presentation = System.IO.Path.Combine(root, "Src", "Presentation");

        return (Directory.Exists(presentation)
            ? Directory.GetDirectories(presentation)
                .Where(folder => File.Exists(System.IO.Path.Combine(folder, "Features", "BackgroundJobs", "Jobs", "BackgroundJobsModule.cs")))
                .Select(System.IO.Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
                .FirstOrDefault()
            : null) ?? FolderOf(prefix, "Api");
    }

    private static bool IsClientPresentation(string folder) =>
        Directory.EnumerateFiles(folder, "*.csproj")
            .Any(project => File.ReadAllText(project).Contains(".Presentation.Client.csproj", StringComparison.Ordinal));

    private static string HostComposition(string root, string host, string adapter)
    {
        string folder = System.IO.Path.Combine(root, "Src", "Presentation", host);
        string module = adapter == "Auth" ? "AddAuthModule(" : "AddExternalIdentityPersistence(";

        string? found = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(file => File.ReadAllText(file).Contains(module, StringComparison.Ordinal));

        return System.IO.Path.GetRelativePath(root, found ?? System.IO.Path.Combine(folder, "Program.cs")).Replace('\\', '/');
    }

    private static string ProjectOf(Spec spec, string layer, string project) =>
        $"{layer}/{FolderOf(spec.Prefix, project)}";

    private static string FolderOf(string prefix, string project) => $"{Initials(prefix)}.{project}";

    internal static string ProjectInFolder(string prefix, string folder) => prefix + folder[Initials(prefix).Length..];

    private static string Initials(string prefix) => string.Concat(prefix.Split('.').Select(SegmentInitials));

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));

    internal static List<Emission> Plan(Spec spec, string root, out List<string> skippedTestProjects)
    {
        var flags = Flags(spec);
        var tokens = Tokens(spec);
        var emissions = new List<Emission>();
        var skipped = new List<string>();

        void Add(string project, string relative, string template, string when = "", Dictionary<string, string>? extra = null)
        {
            if (when.Length > 0 && !Template.Evaluate(when, flags))
            {
                return;
            }

            var all = extra is null ? tokens : tokens.Concat(extra).ToDictionary(pair => pair.Key, pair => pair.Value);

            emissions.Add(new Emission(
                $"{project}/{Template.Substitute(relative, all)}",
                Template.Render(template, flags, all)));
        }

        void AddTest(string project, string relative, string template, string when = "", Dictionary<string, string>? extra = null)
        {
            if (!Directory.Exists(System.IO.Path.Combine(root, project)))
            {
                if (!skipped.Contains(project, StringComparer.Ordinal))
                {
                    skipped.Add(project);
                }

                return;
            }

            Add(project, relative, template, when, extra);
        }

        string domain = ProjectOf(spec, "Src/Domain", "Domain");
        string application = ProjectOf(spec, "Src/Application", "Application");
        string applicationCore = ProjectOf(spec, "Src/Application", "Application.Core");
        string persistence = ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence");
        string api = ProjectOf(spec, "Src/Presentation", "Api");

        string applicationTests = ProjectOf(spec, "Tests/Application", "Application.UnitTests");
        string domainTests = ProjectOf(spec, "Tests/Domain", "Domain.UnitTests");
        string persistenceTests = ProjectOf(spec, "Tests/Infrastructure", "Infrastructure.Persistence.UnitTests");

        string feature = "Features/%F%";

        if (spec.Reaction is { } adapter)
        {
            Add(applicationCore, "Common/Ports/IDeletedAccountDataPolicy.cs", Templates.DeletedAccountDataPolicy);
            Add(applicationCore, "Common/Ports/IUserAccountDirectory.cs", Templates.UserAccountDirectory);
            Add(applicationCore, "Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountData.cs", Templates.SweepUseCase);
            string jobsHost = JobsHost(root, spec.Prefix);
            var jobs = new Dictionary<string, string>(StringComparer.Ordinal) { ["J"] = ProjectInFolder(spec.Prefix, jobsHost) };

            Add($"Src/Presentation/{jobsHost}", "Features/BackgroundJobs/Jobs/DeletedAccountSweepJobsExtensions.cs", Templates.DeletedAccountSweepJobs, extra: jobs);
            Add($"Src/Presentation/{jobsHost}", "Features/BackgroundJobs/Jobs/DeletedAccountSweepOptions.cs", Templates.DeletedAccountSweepOptions, extra: jobs);

            foreach (string host in ComposingHosts(root, spec))
            {
                var named = new Dictionary<string, string>(StringComparer.Ordinal) { ["H"] = ProjectInFolder(spec.Prefix, host) };

                if (adapter == "Auth")
                {
                    Add($"Src/Presentation/{host}", "Features/Auth/Composition/AccountUserAccountDirectory.cs", Templates.LocalUserAccountDirectory, extra: named);
                }
                else
                {
                    Add($"Src/Presentation/{host}", "Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory.cs", Templates.ExternalUserAccountDirectory, extra: named);
                }
            }
        }

        Add(domain, $"{feature}/Model/Entities/%A%.cs", Templates.Aggregate, "agg");
        Add(domain, $"{feature}/Repositories/I%A%Repository.cs", Templates.RepositoryPort, "agg&wq");

        Add(application, $"{feature}/Model/Dtos/%A%ContentDto.cs", Templates.ContentDto, "rec&content");
        Add(application, $"{feature}/Model/Dtos/%A%Dto.cs", Templates.Dto, "dto");
        Add(application, $"{feature}/Model/Dtos/%A%SummaryDto.cs", Templates.SummaryDto, "list");
        Add(application, $"{feature}/Model/Dtos/%A%ListQueryDto.cs", Templates.ListQuery, "rec&list");
        Add(application, $"{feature}/Model/Errors/%A%Errors.cs", Templates.Errors, "notfound");
        Add(application, $"{feature}/Model/Policies/%A%CollectionPolicy.cs", Templates.CollectionPolicy, "list");
        Add(application, $"{feature}/Model/Policies/%A%InputPolicy.cs", Templates.InputPolicy, "rec&policy");
        Add(application, $"{feature}/Ports/%A%Queries/I%A%Queries.cs", Templates.QueriesPort, "rq");
        Add(application, $"{feature}/Ports/%A%Queries/%A%Filter.cs", Templates.Filter, "list");
        Add(application, $"{feature}/Ports/%A%Store/I%A%Store.cs", Templates.StorePort, "rec&stage");
        Add(application, $"{feature}/Ports/%A%Store/%A%ChangeStatus.cs", Templates.ChangeStatus, "rec&change");

        Add(application, $"{feature}/Services/I%A%Service.cs", Templates.ServicePort, "rec");
        Add(application, $"{feature}/Services/%A%Service.cs", Templates.Service, "rec");
        Add(application, $"{feature}/Model/Dtos/%A%ContentDtoValidator.cs", Templates.ContentValidator, "rec&content");

        Add(application, $"{feature}/Model/Mapping/%A%DtoMapping.cs", Templates.DtoMapping, "agg&content");
        Add(application, $"{feature}/Services/I%A%Service.cs", Templates.AccessServicePort, "agg&change");
        Add(application, $"{feature}/Services/%A%Service.cs", Templates.AccessService, "agg&change");
        string queries = $"{feature}/UseCases/Queries";
        string commands = $"{feature}/UseCases/Commands";

        Add(application, $"{queries}/Get%A%/Get%A%.cs", Templates.GetUseCase, "agg&get");
        Add(application, $"{queries}/Get%A%/Get%A%QueryValidator.cs", Templates.GetQueryValidator, "agg&get");
        Add(application, $"{queries}/Get%F%/Get%F%.cs", Templates.ListUseCase, "agg&list");
        Add(application, $"{commands}/Create%A%/Create%A%.cs", Templates.CreateUseCase, "agg&create");
        Add(application, $"{commands}/Create%A%/Create%A%CommandValidator.cs", Templates.CreateCommandValidator, "agg&create");
        Add(application, $"{commands}/Update%A%/Update%A%.cs", Templates.UpdateUseCase, "agg&update");
        Add(application, $"{commands}/Update%A%/Update%A%CommandValidator.cs", Templates.UpdateCommandValidator, "agg&update");
        Add(application, $"{commands}/Delete%A%/Delete%A%.cs", Templates.DeleteUseCase, "agg&delete");
        Add(application, $"{commands}/Delete%A%/Delete%A%CommandValidator.cs", Templates.DeleteCommandValidator, "agg&delete");

        Add(application, $"{feature}/Services/Consumers/%Verb%%A%OnAccountDeletedConsumer.cs", Templates.AccountDeletedConsumer, "owner");

        Add(persistence, $"{feature}/Rows/Models/%A%Record.cs", Templates.RecordPlain, "rec");
        Add(persistence, $"{feature}/Rows/Models/%A%Record.cs", Templates.RecordAudited, "agg");
        Add(persistence, $"{feature}/Rows/Configurations/%A%RecordConfiguration.cs", Templates.Configuration);
        Add(persistence, $"{feature}/Access/Stores/%A%Store.cs", Templates.Store, "rec&stage");
        Add(persistence, $"{feature}/Rows/Mapping/I%A%Mapper.cs", Templates.MapperPort, "agg");
        Add(persistence, $"{feature}/Rows/Mapping/%A%Mapper.cs", Templates.Mapper, "agg");
        Add(persistence, $"{feature}/Rows/Tracking/I%A%Tracker.cs", Templates.TrackerPort, "agg");
        Add(persistence, $"{feature}/Rows/Tracking/%A%Tracker.cs", Templates.Tracker, "agg");
        Add(persistence, $"{feature}/Access/Repositories/%A%Repository.cs", Templates.Repository, "agg&wq");
        Add(persistence, $"{feature}/Access/Queries/%A%Queries.cs", Templates.Queries, "rq");
        Add(persistence, $"{feature}/Access/Queries/%A%SortMap.cs", Templates.SortMap, "list&!fields");
        Add(persistence, $"{feature}/Access/Queries/%A%SortMap.cs", Templates.FieldSortMap, "list&fields");

        if (spec.HasApi)
        {
            Add(api, $"{feature}/Contracts/Requests/Create%A%Request.cs", Templates.CreateRequest, "create");
            Add(api, $"{feature}/Contracts/Requests/Update%A%Request.cs", Templates.UpdateRequest, "update");
            Add(api, $"{feature}/Contracts/Requests/Get%F%Request.cs", Templates.ListRequest, "list");
            Add(api, $"{feature}/Contracts/Responses/%A%Response.cs", Templates.Response, "dto");
            Add(api, $"{feature}/Contracts/Responses/%A%SummaryResponse.cs", Templates.SummaryResponse, "list");
            Add(api, $"{feature}/Controllers/%F%Controller.cs", Templates.Controller);
            Add(api, $"{feature}/Contracts/Mapping/%A%ResponseMapping.cs", Templates.ResponseMapping, "dto|list");
        }

        AddTest(applicationTests, $"{feature}/Model/Dtos/%A%ContentDtoValidatorTests.cs", Templates.ContentValidatorTests, "rec&content&!fields");
        AddTest(applicationTests, $"{feature}/Model/Dtos/%A%ContentDtoValidatorTests.cs", Templates.FieldContentValidatorTests, "rec&content&fields");
        AddTest(applicationTests, $"{feature}/Services/%A%ServiceTests.cs", Templates.ServiceTests, "rec");
        foreach (string operation in spec.Fields is null ? [] : new[] { "Create", "Update" })
        {
            var fields = spec.Fields!;
            string message = $"{operation}{spec.Type}Command";

            AddTest(
                applicationTests,
                $"{feature}/UseCases/Commands/{operation}%A%/{operation}%A%CommandValidatorTests.cs",
                Templates.FieldCommandValidatorTests,
                $"agg&{operation.ToLowerInvariant()}",
                new()
                {
                    ["Op"] = operation,
                    ["FValidatorTests"] = FieldCode.ValidatorTests(fields, message, "AValidCommand", spec.Type),
                    ["FValidCommand"] = FieldCode.Construction(
                        operation == "Create" ? fields.Select(field => FieldCode.Sample(field, 1)) : fields.Select(field => FieldCode.Sample(field, 1)).Prepend("Guid.CreateVersion7()")),
                });
        }

        AddTest(
            applicationTests,
            $"{feature}/UseCases/Commands/Create%A%/Create%A%CommandValidatorTests.cs",
            Templates.CommandValidatorTests,
            "agg&create&!fields",
            new() { ["Op"] = "Create", ["CommandNew"] = "new Create%A%Command(name!, description!)" });
        AddTest(
            applicationTests,
            $"{feature}/UseCases/Commands/Update%A%/Update%A%CommandValidatorTests.cs",
            Templates.CommandValidatorTests,
            "agg&update&!fields",
            new() { ["Op"] = "Update", ["CommandNew"] = "new Update%A%Command(Guid.CreateVersion7(), name!, description!)" });
        AddTest(domainTests, $"{feature}/Model/Entities/%A%Tests.cs", Templates.AggregateTests, "agg&!fields");
        AddTest(domainTests, $"{feature}/Model/Entities/%A%Tests.cs", Templates.FieldAggregateTests, "agg&fields");
        AddTest(persistenceTests, $"{feature}/Rows/Configurations/%A%RecordConfigurationTests.cs", Templates.ConfigurationTests);
        AddTest(persistenceTests, $"{feature}/%A%MapperFidelityTests.cs", Templates.MapperFidelityTests, "agg&!fields");
        AddTest(persistenceTests, $"{feature}/%A%MapperFidelityTests.cs", Templates.FieldMapperFidelityTests, "agg&fields");

        if (spec.HasApi && spec.Fields is not null && spec.Has("create"))
        {
            AddTest(IntegrationTests(spec), $"{feature}/%A%FieldTests.cs", Templates.FieldApiTests);
        }

        skippedTestProjects = skipped;

        return emissions;
    }

    private static HashSet<string> Flags(Spec spec)
    {
        bool record = spec.Model == "record";
        var flags = new HashSet<string>(spec.Operations, StringComparer.Ordinal)
        {
            spec.Access,
            record ? "rec" : "agg",
        };

        void When(bool condition, string flag)
        {
            if (condition)
            {
                flags.Add(flag);
            }
        }

        bool get = spec.Has("get");
        bool list = spec.Has("list");
        bool create = spec.Has("create");
        bool update = spec.Has("update");
        bool delete = spec.Has("delete");
        bool owner = spec.Access == "owner";

        When(spec.Concurrency == "etag", "etag");
        When(spec.Deletion == "delete", "purge");
        When(spec.Deletion == "anonymise", "anon");

        When(create || update || delete, "wq");
        When(update || delete, "change");
        When(create || update, "content");

        When(get || create || update, "dto");

        When(get || (record && (create || update)), "single");
        When(flags.Contains("single") || list || owner, "rq");

        When(record && (create || update || delete || owner), "stage");

        When(get || update || delete || (record && create), "notfound");
        When(record && (create || update), "clock");

        var fields = spec.Fields;

        When(fields is not null, "fields");
        When(spec.Deletion == "anonymise" && (fields is null || fields.Any(field => field is { Kind: FieldKind.String, Nullable: false })), "anontext");
        When(flags.Contains("content") && (fields is null || fields.Any(field => field.Kind == FieldKind.String)), "trim");
        When(fields is null || fields.Any(FieldCode.HasConstants), "policy");
        When(spec.TablePrefixes, "prefixed");

        return flags;
    }

    private static Dictionary<string, string> Tokens(Spec spec)
    {
        bool record = spec.Model == "record";
        bool owner = spec.Access == "owner";
        bool etag = spec.Concurrency == "etag";

        string a = spec.Type;
        string f = spec.Features;
        string l = CSharpNames.Lower(a);

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["P"] = spec.Prefix,
            ["LowerP"] = spec.Prefix.ToLowerInvariant(),
            ["F"] = f,
            ["A"] = a,
            ["L"] = l,
            ["R"] = Kebab(f),
            ["S"] = Snake(f),
            ["ReadDto"] = etag ? $"Versioned<{a}Dto>" : $"{a}Dto",
            ["ReadResponse"] = etag ? $"Versioned<{a}Response>" : $"{a}Response",
            ["OwnerClause"] = owner ? " && record.OwnerId == ownerId.Value" : string.Empty,
            ["Limits"] = record ? $"{a}InputPolicy" : a,
            ["ListQuery"] = owner ? "GetForOwnerAsync" : "GetPageAsync",
            ["ListInput"] = record ? $"{a}ListQueryDto" : $"Get{f}Query",
            ["ListInputNamespace"] = record
                ? $"{spec.Prefix}.Application.Features.{f}.Model.Dtos"
                : $"{spec.Prefix}.Application.Features.{f}.UseCases.Queries.Get{f}",
            ["SvcOwner"] = owner ? "userId.Value, " : string.Empty,
            ["Implements"] = owner ? ", IOwnedAggregate" : string.Empty,
            ["Load"] = owner ? "LoadOwnedAsync" : "LoadAsync",
            ["Precondition"] = etag ? ", precondition" : string.Empty,
            ["Scope"] = owner ? "caller's own " : string.Empty,
            ["a"] = IsVowel(l[0]) ? "an" : "a",
            ["An"] = IsVowel(l[0]) ? "An" : "A",
            ["OwnerArg"] = owner ? "ownerId, " : string.Empty,
            ["OwnerIdArg"] = owner ? "_ownerId, " : string.Empty,
            ["NullPrecondition"] = etag ? ", null" : string.Empty,
            ["AnyOwner"] = owner ? "Arg.Any<UserId>(), " : string.Empty,
            ["AnyPrecondition"] = etag ? "Arg.Any<VersionPrecondition?>(), " : string.Empty,
            ["Verb"] = spec.Deletion == "anonymise" ? "Anonymise" : "Delete",
            ["Fate"] = spec.Deletion == "anonymise" ? "anonymised" : "deleted",
            ["Stage"] = spec.Deletion == "anonymise" ? "StageAnonymisationForOwnerAsync" : "StageRemovalForOwnerAsync",
        };

        string[] serviceArguments =
        [
            .. Flags(spec).Contains("rq") ? ["_queries"] : Array.Empty<string>(),
            .. Flags(spec).Contains("wq") ? ["_store", "_unitOfWork"] : Array.Empty<string>(),
            .. Flags(spec).Contains("clock") ? ["TheClock.Start()"] : Array.Empty<string>(),
            .. Flags(spec).Contains("content") ? [$"new {a}ContentDtoValidator()"] : Array.Empty<string>(),
            "currentUser",
        ];

        tokens["ServiceArgs"] = string.Join(", ", serviceArguments);

        tokens["CtorParams"] = record ? $"I{a}Service service" : "ISender sender";

        string pre = etag ? ", precondition" : string.Empty;
        string content = spec.Fields is { } requested
            ? string.Join(", ", requested.Select(field => $"request.{field.Name}"))
            : "request.Name, request.Description";

        tokens["CallList"] = record
            ? "service.ListAsync(query, cancellationToken)"
            : "sender.Send(query, cancellationToken)";
        tokens["CallGet"] = record
            ? $"service.GetAsync({l}Id, cancellationToken)"
            : $"sender.Send(new Get{a}Query({l}Id), cancellationToken)";
        tokens["CallCreate"] = record
            ? $"service.CreateAsync(new {a}ContentDto({content}), cancellationToken)"
            : $"sender.Send(new Create{a}Command({content}), cancellationToken)";
        tokens["CallUpdate"] = record
            ? $"service.UpdateAsync({l}Id, new {a}ContentDto({content}){pre}, cancellationToken)"
            : $"sender.Send(new Update{a}Command({l}Id, {content}{pre}), cancellationToken)";
        tokens["CallDelete"] = record
            ? $"service.DeleteAsync({l}Id{pre}, cancellationToken)"
            : $"sender.Send(new Delete{a}Command({l}Id{pre}), cancellationToken)";

        tokens["Staged"] = Flags(spec).Contains("trim") ? "Trimmed(content)" : "content";
        tokens["Replaced"] = spec.Fields is null ? "name and description" : "fields";
        tokens["Revised"] = spec.Fields is null ? "text" : "fields";
        tokens["ListTail"] = spec.Fields is null ? ", names only"
            : spec.Fields.Any(field => !FieldCode.InSummary(field)) ? ", without their texts" : string.Empty;
        tokens["SummaryDoc"] = spec.Fields is null ? ", without its description"
            : spec.Fields.Any(field => !FieldCode.InSummary(field)) ? ", without its texts" : string.Empty;
        tokens["SortDoc"] = string.Join(
            ", ",
            (spec.Fields?.Where(FieldCode.IsSortable).Select(field => CSharpNames.Lower(field.Name)) ?? ["name"])
                .Append("createdAt")
                .Select(field => $"<c>{field}</c>"));
        tokens["DateKeys"] = string.Join(
            ", ",
            (spec.Fields ?? [])
                .Where(field => FieldCode.IsKeyset(field) && field.Kind == FieldKind.DateTimeOffset)
                .Select(field => $"{a}CollectionPolicy.{field.Name}Field")
                .Prepend($"{a}CollectionPolicy.CreatedAtField"));

        if (spec.Fields is { } fields)
        {
            FieldTokens(tokens, spec, fields);
        }

        return tokens;
    }

    private static void FieldTokens(Dictionary<string, string> tokens, Spec spec, IReadOnlyList<Field> fields)
    {
        var flags = Flags(spec);
        bool record = spec.Model == "record";
        bool owner = spec.Access == "owner";
        string a = spec.Type;
        string limits = record ? $"{a}InputPolicy" : a;
        var summary = fields.Where(FieldCode.InSummary).ToList();
        var sortable = fields.Where(FieldCode.IsSortable).ToList();
        var keyset = sortable.Where(FieldCode.IsKeyset).ToList();
        var anonymised = fields.Where(field => FieldCode.Anonymised(field) is not null).ToList();

        string Declared(Field field) => $"{FieldCode.Type(field)} {FieldCode.Parameter(field)}";

        string Setter(Field field) =>
            flags.Contains("update") || (flags.Contains("anon") && FieldCode.Anonymised(field) is not null) ? "{ get; private set; }" : "{ get; }";

        string Required(Field field) =>
            FieldCode.HasRequire(field) ? $"Require{field.Name}({FieldCode.Parameter(field)})" : FieldCode.Parameter(field);

        tokens["FConstants"] = FieldCode.Constants(fields, ceilings: !record && flags.Contains("content"));
        tokens["FPolicyConstants"] = FieldCode.Constants(fields, ceilings: false);
        tokens["FCtorParams"] = FieldCode.List(fields.Select(Declared), "        ", ") : base(id)");
        tokens["FAssign"] = FieldCode.Joined(fields.Select(field => $"        {field.Name} = {FieldCode.Parameter(field)};"));
        tokens["FProperties"] = FieldCode.Joined(fields.Select(field => $"    public {FieldCode.Type(field)} {field.Name} {Setter(field)}"), "\n\n");
        tokens["FCreateParams"] = FieldCode.List(fields.Select(Declared), "        ", ") =>");
        tokens["FCreateArgs"] = FieldCode.List(fields.Select(Required), "            ", ");");
        tokens["FRehydrateParams"] = FieldCode.List(fields.Select(Declared), "        ", ")");
        tokens["FRehydrateArgs"] = FieldCode.List(fields.Select(FieldCode.Parameter), "            ", ");");
        tokens["FUpdateParams"] = string.Join(", ", fields.Select(Declared));
        tokens["FUpdateBody"] = FieldCode.Joined(
        [
            .. fields.Where(FieldCode.HasRequire).Select(field => $"        {FieldCode.Type(field)} valid{field.Name} = {Required(field)};"),
            string.Empty,
            .. fields.Select(field => $"        {field.Name} = {(FieldCode.HasRequire(field) ? "valid" + field.Name : FieldCode.Parameter(field))};"),
        ]);
        tokens["FAnonymise"] = FieldCode.Joined(anonymised.Select(field => $"        {field.Name} = {FieldCode.Anonymised(field)};"));
        tokens["FRequire"] = FieldCode.Joined(fields.Where(FieldCode.HasRequire).Select(FieldCode.Require), "\n\n");
        tokens["FRulesCommand"] = FieldCode.Rules(fields, "command", a);
        tokens["FRulesContent"] = FieldCode.Rules(fields, "content", limits);
        tokens["FParamList"] = FieldCode.List(fields.Select(field => $"{FieldCode.Type(field)} {field.Name}"), "    ");
        tokens["FDtoParams"] = FieldCode.Joined(fields.Select(field => $"    {FieldCode.Type(field)} {field.Name},"));
        tokens["FSummaryParams"] = FieldCode.Joined(summary.Select(field => $"    {FieldCode.Type(field)} {field.Name},"));
        tokens["FCommandArgs"] = string.Join(", ", fields.Select(field => $"command.{field.Name}"));
        tokens["FDetailArgs"] = FieldCode.Joined(fields.Select(field => $"            %L%.{field.Name},"));
        tokens["FRecordProperties"] = FieldCode.Joined(fields.Select(FieldCode.RecordProperty), "\n\n");
        tokens["FConfiguration"] = FieldCode.Joined(fields.Select(field => FieldCode.Configuration(field, limits)), "\n\n");
        tokens["FIndexes"] = FieldCode.Joined(sortable.Select(field => FieldCode.SortIndex(spec.Features, field, owner)), "\n\n");
        tokens["FStoreAssign"] = FieldCode.Joined(fields.Select(field => $"            {field.Name} = {FieldCode.Store(field, "content")},"));
        tokens["FStoreUpdate"] = FieldCode.Joined(fields.Select(field => $"            record.{field.Name} = {FieldCode.Store(field, "content")};"));
        tokens["FStoreAnonymise"] = FieldCode.Joined(anonymised.Select(field => $"            record.{field.Name} = {FieldCode.Anonymised(field)};"));
        tokens["FMapperRehydrate"] = FieldCode.List(fields.Select(field => $"record.{field.Name}"), "            ", ");");
        tokens["FMapperNew"] = FieldCode.Joined(fields.Select(field => $"            {field.Name} = aggregate.{field.Name},"));
        tokens["FMapperWrite"] = FieldCode.Joined(fields.Select(field => $"        record.{field.Name} = aggregate.{field.Name};"));
        tokens["FRecordArgs"] = string.Join(", ", fields.Select(field => $"record.{field.Name}"));
        tokens["FRecordArgLines"] = FieldCode.Joined(fields.Select(field => $"                record.{field.Name},"));
        tokens["FSummaryArgs"] = string.Concat(summary.Select(field => $"record.{field.Name}, "));
        tokens["FResponseArgs"] = string.Concat(fields.Select(field => $"%L%.{field.Name}, "));
        tokens["FSummaryResponseArgs"] = string.Concat(summary.Select(field => $"summary.{field.Name}, "));
        tokens["FRequestDocs"] = FieldCode.Joined(fields.Select(FieldCode.WireDocumentation));
        tokens["FRequestParams"] = FieldCode.List(fields.Select(FieldCode.WireParameter), "    ");
        tokens["FTrimmed"] = string.Join(
            ", ",
            fields.Where(field => field.Kind == FieldKind.String)
                .Select(field => $"{field.Name} = content.{field.Name}{(field.Nullable ? "?" : string.Empty)}.Trim()"));
        tokens["FPolicyFields"] = FieldCode.Joined(sortable.Select(field => $"    public const string {field.Name}Field = \"{CSharpNames.Lower(field.Name)}\";"), "\n\n");
        tokens["FSortables"] = FieldCode.Joined(sortable.Select(field =>
            $"        SortableField.{(FieldCode.IsKeyset(field) ? "Keyset" : "OffsetOnly")}({field.Name}Field),"));
        tokens["FKeysetCases"] = FieldCode.Joined(keyset.Select(field =>
            $"            %A%CollectionPolicy.{field.Name}Field => Apply{field.Name}Keyset(source, term.Direction, cursor),"));
        tokens["FKeyCases"] = FieldCode.Joined(keyset.Select(field => field.Kind == FieldKind.String
            ? $"            %A%CollectionPolicy.{field.Name}Field => summary.{field.Name},"
            : $"            %A%CollectionPolicy.{field.Name}Field => summary.{field.Name}.ToString(\"O\", CultureInfo.InvariantCulture),"));
        tokens["FOrderCases"] = SortCases(sortable, "source", "OrderBy");
        tokens["FThenCases"] = SortCases(sortable, "source", "ThenBy");
        tokens["FKeysetMethods"] = FieldCode.Joined(keyset.Select(KeysetMethod), "\n\n");
        tokens["FValidContent"] = FieldCode.Construction(fields.Select(field => FieldCode.Sample(field, 1)));
        tokens["FServiceInvalid"] = fields.FirstOrDefault(field => FieldCode.Invalid(field, limits).Count > 0) is { } refused
            ? FieldCode.Joined(
            [
                "    [Fact]",
                "    public async Task CreateAsync_RejectsInvalidContent_AndStagesNothing()",
                "    {",
                $"        var result = await Service().CreateAsync(AValidContent with {{ {refused.Name} = {FieldCode.Invalid(refused, limits)[0].Value} }}, TestToken);",
                string.Empty,
                "        result.Error!.Type.ShouldBe(ErrorType.Validation);",
                "        _store.ReceivedCalls().ShouldBeEmpty();",
                "        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());",
                "    }",
            ])
            : string.Empty;
        tokens["FContentValidatorTests"] = FieldCode.ValidatorTests(fields, $"{a}ContentDto", "AValidContentDto", limits);
        tokens["FAggregateTests"] = FieldCode.AggregateTests(fields, flags.Contains("create"), flags.Contains("update"), "%OwnerIdArg%");
        tokens["FConfigurationTests"] = FieldCode.ConfigurationTests(fields, limits);
        tokens["FFidelityArgs"] = FieldCode.List(fields.Select(field => FieldCode.Sample(field, 1)), "            ", ");");
        tokens["FFidelityAsserts"] = FieldCode.Joined(fields.Select(field => FieldCode.Assertion("roundTripped", field, FieldCode.Sample(field, 1))));
        tokens["FRequestSample"] = FieldCode.Construction(fields.Select(field => FieldCode.Sample(field, 1)));
        tokens["FApiInstants"] = fields.Any(field => field.Kind == FieldKind.DateTimeOffset)
            ? FieldCode.Joined(
            [
                "    [Fact]",
                "    public async Task EveryDateTimeOffset_SentWithAnOffset_IsKeptAsTheSameInstant_InUtc()",
                "    {",
                "        var (client, _, _) = await SignInAsync();",
                "        var sent = new DateTimeOffset(2026, 3, 4, 7, 6, 7, TimeSpan.FromHours(2));",
                string.Empty,
                $"        using var response = await client.PostAsJsonAsync(_route, AValidRequest() with {{ {string.Join(", ", fields.Where(field => field.Kind == FieldKind.DateTimeOffset).Select(field => $"{field.Name} = sent"))} }}, TestToken);",
                string.Empty,
                "        response.StatusCode.ShouldBe(HttpStatusCode.Created);",
                string.Empty,
                "        var created = await ApiJson.ReadAsync<%A%Response>(response, TestToken);",
                string.Empty,
                .. fields.Where(field => field.Kind == FieldKind.DateTimeOffset).SelectMany(field => new[]
                {
                    $"        created.{field.Name}.ShouldBe(sent);",
                    $"        created.{field.Name}{(field.Nullable ? "!.Value" : string.Empty)}.Offset.ShouldBe(TimeSpan.Zero);",
                }),
                "    }",
            ])
            : string.Empty;
        tokens["FResponseAsserts"] = FieldCode.Joined(fields.Select(field => FieldCode.Assertion("created", field, FieldCode.Sample(field, 1))));
        tokens["FApiRefusal"] = fields
            .SelectMany(field => FieldCode.Invalid(field, null).Where(invalid => invalid.Case != "Missing").Select(invalid => (Field: field, invalid.Case, invalid.Value)))
            .Select(refusal => FieldCode.Joined(
            [
                "    [Fact]",
                $"    public async Task {refusal.Field.Name}{refusal.Case}_Answers400_WithTheValidationProblem()",
                "    {",
                "        var (client, _, _) = await SignInAsync();",
                string.Empty,
                $"        using var response = await client.PostAsJsonAsync(_route, AValidRequest() with {{ {refusal.Field.Name} = {refusal.Value} }}, TestToken);",
                string.Empty,
                "        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);",
                "        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe(\"request.validationFailed\");",
                "    }",
            ]))
            .FirstOrDefault() ?? string.Empty;
    }

    private static string SortCases(IEnumerable<Field> sortable, string source, string verb) =>
        FieldCode.Joined(sortable.Select(field => FieldCode.Joined(
        [
            $"            %A%CollectionPolicy.{field.Name}Field => term.Direction == SortDirection.Ascending",
            $"                ? {source}.{verb}(record => record.{field.Name})",
            $"                : {source}.{verb}Descending(record => record.{field.Name}),",
        ])));

    private static string KeysetMethod(Field field) =>
        field.Kind == FieldKind.String
            ? $$"""
                    private static IQueryable<%A%Record> Apply{{field.Name}}Keyset(
                        IQueryable<%A%Record> source,
                        SortDirection direction,
                        Cursor cursor) =>
                        direction == SortDirection.Ascending
                            ? source.Where(record =>
                                record.{{field.Name}}.CompareTo(cursor.Key) > 0
                                || (record.{{field.Name}} == cursor.Key && record.Id.CompareTo(cursor.Id) > 0))
                            : source.Where(record =>
                                record.{{field.Name}}.CompareTo(cursor.Key) < 0
                                || (record.{{field.Name}} == cursor.Key && record.Id.CompareTo(cursor.Id) < 0));
                """
            : $$"""
                    private static IQueryable<%A%Record> Apply{{field.Name}}Keyset(
                        IQueryable<%A%Record> source,
                        SortDirection direction,
                        Cursor cursor)
                    {
                        if (!DateTimeOffset.TryParse(
                            cursor.Key,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out DateTimeOffset parsed))
                        {
                            throw new InvalidOperationException(
                                "The cursor's key is not a valid date/time: CursorKeys should have rejected it first.");
                        }

                        var key = parsed.ToUniversalTime();

                        return direction == SortDirection.Ascending
                            ? source.Where(record =>
                                record.{{field.Name}}.CompareTo(key) > 0
                                || (record.{{field.Name}} == key && record.Id.CompareTo(cursor.Id) > 0))
                            : source.Where(record =>
                                record.{{field.Name}}.CompareTo(key) < 0
                                || (record.{{field.Name}} == key && record.Id.CompareTo(cursor.Id) < 0));
                    }
                """;

    private static List<string> Conflicts(string root, Spec spec, List<Emission> plan)
    {
        var problems = new List<string>();

        string persistence = ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence");

        string[] projects =
        [
            ProjectOf(spec, "Src/Application", "Application"),
            persistence,
            .. spec.HasApi ? [ProjectOf(spec, "Src/Presentation", "Api")] : Array.Empty<string>(),
            .. spec.Model == "aggregate" ? [ProjectOf(spec, "Src/Domain", "Domain")] : Array.Empty<string>(),
        ];

        foreach (string project in projects.Where(project => !Directory.Exists(System.IO.Path.Combine(root, project))))
        {
            problems.Add($"'{project}' does not exist under '{root}', so this is not a tree of this template.");
        }

        if (ComposingHosts(root, spec).Length == 0)
        {
            problems.Add(
                $"Neither '{ProjectOf(spec, "Src/Presentation", "Api")}' nor a presentation host with a Program.cs exists under "
                + $"'{root}', so no host would compose the feature and this is not a tree of this template.");
        }

        string context = System.IO.Path.Combine(root, persistence, "Common", "Contexts", "AppDbContext.cs");

        if (!File.Exists(context))
        {
            problems.Add($"'{System.IO.Path.GetRelativePath(root, context)}' does not exist, and the feature's table has to be added to it.");
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        string declared = File.ReadAllText(context);

        if (Regex.IsMatch(declared, $@"\b{spec.Features}Schema\b", RegexOptions.None, TimeSpan.FromSeconds(2))
            || Regex.IsMatch(declared, $@"\b{spec.Features}\s*=>", RegexOptions.None, TimeSpan.FromSeconds(2)))
        {
            problems.Add(
                $"AppDbContext already declares a member named '{spec.Features}' or '{spec.Features}Schema', "
                + "so a feature of that name exists or its name would collide. Choose another, or edit that feature.");
        }

        string[] featureFolders =
        [
            .. projects,
            ProjectOf(spec, "Tests/Application", "Application.UnitTests"),
            ProjectOf(spec, "Tests/Domain", "Domain.UnitTests"),
            ProjectOf(spec, "Tests/Infrastructure", "Infrastructure.Persistence.UnitTests"),
            ProjectOf(spec, "Tests/Presentation", "Api.UnitTests"),
        ];

        foreach (string folder in featureFolders
            .Select(project => System.IO.Path.Combine(project, "Features", spec.Features))
            .Where(folder => Directory.Exists(System.IO.Path.Combine(root, folder))))
        {
            problems.Add($"'{folder}' already exists. A feature is scaffolded once; after that it is edited.");
        }

        if (spec.Fields is { } fields)
        {
            problems.AddRange(FieldCode.Conflicts(
                fields,
                spec.Features,
                spec.Type,
                plan.Select(emission => System.IO.Path.GetFileNameWithoutExtension(emission.Path)),
                spec.Deletion,
                spec.Access == "owner",
                spec.Has("list")));
        }

        string manifest = ManifestPath(spec);

        if (File.Exists(System.IO.Path.Combine(root, manifest)))
        {
            problems.Add($"'{manifest}' already exists: a feature called {spec.Features} was generated here before.");
        }

        string[] existing = [.. plan
            .Select(emission => emission.Path)
            .Where(path => File.Exists(System.IO.Path.Combine(root, path)))];

        if (existing.Length > 0)
        {
            problems.Add(
                $"{existing.Length} of the files to write already exist, and none is ever overwritten:\n"
                + string.Join("\n", existing.Select(path => $"  {path}")));
        }

        return problems;
    }

    private static string ManifestPath(Spec spec) => FeatureManifest.PathOf(spec.Features);

    internal static string PagePath(string features) => $"docs/features/{Kebab(features)}.md";

    private static Emission? PageOf(string root, Spec spec, List<Emission> plan, out string? unwritten)
    {
        string path = PagePath(spec.Features);
        string template = System.IO.Path.Combine(root, _pageTemplate);

        if (File.Exists(System.IO.Path.Combine(root, path)))
        {
            unwritten = $"'{path}' already exists, so it was left as it is and the manifest does not record it: remove-feature never deletes it.";

            return null;
        }

        if (!File.Exists(template))
        {
            unwritten = $"'{_pageTemplate}' is not in this tree, so '{path}' was not written; write it by hand.";

            return null;
        }

        unwritten = null;

        return new Emission(path, PageText(File.ReadAllText(template), spec, plan));
    }

    private static string PageText(string template, Spec spec, List<Emission> plan)
    {
        string plural = Separated(spec.Features, ' ');
        string singular = Separated(spec.Type, ' ');
        string one = (IsVowel(singular[0]) ? "an " : "a ") + singular;
        string route = $"/api/v1/{Kebab(spec.Features)}";
        string item = $"{route}/{{{CSharpNames.Lower(spec.Type)}Id}}";

        string access = spec.Access == "owner"
            ? $"each authenticated caller, on their own {plural} only; once an account is deleted, its {plural} are "
                + (spec.Deletion == "anonymise" ? "anonymised" : "deleted")
            : $"every authenticated caller, on the same {plural}";

        string shape = (spec.Model == "record"
                ? "a service over a store, with no domain model"
                : $"one use case per operation over the {singular} aggregate")
            + (spec.Concurrency != "etag" ? ", and the last write wins"
                : spec.HasApi ? ", and a write sends If-Match with the ETag a read returned"
                : ", and a write carries the version a read returned");

        (string Operation, string Line)[] endpoints = spec.HasApi
            ?
            [
                ("list", $"- `GET {route}`: a page of {plural}"),
                ("get", $"- `GET {item}`: {one}, and `HEAD` for its headers alone"),
                ("create", $"- `POST {route}`: creates {one}"),
                ("update", $"- `PUT {item}`: replaces {one}"),
                ("delete", $"- `DELETE {item}`: deletes {one}"),
            ]
            :
            [
                ("list", $"- list: a page of {plural}"),
                ("get", $"- get: {one}"),
                ("create", $"- create: creates {one}"),
                ("update", $"- update: replaces {one}"),
                ("delete", $"- delete: deletes {one}"),
            ];

        string marker = $"/Features/{spec.Features}";
        string[] tests = [.. plan
            .Select(emission => emission.Path)
            .Where(path => path.StartsWith("Tests/", StringComparison.Ordinal) && path.Contains(marker + "/", StringComparison.Ordinal))
            .Select(path => path[..(path.IndexOf(marker + "/", StringComparison.Ordinal) + marker.Length)])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        return template
            .Replace("type: template\n", "type: feature\n", StringComparison.Ordinal)
            .Replace("<description>", $"What a caller can do with {plural}, who may, and how the feature works.", StringComparison.Ordinal)
            .Replace("<Feature>", char.ToUpperInvariant(plural[0]) + plural[1..], StringComparison.Ordinal)
            .Replace("<access>", access, StringComparison.Ordinal)
            .Replace("<shape>", shape, StringComparison.Ordinal)
            .Replace("<endpoints>", string.Join('\n', endpoints.Where(endpoint => spec.Has(endpoint.Operation)).Select(endpoint => endpoint.Line)), StringComparison.Ordinal)
            .Replace(
                "<tests>",
                tests.Length == 0 ? "no unit test project of this tree received one" : string.Join(", ", tests.Select(folder => $"`{folder}`")),
                StringComparison.Ordinal);
    }

    private static byte[] Bytes(Emission emission) => TextEdits.Encode(emission.Path, emission.Content);

    internal static Emission ManifestOf(Spec spec, List<Emission> plan, Emission? page, TextEdits.Editor editor, string version)
    {
        var options = new JsonObject
        {
            ["access"] = spec.Access,
            ["operations"] = new JsonArray([.. spec.Operations.Select(operation => (JsonNode)JsonValue.Create(operation)!)]),
            ["model"] = spec.Model,
            ["concurrency"] = spec.Concurrency,
        };

        if (spec.Access == "owner")
        {
            options["deletion"] = spec.Deletion;
        }

        var manifest = FeatureManifest.Create(Generator, version, spec.Features, spec.Type, options, spec.Fields is { } fields ? FieldsOf(fields) : null, page?.Path);

        foreach (var emission in plan)
        {
            manifest.Record(emission.Path, Bytes(emission));
        }

        manifest.Record(editor);

        return new Emission(manifest.Path, Encoding.UTF8.GetString(manifest.Bytes()));
    }

    private static JsonArray FieldsOf(IReadOnlyList<Field> fields)
    {
        var written = new JsonArray();

        foreach (var field in fields)
        {
            var entry = new JsonObject
            {
                ["name"] = field.Name,
                ["declaration"] = field.Declaration,
                ["kind"] = field.Kind.ToString(),
                ["nullable"] = field.Nullable,
            };

            if (FieldCode.IsReference(field))
            {
                entry["maxLength"] = field.Length;
            }

            if (field.Kind == FieldKind.Decimal)
            {
                entry["precision"] = field.Precision;
                entry["scale"] = field.Scale;
            }

            foreach (var bound in field.Bounds)
            {
                entry[char.ToLowerInvariant(bound.Suffix[0]) + bound.Suffix[1..]] = bound.Literal;
            }

            entry["sortable"] = FieldCode.IsSortable(field);
            entry["inSummary"] = FieldCode.InSummary(field);
            written.Add((JsonNode)entry);
        }

        return written;
    }

    internal static void Report(TextWriter output, string root, Spec spec, List<Emission> plan, string manifest, List<string> skipped, Mode mode)
    {
        string p = spec.Prefix;
        string f = spec.Features;
        string a = spec.Type;
        bool record = spec.Model == "record";
        bool owner = spec.Access == "owner";
        string verb = spec.Deletion == "anonymise" ? "Anonymise" : "Delete";
        var flags = Flags(spec);
        string[] hosts = ComposingHosts(root, spec);

        output.WriteLine(
            (mode == Mode.Applied ? "Wrote" : "Would write")
            + $" {plan.Count} files for {f}/{a} "
            + $"(access {spec.Access}, model {spec.Model}, operations {string.Join(',', spec.Operations)}, "
            + $"concurrency {spec.Concurrency}"
            + (owner ? $", deletion {spec.Deletion}" : string.Empty)
            + "):");

        foreach (var emission in plan)
        {
            output.WriteLine($"  {emission.Path}");
        }

        output.WriteLine();
        output.WriteLine(mode == Mode.Applied
            ? $"Manifest: {manifest} (the options, the SHA-256 of each file above, and each edit below with the line it is placed beside)."
            : $"Manifest: {manifest} (the options, the SHA-256 of each file above, and each edit below with the line it is placed beside), not written.");

        foreach (string project in skipped)
        {
            output.WriteLine($"Not written: '{project}' is not in this tree, so its tests were skipped.");
        }

        output.WriteLine();
        output.WriteLine(mode switch
        {
            Mode.Applied => "The feature composes through these edits, each placed beside a line it found and recorded in the manifest.",
            Mode.DryRun => "A run without --dry-run makes these edits, each placed beside a line it finds, or none when a line is missing.",
            _ => "None of these edits was made: the solution does not compile until edit 1 is made, nor the feature compose until all are.",
        });
        output.WriteLine(spec.Reaction is null
            ? "Six edits, each somewhere an absence should be visible:"
            : "Eleven edits: six like every feature, and five more because this project was");
        output.WriteLine(spec.Reaction is null
            ? string.Empty
            : "generated with no feature that owns user data, so its account-deleted reaction ships with this one.");

        if (spec.Reaction is not null)
        {
            output.WriteLine(_removalRefusal);
        }

        output.WriteLine();
        output.WriteLine($"  1. {ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence")}/Common/Contexts/AppDbContext.cs");
        output.WriteLine($"       public const string {f}Schema = \"{Snake(f)}\";");
        output.WriteLine($"       internal DbSet<{a}Record> {f} => Set<{a}Record>();");
        output.WriteLine($"       modelBuilder.ApplyConfiguration(new {a}RecordConfiguration());");
        output.WriteLine($"     with the usings of {p}.Infrastructure.Persistence.Features.{f}.Rows.Configurations and .Rows.Models");
        output.WriteLine();
        output.WriteLine($"  2. {ProjectOf(spec, "Src/Application", "Application")}/ApplicationModule.cs: an Add{f}() beside the others");

        if (record || flags.Contains("change"))
        {
            output.WriteLine($"       services.AddFeature(\"{f}\");   // its validators, found by folder, and the use-case pipeline");
            output.WriteLine($"       services.AddScoped<I{a}Service, {a}Service>();");
        }
        else
        {
            output.WriteLine($"       services.AddFeature(\"{f}\");   // its validators, found by folder, and the use-case pipeline");
        }

        if (owner)
        {
            output.WriteLine($"       services.AddDomainEventConsumer<UserAccountDeleted, {verb}{a}OnAccountDeletedConsumer>();");
            output.WriteLine($"       services.AddScoped<IDeletedAccountDataPolicy, {verb}{a}OnAccountDeletedConsumer>();");
            output.WriteLine("     // the declared fate of this feature's data once the account that owned it is gone, applied");
            output.WriteLine("     // by the consumer when the fact arrives and by the sweep when it never does");
        }

        output.WriteLine();
        output.WriteLine($"  3. {ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence")}/PersistenceModule.cs: an Add{f}Feature(services),");
        output.WriteLine("     called from AddPersistenceModule, registering the adapters of this feature's ports:");

        if (record)
        {
            if (flags.Contains("stage"))
            {
                output.WriteLine($"       services.TryAddScoped<I{a}Store, {a}Store>();");
            }

            if (flags.Contains("rq"))
            {
                output.WriteLine($"       services.TryAddScoped<I{a}Queries, {a}Queries>();");
            }
        }
        else
        {
            output.WriteLine($"       services.TryAddSingleton<I{a}Mapper, {a}Mapper>();");
            output.WriteLine($"       AddScopedTracker<{a}Tracker, I{a}Tracker>(services);");
            output.WriteLine($"       services.TryAddScoped<I{a}Repository, {a}Repository>();");

            if (flags.Contains("rq"))
            {
                output.WriteLine($"       services.TryAddScoped<I{a}Queries, {a}Queries>();");
            }
        }

        output.WriteLine();
        output.WriteLine($"  4. Src/Presentation/{hosts[0]}/Program.cs: builder.Services.Add{f}(); beside the other features,");
        output.WriteLine($"     with using {p}.Application; if the file has none yet");

        foreach (string host in hosts.Skip(1))
        {
            output.WriteLine($"     and Src/Presentation/{host}/Program.cs the same, since that host composes the features itself");
        }
        output.WriteLine();
        output.WriteLine($"  5. {ProjectOf(spec, "Tests/Architecture", "Architecture.Tests")}/Composition/HostComposition.cs: services.Add{f}(); in each");
        output.WriteLine("     composition that offers the feature, so the container tests resolve what it registers,");
        output.WriteLine($"     with using {p}.Application; if the file has none yet");
        output.WriteLine();

        string moduleTests = $"{ProjectOf(spec, "Tests/Application", "Application.UnitTests")}/ApplicationModuleTests.cs";

        if (File.Exists(Path.Combine(root, moduleTests)))
        {
            output.WriteLine($"  6. {moduleTests}: services.Add{f}(); in ComposeEveryFeature,");
            output.WriteLine("     so the registration facts read the feature's use cases and validators");
        }
        else
        {
            output.WriteLine($"  6. Nothing to edit: {moduleTests} is only generated with a feature,");
            output.WriteLine("     and this project had none. Step 5 is what makes the container tests resolve the feature.");
        }

        if (owner && spec.Reaction is { } adapter)
        {
            string directory = adapter == "Auth" ? "AccountUserAccountDirectory" : "ExternalIdentityUserAccountDirectory";
            string jobsHost = JobsHost(root, p);
            string composition = !spec.HasApi
                ? HostComposition(root, hosts[0], adapter)
                : adapter == "Auth"
                    ? $"{ProjectOf(spec, "Src/Presentation", "Api")}/Features/Auth/Composition/AuthComposition.cs"
                    : $"{ProjectOf(spec, "Src/Presentation", "Api")}/Features/ExternalIdentity/Composition/ExternalAuthenticationComposition.cs";

            output.WriteLine();
            output.WriteLine($"  7. {ProjectOf(spec, "Src/Application", "Application.Core")}/ApplicationCoreModule.cs: the AddDeletedAccountSweep()");
            output.WriteLine("     method before AddUseCasePipeline, composing the use-case pipeline as AddPurgeExpiredIdempotencyKeys does:");

            foreach (string line in SweepMethod())
            {
                output.WriteLine(line.Length == 0 ? string.Empty : "     " + line);
            }
            output.WriteLine();
            output.WriteLine($"  8. Src/Presentation/{jobsHost}/Program.cs: builder.Services.AddDeletedAccountSweep(); beside");
            output.WriteLine("     builder.Services.AddPurgeExpiredIdempotencyKeys();");
            output.WriteLine();
            output.WriteLine($"  9. Src/Presentation/{jobsHost}/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs:");
            output.WriteLine("     services.AddDeletedAccountSweepJobs(configuration); after services.AddMaintenanceJobs(configuration);");
            output.WriteLine();
            output.WriteLine($" 10. {composition}: services.AddScoped<IUserAccountDirectory, {directory}>();");
            output.WriteLine("     with the using of Application.Core's ports; the directory shares the composition's namespace");

            foreach (string host in hosts.Skip(1))
            {
                output.WriteLine($"     and in {HostComposition(root, host, adapter)}, beside the directory written for that host");
            }
            output.WriteLine();
            output.WriteLine($" 11. {ProjectOf(spec, "Tests/Architecture", "Architecture.Tests")}/Composition/HostComposition.cs: services.AddDeletedAccountSweep();");
            output.WriteLine("     after services.AddPurgeExpiredIdempotencyKeys(); in each composition, and in AddHostSuppliedAdapters");
            output.WriteLine(spec.HasApi
                ? "     and AddBackgroundOnlyAdapters the double that stands in for the directory the API composes:"
                : "     and AddBackgroundOnlyAdapters the double that stands in for the directory the hosts compose:");
            output.WriteLine("       services.AddScoped<IUserAccountDirectory, ArchitectureTestUserAccountDirectory>();");
            output.WriteLine("     declared beside the other doubles, or the container tests cannot resolve the feature's consumer:");
            foreach (string line in DirectoryDouble())
            {
                output.WriteLine("       " + line);
            }
        }

        if (WritesTheFieldTests(spec, plan))
        {
            output.WriteLine();
            output.WriteLine($" +. {TestDatabasePath(spec)}: AppDbContext.{f}Schema, before AppDbContext.PlatformSchema,");
            output.WriteLine("     so the database fixture truncates the table the feature's integration test writes to");
        }
        else if (spec.HasApi)
        {
            output.WriteLine();
            output.WriteLine($"When integration tests exercise the feature, AppDbContext.{f}Schema also joins the schemas their database");
            output.WriteLine("fixture truncates between tests (Tests/Integration, TestDatabase).");
        }

        if (spec.Fields is { } fields)
        {
            ReportFields(output, spec, fields);
        }

        output.WriteLine();

        if (mode != Mode.Applied)
        {
            output.WriteLine(mode == Mode.DryRun
                ? "Then a run without --dry-run runs, in this order, and stops at the first that fails:"
                : "Then, once the edits are made:");

            foreach (var step in Steps(root, spec))
            {
                output.WriteLine($"  {step.Display}");
            }

            output.WriteLine();
        }

        if (!record)
        {
            output.WriteLine("The aggregate raises no event. The first one it raises needs a consumer, or its name in");
            output.WriteLine("DomainEventTests._deliberatelyUnconsumed with the reason (docs/guides/adding-a-feature.md lists the closed lists).");
            output.WriteLine();
        }

        output.WriteLine("Not generated, because it is a decision rather than a shape: the rules the feature protects, any domain");

        if (spec.HasApi)
        {
            output.WriteLine("event of its own, authorisation beyond default-deny, and the integration tests against PostgreSQL,");
            output.WriteLine("a read of every response member on the wire among them.");
        }
        else
        {
            output.WriteLine("event of its own, authorisation beyond default-deny, and what each host shows of it.");
        }

        if (spec.Concurrency == "none")
        {
            output.WriteLine();
            output.WriteLine("--concurrency none: an update replaces what is stored whatever its state, so two concurrent writers");
            output.WriteLine(spec.HasApi
                ? "are last-write-wins, and the API publishes no ETag and reads no If-Match."
                : "are last-write-wins.");
        }
    }

    private static void ReportFields(TextWriter output, Spec spec, IReadOnlyList<Field> fields)
    {
        output.WriteLine();
        output.WriteLine($"--fields: {string.Join(", ", fields.Select(field => field.Declaration))}.");
        output.WriteLine("Each is checked twice, by the validator and by the aggregate or the service, stored in a column of its");
        output.WriteLine("length or precision, and carried by the contracts and the generated tests.");

        if (spec.Has("list"))
        {
            var sortable = fields.Where(FieldCode.IsSortable).Select(field => field.Name).ToList();

            output.WriteLine(sortable.Count == 0
                ? "No field sorts the list: only required string(n), int, long, DateOnly and DateTimeOffset fields do, each with its index."
                : $"The list sorts by {string.Join(", ", sortable)} and createdAt, each with its index; a text, a decimal, a bool,");
            output.WriteLine(sortable.Count == 0
                ? "A text is long and unindexed, SQLite cannot order a decimal, and a nullable field orders apart on each provider."
                : "a Guid and a nullable field do not, being long, unordered on SQLite, or ordered apart on each provider.");
        }

        if (spec.Deletion == "anonymise" && fields.Where(field => FieldCode.Anonymised(field) is null).Select(field => field.Name).ToList() is { Count: > 0 } kept)
        {
            output.WriteLine($"--deletion anonymise rewrites the strings, the texts and the nullable fields, and keeps {string.Join(", ", kept)}:");
            output.WriteLine("rewrite whatever of them still says who the owner was.");
        }
    }

    internal static void ReportPage(TextWriter output, Spec spec, Emission? page, string? unwritten, Mode mode)
    {
        string path = PagePath(spec.Features);
        bool applied = mode == Mode.Applied;

        output.WriteLine();

        if (page is null)
        {
            output.WriteLine($"Feature page: {unwritten}");
        }
        else
        {
            output.WriteLine(applied
                ? $"Feature page: {path}, from {_pageTemplate}. The manifest records it, so remove-feature deletes it, edited or not."
                : $"Feature page: {path}, from {_pageTemplate}, written with the files above.");
            output.WriteLine("Each heading says what its section holds; replace those lines with what this feature does.");
        }

        output.WriteLine(applied
            ? "docs-index lists it in the index of docs/features, by the title and the description of its frontmatter."
            : "Then docs-index lists it in the index of docs/features, by the title and the description of its frontmatter.");

        if (spec.HasApi)
        {
            output.WriteLine(applied
                ? "Its section, added to the route table under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads:"
                : "And its section of the route table under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads:");

            foreach (string line in RouteSection(spec))
            {
                output.WriteLine("  " + line);
            }
        }

        if (spec.HasApi && Flags(spec).Contains("notfound"))
        {
            output.WriteLine(applied
                ? "The row of its refusal, added under ### Resources in docs/reference/api.md, which the error-code tests read:"
                : "And the row of its refusal under ### Resources in docs/reference/api.md, which the error-code tests read:");
            output.WriteLine($"  {ErrorRow(spec)}");
        }
    }

    private static string ErrorRow(Spec spec) => $"| `{CSharpNames.Lower(spec.Type)}.notFound` | 404 | Missing, or another caller's |";

    private static readonly Regex _fileNamespace = new(@"^namespace\s+([A-Za-z0-9_.]+);", RegexOptions.Multiline, TimeSpan.FromSeconds(2));

    private static string NamespaceOf(List<Emission> plan, string type) =>
        plan.FirstOrDefault(emission => emission.Path.EndsWith($"/{type}.cs", StringComparison.Ordinal)) is { } declaring
            && _fileNamespace.Match(declaring.Content) is { Success: true } match
                ? match.Groups[1].Value
                : throw new InvalidOperationException($"No file of the plan declares {type}, which an edit names.");

    internal static TextEdits.Editor Edits(string root, Spec spec, List<Emission> plan)
    {
        var editor = new TextEdits.Editor(root);
        string p = spec.Prefix;
        string f = spec.Features;
        string a = spec.Type;
        bool record = spec.Model == "record";
        bool owner = spec.Access == "owner";
        var flags = Flags(spec);
        string consumer = $"{(spec.Deletion == "anonymise" ? "Anonymise" : "Delete")}{a}OnAccountDeletedConsumer";
        string[] hosts = ComposingHosts(root, spec);
        string persistence = PersistenceProject(spec);
        string composition = $"{ProjectOf(spec, "Tests/Architecture", "Architecture.Tests")}/Composition/HostComposition.cs";

        string context = $"{persistence}/Common/Contexts/AppDbContext.cs";
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

        string module = $"{ProjectOf(spec, "Src/Application", "Application")}/ApplicationModule.cs";
        bool service = record || flags.Contains("change");
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

        string persistenceModule = $"{persistence}/PersistenceModule.cs";
        (string Line, string[] Types)[] adapters = record
            ?
            [
                .. flags.Contains("stage") ? [($"services.TryAddScoped<I{a}Store, {a}Store>();", (string[])[$"I{a}Store", $"{a}Store"])] : Array.Empty<(string, string[])>(),
                .. flags.Contains("rq") ? [($"services.TryAddScoped<I{a}Queries, {a}Queries>();", (string[])[$"I{a}Queries", $"{a}Queries"])] : Array.Empty<(string, string[])>(),
            ]
            :
            [
                ($"services.TryAddSingleton<I{a}Mapper, {a}Mapper>();", [$"I{a}Mapper", $"{a}Mapper"]),
                ($"AddScopedTracker<{a}Tracker, I{a}Tracker>(services);", [$"{a}Tracker", $"I{a}Tracker"]),
                ($"services.TryAddScoped<I{a}Repository, {a}Repository>();", [$"I{a}Repository", $"{a}Repository"]),
                .. flags.Contains("rq") ? [($"services.TryAddScoped<I{a}Queries, {a}Queries>();", (string[])[$"I{a}Queries", $"{a}Queries"])] : Array.Empty<(string, string[])>(),
            ];
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

        foreach (string host in hosts)
        {
            string program = $"Src/Presentation/{host}/Program.cs";
            editor.Step = $"edit 4 ({program})";
            editor.Place(
                program,
                ["builder.Services.AddPurgeExpiredIdempotencyKeys();", "builder.Services.AddCacheStore();"],
                before: true,
                [$"builder.Services.Add{f}();", string.Empty],
                $"builder.Services.Add{f}();");
            editor.Using(program, $"{p}.Application");
        }

        editor.Step = $"edit 5 ({composition})";
        editor.PlaceInEachMethod(composition, "services.AddPurgeExpiredIdempotencyKeys();", before: true, $"        services.Add{f}();");
        editor.Using(composition, $"{p}.Application");

        string moduleTests = $"{ProjectOf(spec, "Tests/Application", "Application.UnitTests")}/ApplicationModuleTests.cs";

        if (File.Exists(System.IO.Path.Combine(root, moduleTests)))
        {
            editor.Step = $"edit 6 ({moduleTests})";
            editor.AppendToMethod(moduleTests, "ServiceCollection ComposeEveryFeature()", "services.Add", $"        services.Add{f}();");
        }

        if (spec.Reaction is { } adapter)
        {
            Reaction(editor, root, spec, hosts, adapter, composition);
        }

        if (WritesTheFieldTests(spec, plan))
        {
            string database = TestDatabasePath(spec);
            editor.Step = $"the schema of {database}";
            editor.Place(database, ["AppDbContext.PlatformSchema,"], before: true, [$"            AppDbContext.{f}Schema,"], $"AppDbContext.{f}Schema,");
        }

        if (spec.HasApi)
        {
            List<string> section = RouteSection(spec);

            editor.Step = "the route section of docs/reference/api.md";
            editor.Section("docs/reference/api.md", "## Routes", [.. section, string.Empty], section[2]);

            if (flags.Contains("notfound"))
            {
                editor.Step = "the error-code row of docs/reference/api.md";
                editor.Row("docs/reference/api.md", "### Resources", ErrorRow(spec), $"| `{CSharpNames.Lower(a)}.notFound` |");
            }
        }

        return editor;
    }

    private static void Reaction(TextEdits.Editor editor, string root, Spec spec, string[] hosts, string adapter, string composition)
    {
        string p = spec.Prefix;
        string jobsHost = JobsHost(root, p);
        string directory = adapter == "Auth" ? "AccountUserAccountDirectory" : "ExternalIdentityUserAccountDirectory";
        string module = adapter == "Auth" ? "services.AddAuthModule(configuration);" : "services.AddExternalIdentityPersistence(configuration);";

        string core = $"{ProjectOf(spec, "Src/Application", "Application.Core")}/ApplicationCoreModule.cs";
        editor.Step = $"edit 7 ({core})";
        editor.Place(
            core,
            ["public static IServiceCollection AddUseCasePipeline("],
            before: true,
            [.. SweepMethod().Select(line => line.Length == 0 ? line : "    " + line), string.Empty],
            "public static IServiceCollection AddDeletedAccountSweep(");

        string program = $"Src/Presentation/{jobsHost}/Program.cs";
        editor.Step = $"edit 8 ({program})";
        editor.Place(
            program,
            ["builder.Services.AddPurgeExpiredIdempotencyKeys();"],
            before: false,
            [string.Empty, "builder.Services.AddDeletedAccountSweep();"],
            "builder.Services.AddDeletedAccountSweep();");
        editor.Using(program, $"{p}.Application.Core");

        string jobs = $"Src/Presentation/{jobsHost}/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs";
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

    private static List<string> RouteSection(Spec spec)
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
