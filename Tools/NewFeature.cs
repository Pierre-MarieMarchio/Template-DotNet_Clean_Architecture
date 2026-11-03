#:property UseAppHost=false

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class NewFeature
{
    private const string _manifestFolder = ".feature-manifest";

    private const string _pageTemplate = "docs/_page-templates/feature.md";

    private const string _usage =
        "Usage: dotnet run Tools/NewFeature.cs <Features> <Feature> --access owner|shared\n"
        + "           [--deletion delete|anonymise]\n"
        + "           [--operations get,list,create,update,delete]\n"
        + "           [--model record|aggregate]\n"
        + "           [--concurrency none|etag]\n"
        + "           [--fields \"<Name:type>, ...\"]\n"
        + "           [--root <path>] [--dry-run]\n"
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
        + "  --root         the tree to write into, default the current directory.\n"
        + "  --dry-run      print the files and the edits, and write and run nothing.\n"
        + "  Without --dry-run it writes the feature's files, makes every edit that composes it, each\n"
        + "  placed beside a line it finds, and records them in the manifest; when one of those lines is\n"
        + "  missing it writes nothing. It refuses while a context has no initial migration or the\n"
        + "  business model has a change no migration holds, then builds, adds the Add<Features>\n"
        + "  migration and rewrites the OpenAPI reference and the clients written from it.\n"
        + "       dotnet run Tools/NewFeature.cs --self-test";

    private static readonly string[] _operationNames = ["get", "list", "create", "update", "delete"];

    private static readonly string[] _optionNames =
        ["--access", "--deletion", "--operations", "--model", "--concurrency", "--fields", "--root"];

    private static readonly string[] _reservedNames =
    [
        "Access", "Api", "Application", "Auth", "Common", "Contracts", "Controllers", "Core", "Dtos",
        "Domain", "Entities", "Errors", "Features", "Infrastructure", "Model", "Models", "Persistence",
        "Policies", "Ports", "Presentation", "Rows", "Services", "UseCases",
    ];

    private sealed record Emission(string Path, string Content);

    private sealed record Request(
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

    private sealed record Spec(
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

    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-test") >= 0)
        {
            return SelfTest();
        }

        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(_usage);

            return 0;
        }

        /*#if (!publicApi)*/
        return Run(args, Console.Out, Console.Error, TextEdits.Execute);
        /*#else
        int rootOption = Array.IndexOf(args, "--root");
        string target = rootOption >= 0 && rootOption + 1 < args.Length ? args[rootOption + 1] : ".";
        string thisProject = Path.GetDirectoryName(Path.GetDirectoryName(ThisFile())!)!;

        if (string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(thisProject)),
            StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                "This project was generated with --auth none: it authenticates nobody, and every "
                + "feature this tool writes (--access owner or shared) needs an authenticated caller. "
                + "Add an authentication scheme and an ICurrentUser that reads it before scaffolding a "
                + "feature, or write a public feature by hand.");

            return 1;
        }

        return Run(args, Console.Out, Console.Error, TextEdits.Execute);

        static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
        #endif*/
    }

    private static int Run(string[] args, TextWriter output, TextWriter error, Func<TextEdits.Command, int> run)
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
        var manifest = ManifestOf(spec, plan, page, editor.Applied);

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

    private enum Mode
    {
        Applied,
        DryRun,
        Refused,
    }

    private const int _manifestSchemaVersion = 2;

    private static (string Path, byte[] Bytes) Created(Emission emission) => (emission.Path, Bytes(emission));

    private static bool AlreadyGenerated(string root, Spec spec)
    {
        string path = System.IO.Path.Combine(root, ManifestPath(spec));

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var top = document.RootElement;
            var options = top.GetProperty("options");

            return top.GetProperty("schemaVersion").GetInt32() == _manifestSchemaVersion
                && top.GetProperty("feature").GetString() == spec.Features
                && top.GetProperty("type").GetString() == spec.Type
                && options.GetProperty("access").GetString() == spec.Access
                && options.GetProperty("model").GetString() == spec.Model
                && options.GetProperty("concurrency").GetString() == spec.Concurrency
                && (options.TryGetProperty("deletion", out var deletion) ? deletion.GetString() : string.Empty) == spec.Deletion
                && options.GetProperty("operations").EnumerateArray().Select(operation => operation.GetString()).SequenceEqual(spec.Operations)
                && (top.TryGetProperty("fields", out var fields)
                    ? spec.Fields is not null && fields.EnumerateArray().Select(field => field.GetProperty("declaration").GetString()).SequenceEqual(spec.Fields.Select(field => field.Declaration))
                    : spec.Fields is null);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return false;
        }
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
        TextEdits.Task(root, "build"),
        .. migrated ? Array.Empty<TextEdits.Command>() : [TextEdits.FromLastBuild(root, "migration-add", $"Add{spec.Features}")],
        .. spec.HasApi ? TextEdits.ContractSteps(root, FolderOf(spec.Prefix, "Client"), built: true) : [],
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

    private static Request Parse(string[] args, List<string> problems)
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

    private static Spec? Check(Request request, string root, List<string> problems)
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

    private static List<string> Contradictions(string[] operations, string model, string concurrency)
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

    private static string ProductPrefix(string root)
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
            .Any(project => File.ReadAllText(project).Contains(@"..\..\Clients\", StringComparison.Ordinal));

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

    private static string ProjectInFolder(string prefix, string folder) => prefix + folder[Initials(prefix).Length..];

    private static string Initials(string prefix) => string.Concat(prefix.Split('.').Select(SegmentInitials));

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));

    private static List<Emission> Plan(Spec spec, string root, out List<string> skippedTestProjects)
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
                            out DateTimeOffset key))
                        {
                            throw new InvalidOperationException(
                                "The cursor's key is not a valid date/time: CursorKeys should have rejected it first.");
                        }

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

    private static string ManifestPath(Spec spec) => $"{_manifestFolder}/{spec.Features}.json";

    private static string PagePath(string features) => $"docs/features/{Kebab(features)}.md";

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

    private static string HashOf(Emission emission) =>
        Convert.ToHexStringLower(SHA256.HashData(Bytes(emission)));

    private static Emission ManifestOf(Spec spec, List<Emission> plan, Emission? page, IReadOnlyList<TextEdits.Edit> edits)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", _manifestSchemaVersion);
            writer.WriteString("generator", "Tools/NewFeature.cs");
            writer.WriteString("feature", spec.Features);
            writer.WriteString("type", spec.Type);

            writer.WriteStartObject("options");
            writer.WriteString("access", spec.Access);
            writer.WriteStartArray("operations");

            foreach (string operation in spec.Operations)
            {
                writer.WriteStringValue(operation);
            }

            writer.WriteEndArray();
            writer.WriteString("model", spec.Model);
            writer.WriteString("concurrency", spec.Concurrency);

            if (spec.Access == "owner")
            {
                writer.WriteString("deletion", spec.Deletion);
            }

            writer.WriteEndObject();

            if (spec.Fields is { } fields)
            {
                WriteFields(writer, fields);
            }

            writer.WriteStartArray("files");

            foreach (var emission in plan.OrderBy(emission => emission.Path, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", emission.Path);
                writer.WriteString("sha256", HashOf(emission));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            if (page is not null)
            {
                writer.WriteString("page", page.Path);
            }

            TextEdits.WriteEdits(writer, edits);
            writer.WriteEndObject();
        }

        string json = Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n";

        return new Emission(ManifestPath(spec), json);
    }

    private static void WriteFields(Utf8JsonWriter writer, IReadOnlyList<Field> fields)
    {
        writer.WriteStartArray("fields");

        foreach (var field in fields)
        {
            writer.WriteStartObject();
            writer.WriteString("name", field.Name);
            writer.WriteString("declaration", field.Declaration);
            writer.WriteString("kind", field.Kind.ToString());
            writer.WriteBoolean("nullable", field.Nullable);

            if (FieldCode.IsReference(field))
            {
                writer.WriteNumber("maxLength", field.Length);
            }

            if (field.Kind == FieldKind.Decimal)
            {
                writer.WriteNumber("precision", field.Precision);
                writer.WriteNumber("scale", field.Scale);
            }

            foreach (var bound in field.Bounds)
            {
                writer.WriteString(char.ToLowerInvariant(bound.Suffix[0]) + bound.Suffix[1..], bound.Literal);
            }

            writer.WriteBoolean("sortable", FieldCode.IsSortable(field));
            writer.WriteBoolean("inSummary", FieldCode.InSummary(field));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void Report(TextWriter output, string root, Spec spec, List<Emission> plan, string manifest, List<string> skipped, Mode mode)
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

    private static void ReportPage(TextWriter output, Spec spec, Emission? page, string? unwritten, Mode mode)
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

        output.WriteLine(applied ? "Its row, added under ## Features in docs/README.md:" : "Then its row under ## Features in docs/README.md:");
        output.WriteLine($"  {ReadmeRow(spec)}");

        //#if (api)
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
        //#endif
    }

    private static string ReadmeRow(Spec spec)
    {
        string title = Separated(spec.Features, ' ');

        return $"| [{char.ToUpperInvariant(title[0]) + title[1..]}](features/{Kebab(spec.Features)}.md) | what a caller can do with it |";
    }

    private static string ErrorRow(Spec spec) => $"| `{CSharpNames.Lower(spec.Type)}.notFound` | 404 | Missing, or another caller's |";

    private static readonly Regex _fileNamespace = new(@"^namespace\s+([A-Za-z0-9_.]+);", RegexOptions.Multiline, TimeSpan.FromSeconds(2));

    private static string NamespaceOf(List<Emission> plan, string type) =>
        plan.FirstOrDefault(emission => emission.Path.EndsWith($"/{type}.cs", StringComparison.Ordinal)) is { } declaring
            && _fileNamespace.Match(declaring.Content) is { Success: true } match
                ? match.Groups[1].Value
                : throw new InvalidOperationException($"No file of the plan declares {type}, which an edit names.");

    private static TextEdits.Editor Edits(string root, Spec spec, List<Emission> plan)
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

        editor.Step = "the row of docs/README.md";
        editor.Row("docs/README.md", "## Features", ReadmeRow(spec), $"](features/{Kebab(f)}.md)");

        //#if (api)
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

        //#endif
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

    private static int SelfTest()
    {
        string scratch = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "newfeature-selftest-" + Guid.CreateVersion7().ToString("N"));
        var problems = new List<string>();
        int checks = 0;

        void Expect(bool condition, string message)
        {
            checks++;

            if (!condition)
            {
                problems.Add(message);
            }
        }

        try
        {
            Directory.CreateDirectory(scratch);

            ParserCases(scratch, Expect);
            CombinationCases(scratch, Expect);
            NamingAndPrefixCases(scratch, Expect);
            SubsetCases(scratch, Expect);
            AppliedCases(scratch, Expect);
            AllOrNothingCases(scratch, Expect);
            IdempotenceCases(scratch, Expect);
            DryRunCases(scratch, Expect);
            PreconditionCases(scratch, Expect);
            StepCases(scratch, Expect);
            NoOverwriteCases(scratch, Expect);
            ManifestCases(scratch, Expect);
            RollbackCases(scratch, Expect);
            ReactionCases(scratch, Expect);
            PageCases(scratch, Expect);
            WithoutTheApiCases(scratch, Expect);
            WithoutFieldsCases(scratch, Expect);
            ProviderCases(scratch, Expect);
            FieldGrammarCases(scratch, Expect);
            FieldEmissionCases(scratch, Expect);
            TemplateGuardCases(Expect);
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        Console.WriteLine($"Self-test: {checks} check(s), {problems.Count} problem(s).");

        foreach (string problem in problems)
        {
            Console.Error.WriteLine($"  {problem}");
        }

        return problems.Count == 0 ? 0 : 1;
    }

    private static string NewTree(string scratch, bool reaction = true, string database = "postgresql")
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

            ## Features

            | Page | Answers |
            |---|---|
            | [Email](features/email.md) | how mail is sent |

            ## Reference

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
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = Run([.. args, "--root", root], output, error, run);

        return (code, output.ToString(), error.ToString());
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
            (["Widgets", "Widget"], "--access is required"),
            (["Widgets", "Widget", "--access", "owner"], "--deletion is required"),
            (["Widgets", "Widget", "--access", "owner", "--deletion"], "'--deletion' needs a value"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "keep"], "--deletion 'keep' is not a choice"),
            (["Widgets", "Widget", "--access", "shared", "--deletion", "delete"], "Leave --deletion out"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--bogus", "x"], "'--bogus' is not an option"),
            (["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--deletion", "anonymise"], "is given twice"),
            (["Widgets", "Widget", "--access"], "'--access' needs a value"),
            (["Widgets", "Widget", "--access", "owner", "--access", "shared"], "is given twice"),
            (["Widgets", "--access", "owner"], "Expected two names"),
            (["Widgets", "Widget", "Extra", "--access", "owner"], "Expected two names"),
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

        string stranger = System.IO.Path.Combine(scratch, "stranger");
        Directory.CreateDirectory(stranger);

        var elsewhere = Invoke(stranger, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(elsewhere.Code == 1 && elsewhere.Error.Contains("No cleanarch.json", StringComparison.Ordinal), "A tree without a project file was not refused for it.");
        expect(!Directory.EnumerateFileSystemEntries(stranger).Any(), "A refused tree was written to.");
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

        string[] leaks = [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("AppTemplate", StringComparison.Ordinal))
            .Select(path => System.IO.Path.GetRelativePath(root, path))];

        expect(leaks.Length == 0, $"The template's own name leaked into: {string.Join(", ", leaks)}");

        expect(result.Output.Contains("AddTodoLists", StringComparison.Ordinal), "The composition steps do not name AddTodoLists.");
        expect(result.Output.Contains("TodoListsSchema", StringComparison.Ordinal), "The composition steps do not name the schema constant.");
        expect(result.Output.Contains("migration-add", StringComparison.Ordinal), "The migration step is not printed.");
        expect(result.Output.Contains("last-write-wins", StringComparison.Ordinal), "The last-write-wins consequence of --concurrency none is not printed.");
        expect(result.Output.Contains("dotnet run Tools/Tasks.cs -- openapi --no-build", StringComparison.Ordinal), "The OpenAPI step is not printed.");
        expect(
            !result.Output.Contains("api-client", StringComparison.Ordinal) && !result.Output.Contains("pnpm api", StringComparison.Ordinal),
            "A step is printed for a client and a web workspace the project does not hold.");

        string contractRoot = NewTree(scratch);

        Directory.CreateDirectory(System.IO.Path.Combine(contractRoot, "Src/Clients/A.Client"));
        Directory.CreateDirectory(System.IO.Path.Combine(contractRoot, "Src/Web/packages/api-client/scripts"));
        File.WriteAllText(System.IO.Path.Combine(contractRoot, "Src/Web/packages/api-client/scripts/generate.mjs"), "// the script pnpm api runs\n");

        var ran = new List<TextEdits.Command>();
        var contract = Invoke(contractRoot, Recording(ran), "Notes", "Note", "--access", "shared");

        expect(
            Launched(ran).SequenceEqual(
            [
                "dotnet run Tools/Tasks.cs migration-check",
                "dotnet run Tools/Tasks.cs build",
                "dotnet run Tools/Tasks.cs -- migration-add AddNotes --no-build",
                "dotnet run Tools/Tasks.cs -- openapi --no-build",
                "dotnet run Tools/Tasks.cs api-client",
                "pnpm api --no-build",
            ]),
            $"The check, the one build, then the migration, the OpenAPI reference, the client and the web schema from that build were not run in that order: {string.Join(" | ", Launched(ran))}");
        expect(ran.Last().WorkingDirectory == System.IO.Path.Combine(contractRoot, "Src", "Web"), "pnpm api did not run in Src/Web.");
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
            ("docs/README.md", "| [Widgets](features/widgets.md) | what a caller can do with it |"),
            //#if (api)
            ("docs/reference/api.md", "Under `/api/v1/widgets`."),
            ("docs/reference/api.md", "| `widget.notFound` | 404 | Missing, or another caller's |"),
            //#endif
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

        //#if (api)
        string api = File.ReadAllText(System.IO.Path.Combine(root, "docs/reference/api.md"));

        expect(
            api.IndexOf("### Widgets", StringComparison.Ordinal) is var section and > 0
                && section > api.IndexOf("### Health", StringComparison.Ordinal)
                && section < api.IndexOf("## Error codes", StringComparison.Ordinal),
            "The route section was not placed after the others under ## Routes.");
        //#endif

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
                edits.Any(edit => edit.GetProperty("path").GetString() == "docs/README.md"),
                "The manifest does not record the row of docs/README.md.");
        }

        expect(
            Launched(ran).SequenceEqual(
            [
                "dotnet run Tools/Tasks.cs migration-check",
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
            ("docs/README.md", "## Features", "docs/README.md"),
            //#if (api)
            ("docs/reference/api.md", "## Error codes", "the route section"),
            //#endif
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
            Launched(ran).SequenceEqual(["dotnet run Tools/Tasks.cs build", "dotnet run Tools/Tasks.cs -- openapi --no-build"]),
            $"Running the same command again did not resume with the build and the OpenAPI reference from it alone: {string.Join(" | ", Launched(ran))}");

        foreach (string line in new[] { "builder.Services.AddWidgets();", "using Acme.Application;" })
        {
            expect(Occurrences(root, "Src/Presentation/A.Api/Program.cs", line) == 1, $"'{line}' is in Program.cs other than once after two runs.");
        }

        var dry = Invoke(root, Recording(ran = []), "Widgets", "Widget", "--access", "shared", "--dry-run");

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
        var none = Invoke(unmigrated, Recording(ran = []), "Widgets", "Widget", "--access", "shared");

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
                ["dotnet run Tools/Tasks.cs migration-check", "dotnet run Tools/Tasks.cs build", "dotnet run Tools/Tasks.cs -- migration-add AddWidgets --no-build"]),
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
        "docs/README.md",
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
            "<Project><ItemGroup><ProjectReference Include=\"..\\..\\Clients\\A.Client\\Acme.Client.csproj\" /></ItemGroup></Project>\n");

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

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string WithPageTemplate(string root)
    {
        string shipped = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(SourceFile())!)!, _pageTemplate);
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
                    && written.Contains($"{_manifestFolder}/{combination[0]}.json", StringComparer.Ordinal)
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

        expect(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "The page has no UTF-8 BOM, which every Markdown file of the tree has.");
        expect(!text.Contains('\r', StringComparison.Ordinal) && text.EndsWith('\n') && !text.EndsWith("\n\n", StringComparison.Ordinal), "The page is not LF only, ending in one newline.");
        expect(!Regex.IsMatch(text, "<[A-Za-z]+>", RegexOptions.None, TimeSpan.FromSeconds(2)), "A placeholder of the page template survived.");
        expect(text.TrimStart('\uFEFF').StartsWith("# Todo lists\n", StringComparison.Ordinal), "The page's title is not the feature's name in words.");

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
        expect(result.Output.Contains("| [Todo lists](features/todo-lists.md) |", StringComparison.Ordinal), "The row of docs/README.md was not printed.");
        //#if (api)
        expect(result.Output.Contains("Under `/api/v1/todo-lists`.", StringComparison.Ordinal), "The route section of docs/reference/api.md was not printed.");
        expect(result.Output.Contains("| GET, HEAD | `/{todoListId}` | 200 | |", StringComparison.Ordinal), "The row of the get route was not printed.");
        //#endif

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

    private const string _removalRefusal = "remove-feature refuses to remove this feature while another owner feature still uses these files.";

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
        ("record, owner, delete", "e163bf80ae69729e7f326fc38ecc9dab57f66b9c4594f02873f4a5b7c7124850"),
        ("record, owner, anonymise", "36cbb1cea5755de5ea1c2b35311fa9b69eab642376ffb225da33826dccb00a50"),
        ("record, shared", "5e72b73401b1fa87c9c616e34a8734a27e17bc7c32ffaff4c200e4b69518e92e"),
        ("aggregate, owner, delete", "6bdc4580d8a672555f961c4130dc5af2452b2697c4bee4a75a7bc6b5cf3f35f6"),
        ("aggregate, owner, anonymise", "e4e3ec095d0a76adbd25c3b81d19968225b4d3e4c8f9a7824ca8f5b0d84c9c51"),
        ("aggregate, shared", "98a62c8b370c718eb3e1436fab9cad52f5823b75d87e4bb9f560dafb53d9967f"),
        ("record, owner, delete, the project's first owner feature", "e880bd15eac462476f4b80e1a48e6fd8b2db491453b2cd478b7bbbcf28dd194a"),
        ("record, owner, delete, without the API", "2acdd9553a3b112bda2f331606c60487785993cfc8a87d483470dbcdc332cf69"),
        ("record, owner, delete, on SQL Server", "e163bf80ae69729e7f326fc38ecc9dab57f66b9c4594f02873f4a5b7c7124850"),
        ("record, owner, delete, on SQLite", "2aa9c005001729a66e9772bd79ae80e97c918097eabb5786d4bc7bc42317c676"),
    ];

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
                + "is a change for every project that runs new-feature: make it on purpose, then record the new digest.");
        }
    }

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

            void Feed(string text) => digest.AppendData(Encoding.UTF8.GetBytes(text + "\u0000"));

            foreach (string[] subset in subsets)
            {
                foreach (string concurrency in new[] { "none", "etag" })
                {
                    var problems = new List<string>();
                    var request = Parse(
                        ["Widgets", "Widget", .. options, "--operations", string.Join(',', subset), "--concurrency", concurrency, "--root", root],
                        problems);

                    if (problems.Count > 0 || Check(request, System.IO.Path.GetFullPath(root), problems) is not { } spec)
                    {
                        continue;
                    }

                    var plan = Plan(spec, root, out var skipped);
                    var editor = Edits(root, spec, plan);
                    var manifest = ManifestOf(spec, plan, null, editor.Applied);

                    using var report = new StringWriter();
                    Report(report, root, spec, plan, manifest.Path, skipped, Mode.DryRun);
                    ReportPage(report, spec, null, "no page", Mode.DryRun);

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
        segments[projectIndex] = ProjectInFolder(ProductPrefix(root), segments[projectIndex]);
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
        [.. typeof(Templates)
            .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .SelectMany(field => _privateMethodDeclaration.Matches((string)field.GetRawConstantValue()!))
            .Select(match => match.Groups[1].Value.Replace("%", string.Empty, StringComparison.Ordinal))]);

    private static IEnumerable<string> PrivateMethodProblems(string relative, string text)
    {
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
                    int end = At(1) == '/'
                        ? text.IndexOf('\n', position) is var newline and >= 0 ? newline : text.Length
                        : text.IndexOf("*/", position + 2, StringComparison.Ordinal) is var close and >= 0 ? close + 2 : text.Length;

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
}

internal static class Template
{
    private static readonly HashSet<string> _knownFlags = new(StringComparer.Ordinal)
    {
        "get", "list", "create", "update", "delete", "owner", "shared", "rec", "agg", "etag",
        "content", "change", "wq", "dto", "single", "rq", "notfound", "clock", "stage",
        "purge", "anon", "fields", "anontext", "trim", "policy", "prefixed",
    };

    private static readonly Regex _tokenPattern = new("%([A-Za-z]+)%", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static readonly Regex _directivePattern = new(
        @"^\s*%(?:if (?<condition>[^%]+)|(?<else>else)|(?<end>end))%\s*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    private static readonly Regex _markedUsing = new(
        @"^using (?<namespace>[^;]+);\s*//\?(?<types>.+)$",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    internal static bool Evaluate(string condition, HashSet<string> flags) =>
        condition.Split('|').Any(group => group.Split('&').All(atom =>
        {
            string trimmed = atom.Trim();
            bool negated = trimmed.StartsWith('!');
            string name = negated ? trimmed[1..] : trimmed;

            if (!_knownFlags.Contains(name))
            {
                throw new InvalidOperationException($"'{name}' is not a flag a template may name.");
            }

            return flags.Contains(name) != negated;
        }));

    internal static string Substitute(string text, Dictionary<string, string> tokens)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            string next = _tokenPattern.Replace(
                text,
                match => tokens.TryGetValue(match.Groups[1].Value, out string? value) ? value : match.Value);

            if (string.Equals(next, text, StringComparison.Ordinal))
            {
                break;
            }

            text = next;
        }

        return text;
    }

    internal static string Render(string template, HashSet<string> flags, Dictionary<string, string> tokens)
    {
        var kept = new List<string>();
        var frames = new Stack<(bool Parent, bool Condition)>();
        bool emitting = true;

        foreach (string line in template.ReplaceLineEndings("\n").Split('\n'))
        {
            var directive = _directivePattern.Match(line);

            if (!directive.Success)
            {
                if (emitting)
                {
                    kept.Add(line);
                }

                continue;
            }

            if (directive.Groups["condition"].Success)
            {
                bool holds = Evaluate(directive.Groups["condition"].Value, flags);

                frames.Push((emitting, holds));
                emitting = emitting && holds;
            }
            else if (directive.Groups["else"].Success)
            {
                var (parent, condition) = frames.Peek();

                emitting = parent && !condition;
            }
            else
            {
                emitting = frames.Pop().Parent;
            }
        }

        if (frames.Count > 0)
        {
            throw new InvalidOperationException("A template opens an %if% it never closes.");
        }

        return Tidy(PruneUsings(Substitute(string.Join('\n', kept), tokens)));
    }

    private static string PruneUsings(string text)
    {
        string[] lines = text.Split('\n');
        int declaration = Array.FindIndex(lines, line => line.StartsWith("namespace ", StringComparison.Ordinal));

        if (declaration < 0)
        {
            throw new InvalidOperationException("A template declares no namespace.");
        }

        string body = string.Join('\n', lines[declaration..]);
        var usings = new List<string>();

        foreach (string line in lines[..declaration].Where(line => line.StartsWith("using ", StringComparison.Ordinal)))
        {
            var marked = _markedUsing.Match(line);

            if (!marked.Success)
            {
                usings.Add(line);

                continue;
            }

            if (Regex.IsMatch(body, $@"\b(?:{marked.Groups["types"].Value.Trim()})\b", RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                usings.Add($"using {marked.Groups["namespace"].Value};");
            }
        }

        string[] sorted =
        [
            .. usings.Distinct(StringComparer.Ordinal)
                .OrderBy(line => !line.StartsWith("using System", StringComparison.Ordinal))
                .ThenBy(line => line.TrimEnd(';'), StringComparer.Ordinal),
        ];

        return sorted.Length == 0 ? body : string.Join('\n', sorted) + "\n\n" + body;
    }

    private static string Tidy(string text)
    {
        var lines = new List<string>();

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd();

            if (line.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0 || lines[^1].EndsWith('{')))
            {
                continue;
            }

            if (line.TrimStart().StartsWith('}') && lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            lines.Add(line);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines) + "\n";
    }
}

internal static class Templates
{
    internal const string Aggregate = """
        using %P%.Domain.Core.Common.Abstractions; //?IOwnedAggregate
        using %P%.Domain.Core.Common.Exceptions; //?DomainException
        using %P%.Domain.Core.Common.Primitives;

        namespace %P%.Domain.Features.%F%.Model.Entities;

        public sealed class %A% : AuditableAggregateRoot<Guid>%Implements%
        {
        %if fields%
        %FConstants%
        %else%
            public const int NameMaxLength = 200;

            public const int DescriptionMaxLength = 2_000;
        %end%
        %if anontext%

            public const string AnonymisedText = "[deleted account]";
        %end%

            private %A%(
                Guid id,
        %if owner%
                UserId ownerId,
        %end%
        %if fields%
        %FCtorParams%
        %else%
                string name,
                string description) : base(id)
        %end%
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);

                OwnerId = ownerId;
        %end%
        %if fields%
        %FAssign%
        %else%
                Name = name;
                Description = description;
        %end%
            }

        %if owner%
            public UserId OwnerId { get; }

        %end%
        %if fields%
        %FProperties%
        %else%
        %if update|anon%
            public string Name { get; private set; }

            public string Description { get; private set; }
        %else%
            public string Name { get; }

            public string Description { get; }
        %end%
        %end%

        %if create%
            public static %A% Create(
        %if owner%
                UserId ownerId,
        %end%
        %if fields%
        %FCreateParams%
        %else%
                string name,
                string description) =>
        %end%
                new(
                    Guid.CreateVersion7(),
        %if owner%
                    ownerId,
        %end%
        %if fields%
        %FCreateArgs%
        %else%
                    RequireName(name),
                    RequireDescription(description));
        %end%

        %end%
            public static %A% Rehydrate(
                Guid id,
        %if owner%
                UserId ownerId,
        %end%
        %if fields%
        %FRehydrateParams%
        %else%
                string name,
                string description)
        %end%
            {
                if (id == Guid.Empty)
                {
                    throw new DomainException("A stored %L% must have an id.");
                }

                return new(
                    id,
        %if owner%
                    ownerId,
        %end%
        %if fields%
        %FRehydrateArgs%
        %else%
                    name,
                    description);
        %end%
            }

        %if update%
        %if fields%
            public void Update(%FUpdateParams%)
            {
        %FUpdateBody%
            }
        %else%
            public void Update(string name, string description)
            {
                string validName = RequireName(name);
                string validDescription = RequireDescription(description);

                Name = validName;
                Description = validDescription;
            }
        %end%

        %end%
        %if anon%
            public void Anonymise()
            {
        %if fields%
        %FAnonymise%
        %else%
                Name = AnonymisedText;
                Description = string.Empty;
        %end%
            }

        %end%
        %if content&fields%
        %FRequire%
        %end%
        %if content&!fields%
            private static string RequireName(string name)
            {
                string trimmed = name?.Trim() ?? string.Empty;

                if (trimmed.Length == 0)
                {
                    throw new DomainException("%An% %L% name is required.");
                }

                return trimmed.Length <= NameMaxLength
                    ? trimmed
                    : throw new DomainException($"%An% %L% name cannot exceed {NameMaxLength} characters.");
            }

            private static string RequireDescription(string description)
            {
                if (description is null)
                {
                    throw new DomainException("%An% %L% description is required; it may be empty.");
                }

                return description.Length <= DescriptionMaxLength
                    ? description
                    : throw new DomainException($"%An% %L% description cannot exceed {DescriptionMaxLength} characters.");
            }
        %end%
        }
        """;

    internal const string RepositoryPort = """
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;

        namespace %P%.Domain.Features.%F%.Repositories;

        public interface I%A%Repository
        {
        %if change%
        %if owner%
            Task<%A%?> GetAsync(UserId ownerId, Guid id, CancellationToken cancellationToken);
        %else%
            Task<%A%?> GetAsync(Guid id, CancellationToken cancellationToken);
        %end%

        %end%
        %if owner%
            Task<IReadOnlyList<%A%>> GetForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken);

        %end%
        %if create%
            void Add(%A% aggregate);

        %end%
        %if delete|owner%
            void Remove(%A% aggregate);
        %end%
        }
        """;

    internal const string ContentDto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        %if fields%
        public sealed record %A%ContentDto(
        %FParamList%);
        %else%
        public sealed record %A%ContentDto(string Name, string Description);
        %end%
        """;

    internal const string Dto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %A%Dto(
            Guid Id,
        %if fields%
        %FDtoParams%
        %else%
            string Name,
            string Description,
        %end%
            DateTimeOffset CreatedAt,
            DateTimeOffset? LastModifiedAt);
        """;

    internal const string SummaryDto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %A%SummaryDto(
            Guid Id,
        %if fields%
        %FSummaryParams%
        %else%
            string Name,
        %end%
            DateTimeOffset CreatedAt,
            DateTimeOffset? LastModifiedAt);
        """;

    internal const string ListQuery = """
        using %P%.Application.Core.Common.Collections;

        namespace %ListInputNamespace%;

        public sealed record %ListInput%(
            string? Paging,
            int? Page,
            int? PageSize,
            string? Cursor,
            string? Sort) : ICollectionQuery
        {
            public static %ListInput% Offset(int? page, int? pageSize) => new(null, page, pageSize, null, null);
        }
        """;

    internal const string Errors = """
        using %P%.Application.Core.Common.Results;

        namespace %P%.Application.Features.%F%.Model.Errors;

        public static class %A%Errors
        {
            public static Error %A%NotFound(Guid %L%Id) => Error.NotFound(
                "%L%.notFound",
                $"No %L% with id '{%L%Id}' was found.");
        }
        """;

    internal const string CollectionPolicy = """
        using %P%.Application.Core.Common.Collections;
        using %P%.Application.Core.Common.Policies;

        namespace %P%.Application.Features.%F%.Model.Policies;

        public sealed class %A%CollectionPolicy : ICollectionPolicy
        {
        %if fields%
        %FPolicyFields%
        %else%
            public const string NameField = "name";
        %end%

            public const string CreatedAtField = "createdAt";

            public static readonly %A%CollectionPolicy Instance = new();

            public IReadOnlyList<SortableField> SortableFields { get; } =
            [
        %if fields%
        %FSortables%
        %else%
                SortableField.Keyset(NameField),
        %end%
                SortableField.Keyset(CreatedAtField),
            ];

            public string DefaultSort => "createdAt:desc";

            public int MaxSortTerms => 2;

            public int MaxPageSize => 100;

            public int DefaultPageSize => 20;
        }
        """;

    internal const string InputPolicy = """
        namespace %P%.Application.Features.%F%.Model.Policies;

        public static class %A%InputPolicy
        {
        %if fields%
        %FPolicyConstants%
        %else%
            public const int NameMaxLength = 200;

            public const int DescriptionMaxLength = 2_000;
        %end%
        }
        """;

    internal const string Filter = """
        namespace %P%.Application.Features.%F%.Ports.%A%Queries;

        public sealed record %A%Filter
        {
            public static readonly %A%Filter None = new();
        }
        """;

    internal const string QueriesPort = """
        using %P%.Application.Core.Common.Collections; //?FeaturePageRequest
        using %P%.Application.Core.Common.Concurrency; //?Versioned
        using %P%.Application.Core.Common.Results; //?PagedResult
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%SummaryDto|%A%Dto
        using %P%.Domain.Core.Common.Primitives; //?UserId

        namespace %P%.Application.Features.%F%.Ports.%A%Queries;

        public interface I%A%Queries
        {
        %if list%
            Task<PagedResult<%A%SummaryDto>> %ListQuery%(
        %if owner%
                UserId ownerId,
        %end%
                FeaturePageRequest<%A%Filter> request,
                CancellationToken cancellationToken = default);

        %end%
        %if single%
            Task<%ReadDto%?> GetAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                CancellationToken cancellationToken = default);
        %end%
        %if owner%

            Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
        %end%
        }
        """;

    internal const string StorePort = """
        using %P%.Application.Core.Common.Concurrency; //?VersionPrecondition
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto
        using %P%.Domain.Core.Common.Primitives; //?UserId

        namespace %P%.Application.Features.%F%.Ports.%A%Store;

        public interface I%A%Store
        {
        %if create%
            void Add(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                %A%ContentDto content,
                DateTimeOffset createdAt);

        %end%
        %if update%
            Task<%A%ChangeStatus> StageReplacementAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                %A%ContentDto content,
                DateTimeOffset modifiedAt,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default);

        %end%
        %if delete%
            Task<%A%ChangeStatus> StageRemovalAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default);
        %end%
        %if purge%

            Task<int> StageRemovalForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken = default);
        %end%
        %if anon%

            Task<int> StageAnonymisationForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken = default);
        %end%
        }
        """;

    internal const string ChangeStatus = """
        namespace %P%.Application.Features.%F%.Ports.%A%Store;

        public enum %A%ChangeStatus
        {
            Staged,

            NotFound,
        %if etag%

            VersionMismatch,
        %end%
        }
        """;

    internal const string ServicePort = """
        using %P%.Application.Core.Common.Concurrency; //?Versioned|VersionPrecondition|ConcurrencyErrors
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto|%A%Dto|%A%SummaryDto|%A%ListQueryDto
        using %P%.Application.Features.%F%.Model.Errors; //?%A%Errors

        namespace %P%.Application.Features.%F%.Services;

        public interface I%A%Service
        {
        %if list%
            Task<Result<PagedResult<%A%SummaryDto>>> ListAsync(
                %A%ListQueryDto query,
                CancellationToken cancellationToken = default);

        %end%
        %if get%
            Task<Result<%ReadDto%>> GetAsync(
                Guid %L%Id,
                CancellationToken cancellationToken = default);

        %end%
        %if create%
            Task<Result<%ReadDto%>> CreateAsync(
                %A%ContentDto content,
                CancellationToken cancellationToken = default);

        %end%
        %if update%
            Task<Result<%ReadDto%>> UpdateAsync(
                Guid %L%Id,
                %A%ContentDto content,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default);

        %end%
        %if delete%
            Task<Result> DeleteAsync(
                Guid %L%Id,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default);
        %end%
        }
        """;

    internal const string Service = """
        using %P%.Application.Core.Common.Collections; //?CollectionBinding
        using %P%.Application.Core.Common.Concurrency; //?Versioned|ConcurrencyErrors|VersionPrecondition
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.Validation; //?EnsureValidAsync
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto|%A%Dto|%A%SummaryDto|%A%ListQueryDto
        using %P%.Application.Features.%F%.Model.Errors; //?%A%Errors
        using %P%.Application.Features.%F%.Model.Policies; //?%A%CollectionPolicy
        using %P%.Application.Features.%F%.Ports.%A%Queries; //?I%A%Queries|%A%Filter
        using %P%.Application.Features.%F%.Ports.%A%Store; //?I%A%Store|%A%ChangeStatus
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using FluentValidation; //?IValidator

        namespace %P%.Application.Features.%F%.Services;

        internal sealed class %A%Service(
        %if rq%
            I%A%Queries queries,
        %end%
        %if wq%
            I%A%Store store,
            IUnitOfWork unitOfWork,
        %end%
        %if clock%
            TimeProvider timeProvider,
        %end%
        %if content%
            IValidator<%A%ContentDto> validator,
        %end%
            ICurrentUser currentUser) : I%A%Service
        {
        %if list%
            public async Task<Result<PagedResult<%A%SummaryDto>>> ListAsync(
                %A%ListQueryDto query,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(query);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<PagedResult<%A%SummaryDto>>();
                }

                var bound = CollectionBinding.Bind(
                    query,
                    %A%CollectionPolicy.Instance,
                    () => Result.Success(%A%Filter.None),
                    %DateKeys%);

                if (bound.IsFailure)
                {
                    return bound.To<PagedResult<%A%SummaryDto>>();
                }

                return await queries.%ListQuery%(%SvcOwner%bound.Value, cancellationToken);
            }

        %end%
        %if get%
            public async Task<Result<%ReadDto%>> GetAsync(Guid %L%Id, CancellationToken cancellationToken = default)
            {
                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<%ReadDto%>();
                }

                return await ReadAsync(%SvcOwner%%L%Id, cancellationToken);
            }

        %end%
        %if create%
            public async Task<Result<%ReadDto%>> CreateAsync(
                %A%ContentDto content,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(content);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<%ReadDto%>();
                }

                var validation = await validator.EnsureValidAsync(content, cancellationToken);

                if (validation.IsFailure)
                {
                    return validation.To<%ReadDto%>();
                }

                var %L%Id = Guid.CreateVersion7();

                store.Add(%SvcOwner%%L%Id, %Staged%, timeProvider.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);

                return await ReadAsync(%SvcOwner%%L%Id, cancellationToken);
            }

        %end%
        %if update%
            public async Task<Result<%ReadDto%>> UpdateAsync(
                Guid %L%Id,
                %A%ContentDto content,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(content);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<%ReadDto%>();
                }

                var validation = await validator.EnsureValidAsync(content, cancellationToken);

                if (validation.IsFailure)
                {
                    return validation.To<%ReadDto%>();
                }

                var status = await store.StageReplacementAsync(
        %if owner%
                    userId.Value,
        %end%
                    %L%Id,
                    %Staged%,
                    timeProvider.GetUtcNow(),
        %if etag%
                    precondition,
        %end%
                    cancellationToken);

                if (status != %A%ChangeStatus.Staged)
                {
                    return Result.Failure<%ReadDto%>(FailureOf(status, %L%Id));
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return await ReadAsync(%SvcOwner%%L%Id, cancellationToken);
            }

        %end%
        %if delete%
            public async Task<Result> DeleteAsync(
                Guid %L%Id,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default)
            {
                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId;
                }

                var status = await store.StageRemovalAsync(
        %if owner%
                    userId.Value,
        %end%
                    %L%Id,
        %if etag%
                    precondition,
        %end%
                    cancellationToken);

                if (status != %A%ChangeStatus.Staged)
                {
                    return Result.Failure(FailureOf(status, %L%Id));
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success();
            }

        %end%
        %if single%
            private async Task<Result<%ReadDto%>> ReadAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                CancellationToken cancellationToken)
            {
                var %L% = await queries.GetAsync(%OwnerArg%%L%Id, cancellationToken);

                return %L% is null
                    ? Result.Failure<%ReadDto%>(%A%Errors.%A%NotFound(%L%Id))
                    : %L%;
            }

        %end%
        %if trim&fields%
            private static %A%ContentDto Trimmed(%A%ContentDto content) => content with { %FTrimmed% };
        %end%
        %if trim&!fields%
            private static %A%ContentDto Trimmed(%A%ContentDto content) => content with { Name = content.Name.Trim() };

        %end%
        %if change%
            private static Error FailureOf(%A%ChangeStatus status, Guid %L%Id) => status switch
            {
                %A%ChangeStatus.NotFound => %A%Errors.%A%NotFound(%L%Id),
        %if etag%
                %A%ChangeStatus.VersionMismatch => ConcurrencyErrors.PreconditionFailed,
        %end%
                _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A staged change has no failure."),
            };
        %end%
        }
        """;

    internal const string ContentValidator = """
        using %P%.Application.Features.%F%.Model.Policies; //?%A%InputPolicy
        using FluentValidation;

        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed class %A%ContentDtoValidator : AbstractValidator<%A%ContentDto>
        {
            public %A%ContentDtoValidator()
            {
        %if fields%
        %FRulesContent%
        %else%
                RuleFor(content => content.Name)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage("%An% %L% name is required.")
                    .Must(name => name.Trim().Length <= %Limits%.NameMaxLength)
                    .WithMessage($"%An% %L% name cannot exceed {%Limits%.NameMaxLength} characters.");

                RuleFor(content => content.Description)
                    .Cascade(CascadeMode.Stop)
                    .NotNull().WithMessage("%An% %L% description is required; it may be empty.")
                    .MaximumLength(%Limits%.DescriptionMaxLength)
                    .WithMessage($"%An% %L% description cannot exceed {%Limits%.DescriptionMaxLength} characters.");
        %end%
            }
        }
        """;

    internal const string DtoMapping = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Domain.Features.%F%.Model.Entities;

        namespace %P%.Application.Features.%F%.Model.Mapping;

        internal static class %A%DtoMapping
        {
            public static Versioned<%A%Dto> Detail(%A% %L%)
            {
                ArgumentNullException.ThrowIfNull(%L%);

                var detail = new %A%Dto(
                    %L%.Id,
        %if fields%
        %FDetailArgs%
        %else%
                    %L%.Name,
                    %L%.Description,
        %end%
                    %L%.CreatedAt,
                    %L%.LastModifiedAt);

                return new Versioned<%A%Dto>(detail, %L%.Version);
            }
        }
        """;

    internal const string AccessServicePort = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Results;
        using %P%.Domain.Features.%F%.Model.Entities;

        namespace %P%.Application.Features.%F%.Services;

        public interface I%A%Service
        {
            Task<Result<%A%>> %Load%(
                Guid %L%Id,
                VersionPrecondition? precondition,
                CancellationToken cancellationToken = default);
        }
        """;

    internal const string AccessService = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Errors;
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Domain.Features.%F%.Repositories;

        namespace %P%.Application.Features.%F%.Services;

        internal sealed class %A%Service(I%A%Repository repository, ICurrentUser currentUser) : I%A%Service
        {
            public async Task<Result<%A%>> %Load%(
                Guid %L%Id,
                VersionPrecondition? precondition,
                CancellationToken cancellationToken = default)
            {
                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<%A%>();
                }

                var aggregate = await repository.GetAsync(%SvcOwner%%L%Id, cancellationToken);

                if (aggregate is null)
                {
                    return Result.Failure<%A%>(%A%Errors.%A%NotFound(%L%Id));
                }

                return precondition is not null && !precondition.IsSatisfiedBy(aggregate.Version)
                    ? Result.Failure<%A%>(ConcurrencyErrors.PreconditionFailed)
                    : Result.Success(aggregate);
            }
        }
        """;

    internal const string GetQueryValidator = """
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%A%;

        public sealed class Get%A%QueryValidator : AbstractValidator<Get%A%Query>
        {
            public Get%A%QueryValidator() =>
                RuleFor(query => query.%A%Id)
                    .NotEmpty().WithMessage("%An% %L% id is required.");
        }
        """;

    internal const string GetUseCase = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Errors;
        using %P%.Application.Features.%F%.Ports.%A%Queries;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%A%;

        public sealed record Get%A%Query(Guid %A%Id) : IQuery<Result<Versioned<%A%Dto>>>;

        public sealed class Get%A%QueryHandler(
            I%A%Queries queries,
            ICurrentUser currentUser) : IQueryHandler<Get%A%Query, Result<Versioned<%A%Dto>>>
        {
            public async ValueTask<Result<Versioned<%A%Dto>>> Handle(
                Get%A%Query query,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(query);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<Versioned<%A%Dto>>();
                }

                var found = await queries.GetAsync(%SvcOwner%query.%A%Id, cancellationToken);

                return found is null
                    ? Result.Failure<Versioned<%A%Dto>>(%A%Errors.%A%NotFound(query.%A%Id))
                    : found;
            }
        }
        """;

    internal const string ListUseCase = """
        using %P%.Application.Core.Common.Collections;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies;
        using %P%.Application.Features.%F%.Ports.%A%Queries;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%F%;

        public sealed record Get%F%Query(
            string? Paging,
            int? Page,
            int? PageSize,
            string? Cursor,
            string? Sort) : ICollectionQuery, IQuery<Result<PagedResult<%A%SummaryDto>>>
        {
            public static Get%F%Query Offset(int? page, int? pageSize) => new(null, page, pageSize, null, null);
        }

        public sealed class Get%F%QueryHandler(I%A%Queries queries, ICurrentUser currentUser)
            : IQueryHandler<Get%F%Query, Result<PagedResult<%A%SummaryDto>>>
        {
            public async ValueTask<Result<PagedResult<%A%SummaryDto>>> Handle(
                Get%F%Query query,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(query);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<PagedResult<%A%SummaryDto>>();
                }

                var bound = CollectionBinding.Bind(
                    query,
                    %A%CollectionPolicy.Instance,
                    () => Result.Success(%A%Filter.None),
                    %DateKeys%);

                if (bound.IsFailure)
                {
                    return bound.To<PagedResult<%A%SummaryDto>>();
                }

                return await queries.%ListQuery%(%SvcOwner%bound.Value, cancellationToken);
            }
        }
        """;

    internal const string CreateCommandValidator = """
        using %P%.Domain.Features.%F%.Model.Entities; //?%A%
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Create%A%;

        public sealed class Create%A%CommandValidator : AbstractValidator<Create%A%Command>
        {
            public Create%A%CommandValidator()
            {
        %if fields%
        %FRulesCommand%
        %else%
                RuleFor(command => command.Name)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage("%An% %L% name is required.")
                    .Must(name => name.Trim().Length <= %A%.NameMaxLength)
                    .WithMessage($"%An% %L% name cannot exceed {%A%.NameMaxLength} characters.");

                RuleFor(command => command.Description)
                    .Cascade(CascadeMode.Stop)
                    .NotNull().WithMessage("%An% %L% description is required; it may be empty.")
                    .MaximumLength(%A%.DescriptionMaxLength)
                    .WithMessage($"%An% %L% description cannot exceed {%A%.DescriptionMaxLength} characters.");
        %end%
            }
        }
        """;

    internal const string CreateUseCase = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.Validation;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Mapping;
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Domain.Features.%F%.Repositories;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Create%A%;

        %if fields%
        public sealed record Create%A%Command(
        %FParamList%) : ICommand<Result<Versioned<%A%Dto>>>;
        %else%
        public sealed record Create%A%Command(string Name, string Description) : ICommand<Result<Versioned<%A%Dto>>>;
        %end%

        public sealed class Create%A%CommandHandler(
            I%A%Repository repository,
            IUnitOfWork unitOfWork,
            ICurrentUser currentUser) : ICommandHandler<Create%A%Command, Result<Versioned<%A%Dto>>>
        {
            public async ValueTask<Result<Versioned<%A%Dto>>> Handle(
                Create%A%Command command,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(command);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<Versioned<%A%Dto>>();
                }

        %if fields%
                var created = DomainGuard.Try(() => %A%.Create(%SvcOwner%%FCommandArgs%));
        %else%
                var created = DomainGuard.Try(() => %A%.Create(%SvcOwner%command.Name, command.Description));
        %end%

                if (created.IsFailure)
                {
                    return created.To<Versioned<%A%Dto>>();
                }

                var %L% = created.Value;

                repository.Add(%L%);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                return %A%DtoMapping.Detail(%L%);
            }
        }
        """;

    internal const string UpdateCommandValidator = """
        using %P%.Domain.Features.%F%.Model.Entities; //?%A%
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Update%A%;

        public sealed class Update%A%CommandValidator : AbstractValidator<Update%A%Command>
        {
            public Update%A%CommandValidator()
            {
                RuleFor(command => command.%A%Id)
                    .NotEmpty().WithMessage("%An% %L% id is required.");

        %if fields%
        %FRulesCommand%
        %else%
                RuleFor(command => command.Name)
                    .Cascade(CascadeMode.Stop)
                    .NotEmpty().WithMessage("%An% %L% name is required.")
                    .Must(name => name.Trim().Length <= %A%.NameMaxLength)
                    .WithMessage($"%An% %L% name cannot exceed {%A%.NameMaxLength} characters.");

                RuleFor(command => command.Description)
                    .Cascade(CascadeMode.Stop)
                    .NotNull().WithMessage("%An% %L% description is required; it may be empty.")
                    .MaximumLength(%A%.DescriptionMaxLength)
                    .WithMessage($"%An% %L% description cannot exceed {%A%.DescriptionMaxLength} characters.");
        %end%
            }
        }
        """;

    internal const string UpdateUseCase = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.Validation;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Mapping;
        using %P%.Application.Features.%F%.Services;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Update%A%;

        public sealed record Update%A%Command(
            Guid %A%Id,
        %if fields%
        %FParamList%,
        %else%
            string Name,
            string Description,
        %end%
            VersionPrecondition? Precondition = null) : ICommand<Result<Versioned<%A%Dto>>>;

        public sealed class Update%A%CommandHandler(
            I%A%Service service,
            IUnitOfWork unitOfWork) : ICommandHandler<Update%A%Command, Result<Versioned<%A%Dto>>>
        {
            public async ValueTask<Result<Versioned<%A%Dto>>> Handle(
                Update%A%Command command,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(command);

                var loaded = await service.%Load%(command.%A%Id, command.Precondition, cancellationToken);

                if (loaded.IsFailure)
                {
                    return loaded.To<Versioned<%A%Dto>>();
                }

                var %L% = loaded.Value;

        %if fields%
                var update = DomainGuard.Try(() => %L%.Update(%FCommandArgs%));
        %else%
                var update = DomainGuard.Try(() => %L%.Update(command.Name, command.Description));
        %end%

                if (update.IsFailure)
                {
                    return update.To<Versioned<%A%Dto>>();
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return %A%DtoMapping.Detail(%L%);
            }
        }
        """;

    internal const string DeleteCommandValidator = """
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Delete%A%;

        public sealed class Delete%A%CommandValidator : AbstractValidator<Delete%A%Command>
        {
            public Delete%A%CommandValidator() =>
                RuleFor(command => command.%A%Id)
                    .NotEmpty().WithMessage("%An% %L% id is required.");
        }
        """;

    internal const string DeleteUseCase = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Services;
        using %P%.Domain.Features.%F%.Repositories;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Delete%A%;

        public sealed record Delete%A%Command(Guid %A%Id, VersionPrecondition? Precondition = null) : ICommand<Result>;

        public sealed class Delete%A%CommandHandler(
            I%A%Service service,
            I%A%Repository repository,
            IUnitOfWork unitOfWork) : ICommandHandler<Delete%A%Command, Result>
        {
            public async ValueTask<Result> Handle(
                Delete%A%Command command,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(command);

                var loaded = await service.%Load%(command.%A%Id, command.Precondition, cancellationToken);

                if (loaded.IsFailure)
                {
                    return loaded;
                }

                repository.Remove(loaded.Value);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                return Result.Success();
            }
        }
        """;

    internal const string RecordPlain = """
        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        internal sealed class %A%Record
        {
            public Guid Id { get; set; }

        %if owner%
            public Guid OwnerId { get; set; }

        %end%
        %if fields%
        %FRecordProperties%
        %else%
            public string Name { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;
        %end%

            public DateTimeOffset CreatedAt { get; set; }

            public DateTimeOffset? LastModifiedAt { get; set; }
        %if etag%

            public uint Version { get; set; }
        %end%
        }
        """;

    internal const string RecordAudited = """
        using %P%.Domain.Core.Common.Abstractions;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        internal sealed class %A%Record : IAuditable
        {
            public Guid Id { get; set; }

        %if owner%
            public Guid OwnerId { get; set; }

        %end%
        %if fields%
        %FRecordProperties%
        %else%
            public string Name { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;
        %end%

            public uint Version { get; set; }

            public DateTimeOffset CreatedAt { get; set; }

            public Guid? CreatedBy { get; set; }

            public DateTimeOffset? LastModifiedAt { get; set; }

            public Guid? LastModifiedBy { get; set; }

            void IAuditable.SetCreated(DateTimeOffset at, Guid? by)
            {
                CreatedAt = at;
                CreatedBy = by;
            }

            void IAuditable.SetLastModified(DateTimeOffset at, Guid? by)
            {
                LastModifiedAt = at;
                LastModifiedBy = by;
            }
        }
        """;

    internal const string Configuration = """
        %if rec%
        using %P%.Application.Features.%F%.Model.Policies; //?%A%InputPolicy
        %else%
        using %P%.Domain.Features.%F%.Model.Entities; //?%A%
        %end%
        using %P%.Infrastructure.Core.Common.Contexts; //?IsVersion
        using %P%.Infrastructure.Persistence.Common.Contexts;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata.Builders;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Configurations;

        internal sealed class %A%RecordConfiguration : IEntityTypeConfiguration<%A%Record>
        {
            public void Configure(EntityTypeBuilder<%A%Record> builder)
            {
                ArgumentNullException.ThrowIfNull(builder);

                builder.ToTable("%F%", AppDbContext.%F%Schema);

                builder.HasKey(record => record.Id);

                builder.Property(record => record.Id).ValueGeneratedNever();

        %if owner%
                builder.Property(record => record.OwnerId).IsRequired();

        %end%
        %if list%
        %if owner%
        %if fields%
        %FIndexes%
        %else%
                builder.HasIndex(record => new { record.OwnerId, record.Name, record.Id })
                    .HasDatabaseName("IX_%F%_OwnerId_Name_Id");
        %end%

                builder.HasIndex(record => new { record.OwnerId, record.CreatedAt, record.Id })
                    .HasDatabaseName("IX_%F%_OwnerId_CreatedAt_Id");
        %else%
        %if fields%
        %FIndexes%
        %else%
                builder.HasIndex(record => new { record.Name, record.Id })
                    .HasDatabaseName("IX_%F%_Name_Id");
        %end%

                builder.HasIndex(record => new { record.CreatedAt, record.Id })
                    .HasDatabaseName("IX_%F%_CreatedAt_Id");
        %end%

        %else%
        %if owner%
                builder.HasIndex(record => record.OwnerId)
                    .HasDatabaseName("IX_%F%_OwnerId");

        %end%
        %end%
        %if fields%
        %FConfiguration%
        %else%
                builder.Property(record => record.Name)
                    .HasMaxLength(%Limits%.NameMaxLength)
                    .IsRequired();

                builder.Property(record => record.Description)
                    .HasMaxLength(%Limits%.DescriptionMaxLength)
                    .IsRequired();
        %end%

                builder.Property(record => record.CreatedAt).IsRequired();
        %if agg%
                builder.Property(record => record.CreatedBy);
        %end%
                builder.Property(record => record.LastModifiedAt);
        %if agg%
                builder.Property(record => record.LastModifiedBy);
        %end%
        %if etag%

                builder.Property(record => record.Version).IsVersion();
        %end%
            }
        }
        """;

    internal const string Store = """
        using %P%.Application.Core.Common.Concurrency; //?VersionPrecondition
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto
        using %P%.Application.Features.%F%.Ports.%A%Store;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Infrastructure.Persistence.Common.Contexts;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;
        using Microsoft.EntityFrameworkCore; //?SingleOrDefaultAsync|ToListAsync

        namespace %P%.Infrastructure.Persistence.Features.%F%.Access.Stores;

        internal sealed class %A%Store(AppDbContext context) : I%A%Store
        {
        %if create%
            public void Add(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                %A%ContentDto content,
                DateTimeOffset createdAt)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);
        %end%
                ArgumentNullException.ThrowIfNull(content);

                context.%F%.Add(new %A%Record
                {
                    Id = %L%Id,
        %if owner%
                    OwnerId = ownerId.Value,
        %end%
        %if fields%
        %FStoreAssign%
        %else%
                    Name = content.Name,
                    Description = content.Description,
        %end%
                    CreatedAt = createdAt,
                });
            }

        %end%
        %if update%
            public async Task<%A%ChangeStatus> StageReplacementAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                %A%ContentDto content,
                DateTimeOffset modifiedAt,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);
        %end%
                ArgumentNullException.ThrowIfNull(content);

                var (status, record) = await LoadForChangeAsync(
        %if owner%
                    ownerId,
        %end%
                    %L%Id,
        %if etag%
                    precondition,
        %end%
                    cancellationToken);

                if (record is not null)
                {
        %if fields%
        %FStoreUpdate%
        %else%
                    record.Name = content.Name;
                    record.Description = content.Description;
        %end%
                    record.LastModifiedAt = modifiedAt;
                }

                return status;
            }

        %end%
        %if delete%
            public async Task<%A%ChangeStatus> StageRemovalAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken = default)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);

        %end%
                var (status, record) = await LoadForChangeAsync(
        %if owner%
                    ownerId,
        %end%
                    %L%Id,
        %if etag%
                    precondition,
        %end%
                    cancellationToken);

                if (record is not null)
                {
                    context.%F%.Remove(record);
                }

                return status;
            }

        %end%
        %if change%
            private async Task<(%A%ChangeStatus Status, %A%Record? Record)> LoadForChangeAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
        %if etag%
                VersionPrecondition? precondition,
        %end%
                CancellationToken cancellationToken)
            {
                var tracked = await context.%F%
                    .SingleOrDefaultAsync(record => record.Id == %L%Id%OwnerClause%, cancellationToken);

                if (tracked is null)
                {
                    return (%A%ChangeStatus.NotFound, null);
                }

        %if etag%
                return precondition is not null && !precondition.IsSatisfiedBy(tracked.Version)
                    ? (%A%ChangeStatus.VersionMismatch, null)
                    : (%A%ChangeStatus.Staged, tracked);
        %else%
                return (%A%ChangeStatus.Staged, tracked);
        %end%
            }
        %end%
        %if purge%

            public async Task<int> StageRemovalForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(ownerId);

                var records = await context.%F%
                    .Where(record => record.OwnerId == ownerId.Value)
                    .ToListAsync(cancellationToken);

                context.%F%.RemoveRange(records);

                return records.Count;
            }
        %end%
        %if anon%

            public async Task<int> StageAnonymisationForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(ownerId);

                var records = await context.%F%
                    .Where(record => record.OwnerId == ownerId.Value)
                    .ToListAsync(cancellationToken);

                foreach (var record in records)
                {
        %if fields%
        %FStoreAnonymise%
        %else%
                    record.Name = AnonymisedText;
                    record.Description = string.Empty;
        %end%
                }

                return records.Count;
            }
        %if anontext%

            private const string AnonymisedText = "[deleted account]";
        %end%
        %end%
        }
        """;

    internal const string MapperPort = """
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;

        internal interface I%A%Mapper
        {
            %A% ToAggregate(%A%Record record);

            %A%Record ToNewRecord(%A% aggregate);

            void WriteTo(%A% aggregate, %A%Record record);
        }
        """;

    internal const string Mapper = """
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Core.Common.Saving.Tracking;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;

        internal sealed class %A%Mapper : I%A%Mapper
        {
            public %A% ToAggregate(%A%Record record)
            {
                ArgumentNullException.ThrowIfNull(record);

                var aggregate = %A%.Rehydrate(
                    record.Id,
        %if owner%
                    UserId.Create(record.OwnerId),
        %end%
        %if fields%
        %FMapperRehydrate%
        %else%
                    record.Name,
                    record.Description);
        %end%

                StoredStamps.ApplyTo(aggregate, record, record.Version, record.Id, "%A%");

                return aggregate;
            }

            public %A%Record ToNewRecord(%A% aggregate)
            {
                ArgumentNullException.ThrowIfNull(aggregate);

                return new %A%Record
                {
                    Id = aggregate.Id,
        %if owner%
                    OwnerId = aggregate.OwnerId.Value,
        %end%
        %if fields%
        %FMapperNew%
        %else%
                    Name = aggregate.Name,
                    Description = aggregate.Description,
        %end%

                    Version = aggregate.Version,

                    CreatedAt = aggregate.CreatedAt,
                    CreatedBy = aggregate.CreatedBy,
                    LastModifiedAt = aggregate.LastModifiedAt,
                    LastModifiedBy = aggregate.LastModifiedBy,
                };
            }

            public void WriteTo(%A% aggregate, %A%Record record)
            {
                ArgumentNullException.ThrowIfNull(aggregate);
                ArgumentNullException.ThrowIfNull(record);

        %if fields%
        %FMapperWrite%
        %else%
                record.Name = aggregate.Name;
                record.Description = aggregate.Description;
        %end%
            }
        }
        """;

    internal const string TrackerPort = """
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Core.Common.Saving.DomainEvents;
        using %P%.Infrastructure.Core.Common.Saving.Tracking;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Tracking;

        internal interface I%A%Tracker : IAggregateFlusher, IDomainEventSource
        {
            %A%? Find(Guid id);

            void Track(%A% aggregate, %A%Record record);

            %A%Record? FindRecord(Guid id);

            void MarkRemoved(%A% aggregate, %A%Record record);
        }
        """;

    internal const string Tracker = """
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Core.Common.Saving.Tracking;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;
        using Microsoft.EntityFrameworkCore;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Rows.Tracking;

        internal sealed class %A%Tracker(I%A%Mapper mapper)
            : AggregateTracker<%A%, %A%Record>(record => record.Version), I%A%Tracker
        {
            public override void FlushTo(DbContext context)
            {
                ArgumentNullException.ThrowIfNull(context);

                foreach (var tracked in TrackedAggregates)
                {
                    var entry = context.Entry(tracked.Record);

                    if (tracked.IsRemoved || entry.State is EntityState.Deleted or EntityState.Detached)
                    {
                        continue;
                    }

                    mapper.WriteTo(tracked.Aggregate, tracked.Record);

                    if (entry.State != EntityState.Added)
                    {
                        entry.Property(record => record.Version).OriginalValue = tracked.Aggregate.Version;
                    }
                }
            }
        }
        """;

    internal const string Repository = """
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Domain.Features.%F%.Repositories;
        using %P%.Infrastructure.Persistence.Common.Contexts;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models; //?%A%Record
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Tracking;
        using Microsoft.EntityFrameworkCore; //?FirstOrDefaultAsync|ToListAsync

        namespace %P%.Infrastructure.Persistence.Features.%F%.Access.Repositories;

        internal sealed class %A%Repository(
            AppDbContext context,
            I%A%Mapper mapper,
            I%A%Tracker tracker) : I%A%Repository
        {
        %if change%
        %if owner%
            public async Task<%A%?> GetAsync(UserId ownerId, Guid id, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(ownerId);

                if (tracker.Find(id) is { } alreadyLoaded)
                {
                    return alreadyLoaded.OwnerId == ownerId ? alreadyLoaded : null;
                }

                var record = await context.%F%.FirstOrDefaultAsync(
                    candidate => candidate.Id == id && candidate.OwnerId == ownerId.Value,
                    cancellationToken);

                return record is null ? null : LoadOrTrack(record);
            }
        %else%
            public async Task<%A%?> GetAsync(Guid id, CancellationToken cancellationToken)
            {
                if (tracker.Find(id) is { } alreadyLoaded)
                {
                    return alreadyLoaded;
                }

                var record = await context.%F%.FirstOrDefaultAsync(
                    candidate => candidate.Id == id,
                    cancellationToken);

                return record is null ? null : LoadOrTrack(record);
            }
        %end%

        %end%
        %if owner%
            public async Task<IReadOnlyList<%A%>> GetForOwnerAsync(
                UserId ownerId,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(ownerId);

                var records = await context.%F%
                    .Where(record => record.OwnerId == ownerId.Value)
                    .ToListAsync(cancellationToken);

                var aggregates = new List<%A%>(records.Count);

                foreach (var record in records)
                {
                    aggregates.Add(LoadOrTrack(record));
                }

                return aggregates;
            }

        %end%
        %if create%
            public void Add(%A% aggregate)
            {
                ArgumentNullException.ThrowIfNull(aggregate);

                var record = mapper.ToNewRecord(aggregate);

                context.%F%.Add(record);
                tracker.Track(aggregate, record);
            }

        %end%
        %if delete|owner%
            public void Remove(%A% aggregate)
            {
                ArgumentNullException.ThrowIfNull(aggregate);

                if (tracker.FindRecord(aggregate.Id) is not { } record)
                {
                    return;
                }

                context.%F%.Remove(record);
                tracker.MarkRemoved(aggregate, record);
            }

        %end%
        %if change|owner%
            private %A% LoadOrTrack(%A%Record record)
            {
                var aggregate = mapper.ToAggregate(record);

                tracker.Track(aggregate, record);

                return aggregate;
            }
        %end%
        }
        """;

    internal const string AccountDeletedConsumer = """
        using %P%.Application.Core.Common.Events; //?UserAccountDeleted
        using %P%.Application.Core.Common.Ports; //?IDeletedAccountDataPolicy|IUserAccountDirectory|IUnitOfWork
        using %P%.Domain.Core.Common.Primitives;
        using %P%.Application.Features.%F%.Ports.%A%Queries;
        %if rec%
        using %P%.Application.Features.%F%.Ports.%A%Store;
        %end%
        %if agg%
        using %P%.Domain.Features.%F%.Repositories;
        %end%

        namespace %P%.Application.Features.%F%.Services.Consumers;

        internal sealed class %Verb%%A%OnAccountDeletedConsumer(
        %if rec%
            I%A%Store store,
        %end%
        %if agg%
            I%A%Repository repository,
        %end%
            I%A%Queries queries,
            IUserAccountDirectory accounts,
            IUnitOfWork unitOfWork) : IDomainEventConsumer<UserAccountDeleted>, IDeletedAccountDataPolicy
        {
            public Task ConsumeAsync(UserAccountDeleted domainEvent, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(domainEvent);

                return ApplyAsync(domainEvent.UserId, cancellationToken);
            }

            public async Task<bool> ApplyAsync(UserId owner, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(owner);

                if (await accounts.ExistsAsync(owner, cancellationToken))
                {
                    return false;
                }

        %if purge%
        %if rec%
                int staged = await store.StageRemovalForOwnerAsync(owner, cancellationToken);

                if (staged == 0)
                {
                    return false;
                }
        %end%
        %if agg%
                var owned = await repository.GetForOwnerAsync(owner, cancellationToken);

                if (owned.Count == 0)
                {
                    return false;
                }

                foreach (var aggregate in owned)
                {
                    repository.Remove(aggregate);
                }
        %end%
        %end%
        %if anon%
        %if rec%
                int staged = await store.StageAnonymisationForOwnerAsync(owner, cancellationToken);

                if (staged == 0)
                {
                    return false;
                }
        %end%
        %if agg%
                var owned = await repository.GetForOwnerAsync(owner, cancellationToken);

                if (owned.Count == 0)
                {
                    return false;
                }

                foreach (var aggregate in owned)
                {
                    aggregate.Anonymise();
                }
        %end%
        %end%

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return true;
            }

            public Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
                CancellationToken cancellationToken = default) =>
                queries.ListDataOwnerIdsAsync(cancellationToken);
        }
        """;

    internal const string DeletedAccountDataPolicy = """
        using %P%.Domain.Core.Common.Primitives;

        namespace %P%.Application.Core.Common.Ports;

        public interface IDeletedAccountDataPolicy
        {
            Task<bool> ApplyAsync(UserId owner, CancellationToken cancellationToken = default);

            Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
        }
        """;

    internal const string UserAccountDirectory = """
        using %P%.Domain.Core.Common.Primitives;

        namespace %P%.Application.Core.Common.Ports;

        public interface IUserAccountDirectory
        {
            Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default);
        }
        """;

    internal const string SweepUseCase = """
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using Mediator;
        using Microsoft.Extensions.Logging;

        namespace %P%.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;

        public sealed record SweepDeletedAccountDataCommand : ICommand<Result<int>>;

        public sealed class SweepDeletedAccountDataCommandHandler(
            IEnumerable<IDeletedAccountDataPolicy> policies,
            ILeaderLease lease,
            ILogger<SweepDeletedAccountDataCommandHandler> logger) : ICommandHandler<SweepDeletedAccountDataCommand, Result<int>>
        {
            private const string _leaseName = "apptemplate.accounts.sweep-deleted-data";

            public async ValueTask<Result<int>> Handle(SweepDeletedAccountDataCommand command, CancellationToken cancellationToken)
            {
                int swept = 0;

                bool ranHere = await lease.TryRunExclusivelyAsync(
                    _leaseName,
                    async leaseToken => swept = await SweepAsync(leaseToken),
                    cancellationToken);

                if (!ranHere && logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Deleted-account data sweep skipped: another host holds the '{LeaseName}' lease.",
                        _leaseName);
                }

                return swept;
            }

            private async Task<int> SweepAsync(CancellationToken cancellationToken)
            {
                int swept = 0;

                foreach (var policy in policies)
                {
                    var owners = await policy.ListDataOwnerIdsAsync(cancellationToken);

                    foreach (var owner in owners)
                    {
                        try
                        {
                            if (await policy.ApplyAsync(owner, cancellationToken))
                            {
                                swept++;
                            }
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            logger.LogError(
                                exception,
                                "Sweeping the data of deleted account {Owner} through {Policy} failed; "
                                + "the owner is skipped and retried at the next pass.",
                                owner.Value,
                                policy.GetType().Name);
                        }
                    }
                }

                return swept;
            }
        }
        """;

    internal const string DeletedAccountSweepJobs = """
        using %J%.Features.Maintenance.Jobs;
        using %P%.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;
        using Microsoft.Extensions.Options;

        namespace %J%.Features.BackgroundJobs.Jobs;

        internal static class DeletedAccountSweepJobsExtensions
        {
            internal static IServiceCollection AddDeletedAccountSweepJobs(
                this IServiceCollection services,
                IConfiguration configuration)
            {
                ArgumentNullException.ThrowIfNull(services);
                ArgumentNullException.ThrowIfNull(configuration);

                services.AddOptions<DeletedAccountSweepOptions>()
                    .Bind(configuration.GetSection(JobsMaintenanceOptions.SectionName));

                services.AddMaintenanceTask<SweepDeletedAccountDataCommand>(
                    "data of deleted accounts",
                    "Rows owned by an id that names no account stay where they are: the deletion event is "
                    + "the fast path, and this sweep is what makes it certain.",
                    provider => provider.GetRequiredService<IOptions<DeletedAccountSweepOptions>>()
                        .Value.SweepDeletedAccountsDataEnabled);

                return services;
            }
        }
        """;

    internal const string DeletedAccountSweepOptions = """
        namespace %J%.Features.BackgroundJobs.Jobs;

        internal sealed class DeletedAccountSweepOptions
        {
            public bool SweepDeletedAccountsDataEnabled { get; set; } = true;
        }
        """;

    internal const string LocalUserAccountDirectory = """
        using %P%.Application.Auth.Features.Accounts.Ports.Accounts;
        using %P%.Application.Core.Common.Ports;
        using %P%.Domain.Core.Common.Primitives;

        namespace %H%.Features.Auth.Composition;

        internal sealed class AccountUserAccountDirectory(IAccountsService accounts) : IUserAccountDirectory
        {
            public async Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(userId);

                return await accounts.FindProfileAsync(userId.Value, cancellationToken) is not null;
            }
        }
        """;

    internal const string ExternalUserAccountDirectory = """
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Features.ExternalIdentity.Services;
        using %P%.Domain.Core.Common.Primitives;

        namespace %H%.Features.ExternalIdentity.Composition;

        internal sealed class ExternalIdentityUserAccountDirectory(
            IExternalIdentityService externalIdentities) : IUserAccountDirectory
        {
            public async Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(userId);

                return await externalIdentities.FindProfileAsync(userId, cancellationToken) is not null;
            }
        }
        """;

    internal const string Queries = """
        using System.Linq.Expressions; //?Expression
        using %P%.Application.Core.Common.Collections; //?FeaturePageRequest
        using %P%.Application.Core.Common.Concurrency; //?Versioned
        using %P%.Application.Core.Common.Results; //?PagedResult
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%SummaryDto|%A%Dto
        using %P%.Application.Features.%F%.Ports.%A%Queries;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Infrastructure.Persistence.Common.Contexts;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models; //?%A%Record
        using Microsoft.EntityFrameworkCore;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Access.Queries;

        internal sealed class %A%Queries(AppDbContext context) : I%A%Queries
        {
        %if list%
            public async Task<PagedResult<%A%SummaryDto>> %ListQuery%(
        %if owner%
                UserId ownerId,
        %end%
                FeaturePageRequest<%A%Filter> request,
                CancellationToken cancellationToken = default)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);
        %end%
                ArgumentNullException.ThrowIfNull(request);

                IQueryable<%A%Record> scoped = context.%F%.AsNoTracking();
        %if owner%
                scoped = scoped.Where(record => record.OwnerId == ownerId.Value);
        %end%

                return request.Paging.Mode == PagingMode.Offset
                    ? await GetOffsetPageAsync(scoped, request, cancellationToken)
                    : await GetKeysetPageAsync(scoped, request, cancellationToken);
            }

        %end%
        %if single%
            public Task<%ReadDto%?> GetAsync(
        %if owner%
                UserId ownerId,
        %end%
                Guid %L%Id,
                CancellationToken cancellationToken = default)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);

        %end%
                return context.%F%
                    .AsNoTracking()
                    .Where(record => record.Id == %L%Id%OwnerClause%)
        %if etag%
                    .Select(record => new Versioned<%A%Dto>(
        %if fields%
                        new %A%Dto(record.Id, %FRecordArgs%, record.CreatedAt, record.LastModifiedAt),
        %else%
                        new %A%Dto(record.Id, record.Name, record.Description, record.CreatedAt, record.LastModifiedAt),
        %end%
                        record.Version))
        %else%
                    .Select(record => new %A%Dto(
                        record.Id,
        %if fields%
        %FRecordArgLines%
        %else%
                        record.Name,
                        record.Description,
        %end%
                        record.CreatedAt,
                        record.LastModifiedAt))
        %end%
                    .FirstOrDefaultAsync(cancellationToken);
            }

        %end%
        %if owner%
            public async Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(
                CancellationToken cancellationToken = default)
            {
                var ids = await context.%F%
                    .AsNoTracking()
                    .Select(record => record.OwnerId)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                return [.. ids.Select(UserId.Create)];
            }

        %end%
        %if list%
            private static async Task<PagedResult<%A%SummaryDto>> GetOffsetPageAsync(
                IQueryable<%A%Record> scoped,
                FeaturePageRequest<%A%Filter> request,
                CancellationToken cancellationToken)
            {
                int totalCount = await scoped.CountAsync(cancellationToken);

                int page = request.Paging.Page!.Value;
                int pageSize = request.Paging.PageSize;

                var items = await %A%SortMap.ApplyOrder(scoped, request.Sort)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(_toSummary)
                    .ToListAsync(cancellationToken);

                return PagedResult.Offset(items, page, pageSize, totalCount);
            }

            private static async Task<PagedResult<%A%SummaryDto>> GetKeysetPageAsync(
                IQueryable<%A%Record> scoped,
                FeaturePageRequest<%A%Filter> request,
                CancellationToken cancellationToken)
            {
                var term = request.Sort.Terms[0];
                int pageSize = request.Paging.PageSize;

                var keysetSource = request.Paging.Cursor is { } cursor
                    ? %A%SortMap.ApplyKeyset(scoped, term, cursor)
                    : scoped;

                var items = await %A%SortMap.ApplyOrder(keysetSource, request.Sort)
                    .Take(pageSize + 1)
                    .Select(_toSummary)
                    .ToListAsync(cancellationToken);

                bool hasNext = items.Count > pageSize;
                var page = hasNext ? items.GetRange(0, pageSize) : items;

                string? nextCursor = null;

                if (hasNext)
                {
                    var last = page[^1];

                    nextCursor = Cursor.After(term, %A%SortMap.KeyOf(last, term.Field), last.Id).Encode();
                }

                return PagedResult.Keyset(page, pageSize, nextCursor);
            }

            private static readonly Expression<Func<%A%Record, %A%SummaryDto>> _toSummary =
        %if fields%
                record => new %A%SummaryDto(record.Id, %FSummaryArgs%record.CreatedAt, record.LastModifiedAt);
        %else%
                record => new %A%SummaryDto(record.Id, record.Name, record.CreatedAt, record.LastModifiedAt);
        %end%
        %end%
        }
        """;

    internal const string SortMap = """
        using System.Globalization;
        using %P%.Application.Core.Common.Collections;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Access.Queries;

        internal static class %A%SortMap
        {
            public static IOrderedQueryable<%A%Record> ApplyOrder(IQueryable<%A%Record> source, SortOrder sort)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(sort);

                IOrderedQueryable<%A%Record>? ordered = null;

                foreach (var term in sort.Terms)
                {
                    ordered = ordered is null ? OrderByTerm(source, term) : ThenByTerm(ordered, term);
                }

                return sort.Terms[^1].Direction == SortDirection.Ascending
                    ? ordered!.ThenBy(record => record.Id)
                    : ordered!.ThenByDescending(record => record.Id);
            }

            public static IQueryable<%A%Record> ApplyKeyset(IQueryable<%A%Record> source, SortTerm term, Cursor cursor)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(term);
                ArgumentNullException.ThrowIfNull(cursor);

                return term.Field switch
                {
                    %A%CollectionPolicy.NameField => ApplyNameKeyset(source, term.Direction, cursor),
                    %A%CollectionPolicy.CreatedAtField => ApplyCreatedAtKeyset(source, term.Direction, cursor),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' has no keyset comparison: the policy should have rejected it first."),
                };
            }

            public static string KeyOf(%A%SummaryDto summary, string field)
            {
                ArgumentNullException.ThrowIfNull(summary);

                return field switch
                {
                    %A%CollectionPolicy.NameField => summary.Name,
                    %A%CollectionPolicy.CreatedAtField => summary.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                    _ => throw new InvalidOperationException(
                        $"'{field}' has no cursor key: the policy should have rejected it first."),
                };
            }

            private static IOrderedQueryable<%A%Record> OrderByTerm(IQueryable<%A%Record> source, SortTerm term) =>
                term.Field switch
                {
                    %A%CollectionPolicy.NameField => term.Direction == SortDirection.Ascending
                        ? source.OrderBy(record => record.Name)
                        : source.OrderByDescending(record => record.Name),
                    %A%CollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                        ? source.OrderBy(record => record.CreatedAt)
                        : source.OrderByDescending(record => record.CreatedAt),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' is not a sortable field: the policy should have rejected it first."),
                };

            private static IOrderedQueryable<%A%Record> ThenByTerm(IOrderedQueryable<%A%Record> source, SortTerm term) =>
                term.Field switch
                {
                    %A%CollectionPolicy.NameField => term.Direction == SortDirection.Ascending
                        ? source.ThenBy(record => record.Name)
                        : source.ThenByDescending(record => record.Name),
                    %A%CollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                        ? source.ThenBy(record => record.CreatedAt)
                        : source.ThenByDescending(record => record.CreatedAt),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' is not a sortable field: the policy should have rejected it first."),
                };

            private static IQueryable<%A%Record> ApplyNameKeyset(
                IQueryable<%A%Record> source,
                SortDirection direction,
                Cursor cursor) =>
                direction == SortDirection.Ascending
                    ? source.Where(record =>
                        record.Name.CompareTo(cursor.Key) > 0
                        || (record.Name == cursor.Key && record.Id.CompareTo(cursor.Id) > 0))
                    : source.Where(record =>
                        record.Name.CompareTo(cursor.Key) < 0
                        || (record.Name == cursor.Key && record.Id.CompareTo(cursor.Id) < 0));

            private static IQueryable<%A%Record> ApplyCreatedAtKeyset(
                IQueryable<%A%Record> source,
                SortDirection direction,
                Cursor cursor)
            {
                if (!DateTimeOffset.TryParse(
                    cursor.Key,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset key))
                {
                    throw new InvalidOperationException(
                        "The cursor's key is not a valid date/time: CursorKeys should have rejected it first.");
                }

                return direction == SortDirection.Ascending
                    ? source.Where(record =>
                        record.CreatedAt.CompareTo(key) > 0
                        || (record.CreatedAt == key && record.Id.CompareTo(cursor.Id) > 0))
                    : source.Where(record =>
                        record.CreatedAt.CompareTo(key) < 0
                        || (record.CreatedAt == key && record.Id.CompareTo(cursor.Id) < 0));
            }
        }
        """;

    internal const string CreateRequest = """
        using System.Text.Json.Serialization; //?JsonRequired

        namespace %P%.Api.Features.%F%.Contracts.Requests;

        %if owner%
        /// <summary>The %L% to create, whose owner is the authenticated caller.</summary>
        %else%
        /// <summary>The %L% to create.</summary>
        %end%
        %if fields%
        %FRequestDocs%
        public sealed record Create%A%Request(
        %FRequestParams%);
        %else%
        /// <param name="Name">Required, at most 200 characters once trimmed.</param>
        /// <param name="Description">Required but may be empty, at most 2000 characters.</param>
        public sealed record Create%A%Request(string Name, string Description);
        %end%
        """;

    internal const string UpdateRequest = """
        using System.Text.Json.Serialization; //?JsonRequired

        namespace %P%.Api.Features.%F%.Contracts.Requests;

        /// <summary>The new %Revised% of %a% %L%, whose id travels in the route.</summary>
        %if fields%
        %FRequestDocs%
        public sealed record Update%A%Request(
        %FRequestParams%);
        %else%
        /// <param name="Name">Required, at most 200 characters once trimmed.</param>
        /// <param name="Description">Required but may be empty, at most 2000 characters.</param>
        public sealed record Update%A%Request(string Name, string Description);
        %end%
        """;

    internal const string ListRequest = """
        namespace %P%.Api.Features.%F%.Contracts.Requests;

        /// <summary>The paging and sorting of a list, bound from the query string.</summary>
        /// <param name="Paging"><c>offset</c> (the default) or <c>cursor</c>.</param>
        /// <param name="Page">The page number, in offset mode.</param>
        /// <param name="PageSize">The number of items per page.</param>
        /// <param name="Cursor">The previous page's <c>nextCursor</c>, in cursor mode.</param>
        /// <param name="Sort">Comma-separated fields (%SortDoc%), each optionally suffixed <c>:asc</c> or <c>:desc</c>; at most one in cursor mode.</param>
        public sealed record Get%F%Request(
            string? Paging,
            int? Page,
            int? PageSize,
            string? Cursor,
            string? Sort);
        """;

    internal const string Response = """
        namespace %P%.Api.Features.%F%.Contracts.Responses;

        %if etag%
        /// <summary>The wire shape of one %L%, whose version travels in the <c>ETag</c> header.</summary>
        %else%
        /// <summary>The wire shape of one %L%.</summary>
        %end%
        public sealed record %A%Response(
            Guid Id,
        %if fields%
        %FDtoParams%
        %else%
            string Name,
            string Description,
        %end%
            DateTimeOffset CreatedAt,
            DateTimeOffset? LastModifiedAt);
        """;

    internal const string SummaryResponse = """
        namespace %P%.Api.Features.%F%.Contracts.Responses;

        /// <summary>The wire shape of one %L% inside a page%SummaryDoc%.</summary>
        public sealed record %A%SummaryResponse(
            Guid Id,
        %if fields%
        %FSummaryParams%
        %else%
            string Name,
        %end%
            DateTimeOffset CreatedAt,
            DateTimeOffset? LastModifiedAt);
        """;

    internal const string ResponseMapping = """
        using %P%.Api.Common.Contracts; //?PagedResponse
        using %P%.Api.Features.%F%.Contracts.Responses;
        using %P%.Application.Core.Common.Concurrency; //?Versioned
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos;

        namespace %P%.Api.Features.%F%.Contracts.Mapping;

        internal static class %A%ResponseMapping
        {
        %if dto%
            public static %A%Response ToResponse(%A%Dto %L%)
            {
                ArgumentNullException.ThrowIfNull(%L%);

        %if fields%
                return new %A%Response(%L%.Id, %FResponseArgs%%L%.CreatedAt, %L%.LastModifiedAt);
        %else%
                return new %A%Response(%L%.Id, %L%.Name, %L%.Description, %L%.CreatedAt, %L%.LastModifiedAt);
        %end%
            }

        %end%
        %if list%
            public static %A%SummaryResponse ToResponse(%A%SummaryDto summary)
            {
                ArgumentNullException.ThrowIfNull(summary);

        %if fields%
                return new %A%SummaryResponse(summary.Id, %FSummaryResponseArgs%summary.CreatedAt, summary.LastModifiedAt);
        %else%
                return new %A%SummaryResponse(summary.Id, summary.Name, summary.CreatedAt, summary.LastModifiedAt);
        %end%
            }

            public static Result<PagedResponse<%A%SummaryResponse>> ToPageResponse(
                Result<PagedResult<%A%SummaryDto>> result) =>
                result.Map(value => PagedResponse.From(value, ToResponse));

        %end%
        %if dto%
        %if etag%
            public static Result<%ReadResponse%> To%A%Response(Result<%ReadDto%> result) =>
                result.Map(value => new Versioned<%A%Response>(ToResponse(value.Value), value.Version));
        %else%
            public static Result<%ReadResponse%> To%A%Response(Result<%ReadDto%> result) =>
                result.Map(ToResponse);
        %end%
        %end%
        }
        """;

    internal const string Controller = """
        using %P%.Api.Common.Contracts; //?PagedResponse
        using %P%.Api.Common.Controllers;
        using %P%.Api.Common.Errors; //?ToActionResult
        using %P%.Api.Common.Idempotency; //?Idempotent
        using %P%.Api.Features.%F%.Contracts.Mapping; //?%A%ResponseMapping
        using %P%.Api.Features.%F%.Contracts.Requests; //?Create%A%Request|Update%A%Request|Get%F%Request
        using %P%.Api.Features.%F%.Contracts.Responses; //?%A%Response|%A%SummaryResponse
        %if rec%
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto|%A%ListQueryDto
        using %P%.Application.Features.%F%.Services;
        %else%
        %if create%
        using %P%.Application.Features.%F%.UseCases.Commands.Create%A%;
        %end%
        %if update%
        using %P%.Application.Features.%F%.UseCases.Commands.Update%A%;
        %end%
        %if delete%
        using %P%.Application.Features.%F%.UseCases.Commands.Delete%A%;
        %end%
        %if get%
        using %P%.Application.Features.%F%.UseCases.Queries.Get%A%;
        %end%
        %if list%
        using %P%.Application.Features.%F%.UseCases.Queries.Get%F%;
        %end%
        using Mediator;
        %end%
        using Microsoft.AspNetCore.Mvc;

        namespace %P%.Api.Features.%F%.Controllers;

        [Route("api/v{version:apiVersion}/%R%")]
        [Asp.Versioning.ApiVersion("1.0")]
        [ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
        public sealed class %F%Controller(
            %CtorParams%) : ApiControllerBase
        {
        %if list%
            /// <summary>Lists %Scope%%F%, sorted and paginated%ListTail%.</summary>
            [HttpGet]
            [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResponse<%A%SummaryResponse>))]
            [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
            public async Task<ActionResult<PagedResponse<%A%SummaryResponse>>> GetAll(
                [FromQuery] Get%F%Request request,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(request);

                var query = new %ListInput%(request.Paging, request.Page, request.PageSize, request.Cursor, request.Sort);

                return OkOrProblem(%A%ResponseMapping.ToPageResponse(await %CallList%));
            }

        %end%
        %if get%
        %if etag%
            /// <summary>Gets one %L%, and the <c>ETag</c> needed to change it.</summary>
        %else%
            /// <summary>Gets one %L%.</summary>
        %end%
            [HttpGet("{%L%Id:guid}", Name = nameof(Get%A%))]
            [HttpHead("{%L%Id:guid}")]
            [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(%A%Response))]
        %if etag%
            [ProducesResponseType(StatusCodes.Status304NotModified)]
        %end%
            [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
            public async Task<ActionResult<%A%Response>> Get%A%(Guid %L%Id, CancellationToken cancellationToken) =>
                OkOrProblem(%A%ResponseMapping.To%A%Response(await %CallGet%));

        %end%
        %if create%
            /// <summary>Creates %a% %L%; an <c>Idempotency-Key</c> header makes a retried request safe.</summary>
            [HttpPost]
            [Idempotent]
            [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(%A%Response))]
            [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
            [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
            public async Task<ActionResult<%A%Response>> Create(
                [FromBody] Create%A%Request request,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(request);

                var outcome = await %CallCreate%;
                var result = %A%ResponseMapping.To%A%Response(outcome);

        %if etag%
                return CreatedOrProblem(result, nameof(Get%A%), created => new { %L%Id = created.Id });
        %else%
                if (result.IsFailure)
                {
                    return result.Error!.ToActionResult(HttpContext);
                }

                return CreatedOrProblem(result, nameof(Get%A%), new { %L%Id = result.Value.Id });
        %end%
            }

        %end%
        %if update%
        %if etag%
            /// <summary>Replaces %a% %L%'s %Replaced%, on the version an <c>If-Match</c> names.</summary>
        %else%
            /// <summary>Replaces %a% %L%'s %Replaced%; of two concurrent writers the later one wins.</summary>
        %end%
            [HttpPut("{%L%Id:guid}")]
            [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(%A%Response))]
            [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
            [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
        %if etag%
            [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
            [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
            [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
        %end%
            public async Task<ActionResult<%A%Response>> Update(
                Guid %L%Id,
                [FromBody] Update%A%Request request,
                CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(request);

        %if etag%
                if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
                {
                    return refusal;
                }

                var outcome = await %CallUpdate%;
                var result = RequiringExistence(requiresExistence, outcome);

                return UpdatedOrProblem(%A%ResponseMapping.To%A%Response(result));
        %else%
                var outcome = await %CallUpdate%;

                return OkOrProblem(%A%ResponseMapping.To%A%Response(outcome));
        %end%
            }

        %end%
        %if delete%
        %if update&etag%
            /// <summary>Deletes %a% %L%, on the same <c>If-Match</c> terms as the update.</summary>
        %else%
            /// <summary>Deletes %a% %L%.</summary>
        %end%
            [HttpDelete("{%L%Id:guid}")]
            [ProducesResponseType(StatusCodes.Status204NoContent)]
        %if etag%
            [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
        %end%
            [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
        %if etag%
            [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
            [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
            [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
        %end%
            public async Task<ActionResult> Delete(Guid %L%Id, CancellationToken cancellationToken)
            {
        %if etag%
                if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
                {
                    return refusal;
                }

                var outcome = await %CallDelete%;

                return NoContentOrProblem(RequiringExistence(requiresExistence, outcome));
        %else%
                return NoContentOrProblem(await %CallDelete%);
        %end%
            }
        %end%
        }
        """;

    internal const string ContentValidatorTests = """
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.Model.Dtos;

        public sealed class %A%ContentDtoValidatorTests
        {
            private static readonly %A%ContentDtoValidator _validator = new();

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

            [Fact]
            public async Task ANameAndADescription_AreAccepted() =>
                (await _validator.ValidateAsync(new %A%ContentDto("Groceries", "Milk"), TestToken)).IsValid.ShouldBeTrue();

            [Fact]
            public async Task AnEmptyDescription_IsAccepted() =>
                (await _validator.ValidateAsync(new %A%ContentDto("Groceries", string.Empty), TestToken)).IsValid.ShouldBeTrue();

            [Theory]
            [InlineData("")]
            [InlineData("   ")]
            public async Task ABlankName_IsRejected(string name) =>
                (await _validator.ValidateAsync(new %A%ContentDto(name, "Milk"), TestToken)).IsValid.ShouldBeFalse();

            [Fact]
            public async Task ANullName_IsRejected_NotDereferenced() =>
                (await _validator.ValidateAsync(new %A%ContentDto(null!, "Milk"), TestToken)).IsValid.ShouldBeFalse();

            [Fact]
            public async Task ANullDescription_IsRejected() =>
                (await _validator.ValidateAsync(new %A%ContentDto("Groceries", null!), TestToken)).IsValid.ShouldBeFalse();

            [Fact]
            public async Task ANameMeasuredAfterTrimming_MayFillTheLimitExactly()
            {
                string name = new('a', %Limits%.NameMaxLength);

                (await _validator.ValidateAsync(new %A%ContentDto($"  {name}  ", "Milk"), TestToken)).IsValid.ShouldBeTrue();
                (await _validator.ValidateAsync(new %A%ContentDto(name + "a", "Milk"), TestToken)).IsValid.ShouldBeFalse();
            }
        }
        """;

    internal const string CommandValidatorTests = """
        using %P%.Application.Features.%F%.UseCases.Commands.%Op%%A%;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.UseCases.Commands.%Op%%A%;

        public sealed class %Op%%A%CommandValidatorTests
        {
            private static readonly %Op%%A%CommandValidator _validator = new();

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

            private static %Op%%A%Command ACommand(string? name, string? description) => %CommandNew%;

            [Fact]
            public async Task ANameAndADescription_AreAccepted() =>
                (await _validator.ValidateAsync(ACommand("Groceries", "Milk"), TestToken)).IsValid.ShouldBeTrue();

            [Fact]
            public async Task AnEmptyDescription_IsAccepted() =>
                (await _validator.ValidateAsync(ACommand("Groceries", string.Empty), TestToken)).IsValid.ShouldBeTrue();

            [Theory]
            [InlineData("")]
            [InlineData("   ")]
            [InlineData(null)]
            public async Task ABlankOrMissingName_IsRejected(string? name) =>
                (await _validator.ValidateAsync(ACommand(name, "Milk"), TestToken)).IsValid.ShouldBeFalse();

            [Fact]
            public async Task AMissingDescription_IsRejected() =>
                (await _validator.ValidateAsync(ACommand("Groceries", null), TestToken)).IsValid.ShouldBeFalse();
        }
        """;

    internal const string ServiceTests = """
        using %P%.Application.Core.Common.Collections; //?FeaturePageRequest
        using %P%.Application.Core.Common.Concurrency; //?Versioned|VersionPrecondition
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDto|%A%Dto|%A%ListQueryDto
        using %P%.Application.Features.%F%.Ports.%A%Queries; //?I%A%Queries|%A%Filter
        using %P%.Application.Features.%F%.Ports.%A%Store; //?I%A%Store|%A%ChangeStatus
        using %P%.Application.Features.%F%.Services;
        using %P%.Application.Features.%F%.Model.Dtos; //?%A%ContentDtoValidator
        using %P%.Application.Features.%F%.Model.Policies; //?%A%InputPolicy
        using %P%.Application.UnitTests.TestDoubles;
        using %P%.Domain.Core.Common.Primitives;
        using NSubstitute;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.Services;

        public sealed class %A%ServiceTests
        {
            private static readonly UserId _callerId = UserId.Create(Guid.CreateVersion7());

        %if rq%
            private readonly I%A%Queries _queries = Substitute.For<I%A%Queries>();
        %end%
        %if wq%
            private readonly I%A%Store _store = Substitute.For<I%A%Store>();
            private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
        %end%

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

        %if content&fields%
            private static %A%ContentDto AValidContent => %FValidContent%;

        %end%
        %if content&!fields%
            private static %A%ContentDto AValidContent => new("Groceries", "Milk, eggs");

        %end%
            [Fact]
            public async Task AnAnonymousCaller_IsRefusedByEveryOperation()
            {
                var service = ServiceFor(StubCurrentUser.Anonymous);
        %if get|update|delete%
                var %L%Id = Guid.CreateVersion7();
        %end%

                Result[] results =
                [
        %if list%
                    await service.ListAsync(%ListInput%.Offset(null, null), TestToken),
        %end%
        %if get%
                    await service.GetAsync(%L%Id, TestToken),
        %end%
        %if create%
                    await service.CreateAsync(AValidContent, TestToken),
        %end%
        %if update%
                    await service.UpdateAsync(%L%Id, AValidContent%NullPrecondition%, TestToken),
        %end%
        %if delete%
                    await service.DeleteAsync(%L%Id%NullPrecondition%, TestToken),
        %end%
                ];

                results.ShouldAllBe(result => result.IsFailure);
                results.ShouldAllBe(result => result.Error!.Type == ErrorType.Unauthorized && result.Error.Code == "auth.required");
            }

            [Fact]
            public async Task AnAnonymousCaller_ReadsAndWritesNothing()
            {
                var service = ServiceFor(StubCurrentUser.Anonymous);
        %if get|update|delete%
                var %L%Id = Guid.CreateVersion7();
        %end%

        %if list%
                await service.ListAsync(%ListInput%.Offset(null, null), TestToken);
        %end%
        %if get%
                await service.GetAsync(%L%Id, TestToken);
        %end%
        %if create%
                await service.CreateAsync(AValidContent, TestToken);
        %end%
        %if update%
                await service.UpdateAsync(%L%Id, AValidContent%NullPrecondition%, TestToken);
        %end%
        %if delete%
                await service.DeleteAsync(%L%Id%NullPrecondition%, TestToken);
        %end%

        %if rq%
                _queries.ReceivedCalls().ShouldBeEmpty();
        %end%
        %if wq%
                _store.ReceivedCalls().ShouldBeEmpty();
                await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        %end%
            }

        %if get%
            [Fact]
            public async Task GetAsync_%An%%A%ThatIsNotThere_IsNotFound()
            {
                _queries.GetAsync(%AnyOwner%Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns((%ReadDto%?)null);

                var result = await Service().GetAsync(Guid.CreateVersion7(), TestToken);

                result.Error!.Type.ShouldBe(ErrorType.NotFound);
                result.Error.Code.ShouldBe("%L%.notFound");
            }

        %end%
        %if create&fields%
        %FServiceInvalid%

        %end%
        %if create&!fields%
            [Theory]
            [InlineData("", "description")]
            [InlineData("   ", "description")]
            [InlineData(null, "description")]
            [InlineData("name", null)]
            public async Task CreateAsync_RejectsInvalidContent_AndStagesNothing(string? name, string? description)
            {
                var result = await Service().CreateAsync(new %A%ContentDto(name!, description!), TestToken);

                result.Error!.Type.ShouldBe(ErrorType.Validation);
                _store.ReceivedCalls().ShouldBeEmpty();
                await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            }

        %end%
        %if delete%
            [Fact]
            public async Task DeleteAsync_%An%%A%ThatCannotBeStaged_FailsAndCommitsNothing()
            {
                _store.StageRemovalAsync(%AnyOwner%Arg.Any<Guid>(), %AnyPrecondition%Arg.Any<CancellationToken>())
                    .Returns(%A%ChangeStatus.NotFound);

                var result = await Service().DeleteAsync(Guid.CreateVersion7()%NullPrecondition%, TestToken);

                result.Error!.Code.ShouldBe("%L%.notFound");
                await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
            }

        %end%
            private %A%Service ServiceFor(ICurrentUser currentUser) =>
                new(%ServiceArgs%);
        %if get|create|delete%

            private %A%Service Service() => ServiceFor(StubCurrentUser.WithId(_callerId));
        %end%
        }
        """;

    internal const string AggregateTests = """
        using %P%.Domain.Core.Common.Exceptions;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using Shouldly;
        using Xunit;

        namespace %P%.Domain.UnitTests.Features.%F%.Model.Entities;

        public sealed class %A%Tests
        {
        %if owner%
            private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

        %end%
        %if create%
            [Fact]
            public void Create_KeepsTheTrimmedNameAndTheDescription()
            {
                var %L% = %A%.Create(%OwnerIdArg%"  Groceries  ", "Milk, eggs");

                %L%.Id.ShouldNotBe(Guid.Empty);
                %L%.Name.ShouldBe("Groceries");
                %L%.Description.ShouldBe("Milk, eggs");
        %if owner%
                %L%.OwnerId.ShouldBe(_ownerId);
        %end%
            }

            [Theory]
            [InlineData("")]
            [InlineData("   ")]
            public void Create_RefusesABlankName(string name) =>
                Should.Throw<DomainException>(() => %A%.Create(%OwnerIdArg%name, "Milk, eggs"));

            [Fact]
            public void Create_RefusesANameOverTheLimit() =>
                Should.Throw<DomainException>(
                    () => %A%.Create(%OwnerIdArg%new string('a', %A%.NameMaxLength + 1), string.Empty));

        %end%
        %if update%
            [Fact]
            public void Update_ReplacesTheNameAndTheDescription()
            {
                var %L% = %A%.Rehydrate(Guid.CreateVersion7(), %OwnerIdArg%"Groceries", "Milk");

                %L%.Update("  Errands  ", "Parcel");

                %L%.Name.ShouldBe("Errands");
                %L%.Description.ShouldBe("Parcel");
            }

            [Fact]
            public void Update_RefusesABlankName()
            {
                var %L% = %A%.Rehydrate(Guid.CreateVersion7(), %OwnerIdArg%"Groceries", "Milk");

                Should.Throw<DomainException>(() => %L%.Update("   ", "Parcel"));
                %L%.Name.ShouldBe("Groceries");
            }

            [Fact]
            public void Update_RefusesAMissingDescription_WithoutChangingTheName()
            {
                var %L% = %A%.Rehydrate(Guid.CreateVersion7(), %OwnerIdArg%"Groceries", "Milk");

                Should.Throw<DomainException>(() => %L%.Update("Errands", null!));
                %L%.Name.ShouldBe("Groceries");
                %L%.Description.ShouldBe("Milk");
            }

        %end%
            [Fact]
            public void Rehydrate_KeepsWhatWasStored_EvenOverTheCurrentLimit()
            {
                string stored = new('a', %A%.NameMaxLength + 1);

                var %L% = %A%.Rehydrate(Guid.CreateVersion7(), %OwnerIdArg%stored, "Milk");

                %L%.Name.ShouldBe(stored);
            }

            [Fact]
            public void Rehydrate_RefusesAnEmptyId() =>
                Should.Throw<DomainException>(() => %A%.Rehydrate(Guid.Empty, %OwnerIdArg%"Groceries", "Milk"));
        }
        """;

    internal const string ConfigurationTests = """
        using %P%.Application.Features.%F%.Model.Policies; //?%A%InputPolicy|%A%CollectionPolicy
        %if agg%
        using %P%.Domain.Features.%F%.Model.Entities; //?%A%
        %end%
        using %P%.Infrastructure.Core.Common.Contexts;
        using %P%.Infrastructure.Persistence.Common.Contexts;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;
        using Microsoft.EntityFrameworkCore;
        using Microsoft.EntityFrameworkCore.Metadata;
        using Shouldly;
        using Xunit;

        namespace %P%.Infrastructure.Persistence.UnitTests.Features.%F%.Rows.Configurations;

        public sealed class %A%RecordConfigurationTests
        {
        %if etag%
            [Fact]
            public void TheVersion_IsTheDatabasesOwnConcurrencyToken()
            {
                var version = Entity().FindProperty(nameof(%A%Record.Version))!;

                version.IsConcurrencyToken.ShouldBeTrue();
                version.GetColumnName().ShouldBe(DatabaseProvider.VersionColumnName);
            }
        %else%
            [Fact]
            public void NoPropertyIsAConcurrencyToken() =>
                Entity().GetProperties().ShouldAllBe(property => !property.IsConcurrencyToken);
        %end%

        %if fields%
        %FConfigurationTests%
        %else%
            [Theory]
            [InlineData(nameof(%A%Record.Name), %Limits%.NameMaxLength)]
            [InlineData(nameof(%A%Record.Description), %Limits%.DescriptionMaxLength)]
            public void EveryTextColumn_IsAsLongAsTheValidatorAllows(string property, int expected) =>
                Entity().FindProperty(property)!.GetMaxLength().ShouldBe(expected);
        %end%

        %if list%
            [Fact]
            public void EverySortIndex_ServesASortableField_AndEndsWithTheId()
            {
                var indexes = Entity().GetIndexes().ToList();

                indexes.Count.ShouldBe(%A%CollectionPolicy.Instance.SortableFields.Count);

                foreach (var index in indexes)
                {
                    var names = index.Properties.Select(property => property.Name).ToList();

        %if owner%
                    names[0].ShouldBe(nameof(%A%Record.OwnerId));
        %end%
                    names[^1].ShouldBe(nameof(%A%Record.Id));
                }
            }

        %end%
            [Fact]
        %if prefixed%
            public void TheTable_IsPrefixedWithThe%F%Schema()
            {
                Entity().GetTableName().ShouldBe($"{AppDbContext.%F%Schema}_%F%");
                Entity().GetSchema().ShouldBeNull();
            }
        %else%
            public void TheTable_LivesInItsOwnSchema()
            {
                Entity().GetTableName().ShouldBe("%F%");
                Entity().GetSchema().ShouldBe(AppDbContext.%F%Schema);
            }
        %end%

            private static IEntityType Entity()
            {
                using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseDatabase(DatabaseProvider.DesignTimeConnectionString)
                    .Options);

                return context.Model.FindEntityType(typeof(%A%Record))!;
            }
        }
        """;

    internal const string MapperFidelityTests = """
        using %P%.Domain.Core.Common.Abstractions;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;
        using Shouldly;
        using Xunit;

        namespace %P%.Infrastructure.Persistence.UnitTests.Features.%F%;

        public sealed class %A%MapperFidelityTests
        {
            [Fact]
            public void EveryPieceOfState_SurvivesTheRoundTrip()
            {
                var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
                var modifiedAt = new DateTimeOffset(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);
                var createdBy = new Guid("11111111-2222-3333-4444-555555555555");
                var modifiedBy = new Guid("66666666-7777-8888-9999-aaaaaaaaaaaa");
        %if owner%
                var ownerId = UserId.Create(new Guid("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a60"));
        %end%

                var original = %A%.Rehydrate(
                    new Guid("0199a3c4-1111-7000-8000-000000000001"),
        %if owner%
                    ownerId,
        %end%
                    "Groceries",
                    "Milk, eggs");

                ((IVersioned)original).SetVersion(987_654u);
                ((IAuditable)original).SetCreated(createdAt, createdBy);
                ((IAuditable)original).SetLastModified(modifiedAt, modifiedBy);

                var mapper = new %A%Mapper();

                var roundTripped = mapper.ToAggregate(mapper.ToNewRecord(original));

                roundTripped.Id.ShouldBe(original.Id);
        %if owner%
                roundTripped.OwnerId.ShouldBe(ownerId);
        %end%
                roundTripped.Name.ShouldBe("Groceries");
                roundTripped.Description.ShouldBe("Milk, eggs");
                roundTripped.Version.ShouldBe(987_654u);
                roundTripped.CreatedAt.ShouldBe(createdAt);
                roundTripped.CreatedBy.ShouldBe(createdBy);
                roundTripped.LastModifiedAt.ShouldBe(modifiedAt);
                roundTripped.LastModifiedBy.ShouldBe(modifiedBy);
            }
        }
        """;


    internal const string FieldContentValidatorTests = """
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies; //?%A%InputPolicy
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.Model.Dtos;

        public sealed class %A%ContentDtoValidatorTests
        {
            private static readonly %A%ContentDtoValidator _validator = new();

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

        %FContentValidatorTests%

            private static %A%ContentDto AValidContentDto() => %FValidContent%;
        }
        """;

    internal const string FieldCommandValidatorTests = """
        using %P%.Application.Features.%F%.UseCases.Commands.%Op%%A%;
        using %P%.Domain.Features.%F%.Model.Entities; //?%A%
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.UseCases.Commands.%Op%%A%;

        public sealed class %Op%%A%CommandValidatorTests
        {
            private static readonly %Op%%A%CommandValidator _validator = new();

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

        %FValidatorTests%

            private static %Op%%A%Command AValidCommand() => %FValidCommand%;
        }
        """;

    internal const string FieldAggregateTests = """
        using %P%.Domain.Core.Common.Exceptions;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using Shouldly;
        using Xunit;

        namespace %P%.Domain.UnitTests.Features.%F%.Model.Entities;

        public sealed class %A%Tests
        {
        %if owner%
            private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

        %end%
        %FAggregateTests%
        }
        """;

    internal const string FieldMapperFidelityTests = """
        using %P%.Domain.Core.Common.Abstractions;
        using %P%.Domain.Core.Common.Primitives; //?UserId
        using %P%.Domain.Features.%F%.Model.Entities;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Mapping;
        using Shouldly;
        using Xunit;

        namespace %P%.Infrastructure.Persistence.UnitTests.Features.%F%;

        public sealed class %A%MapperFidelityTests
        {
            [Fact]
            public void EveryPieceOfState_SurvivesTheRoundTrip()
            {
                var createdAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
                var modifiedAt = new DateTimeOffset(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);
                var createdBy = new Guid("11111111-2222-3333-4444-555555555555");
                var modifiedBy = new Guid("66666666-7777-8888-9999-aaaaaaaaaaaa");
        %if owner%
                var ownerId = UserId.Create(new Guid("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a60"));
        %end%

                var original = %A%.Rehydrate(
                    new Guid("0199a3c4-1111-7000-8000-000000000001"),
        %if owner%
                    ownerId,
        %end%
        %FFidelityArgs%

                ((IVersioned)original).SetVersion(987_654u);
                ((IAuditable)original).SetCreated(createdAt, createdBy);
                ((IAuditable)original).SetLastModified(modifiedAt, modifiedBy);

                var mapper = new %A%Mapper();

                var roundTripped = mapper.ToAggregate(mapper.ToNewRecord(original));

                roundTripped.Id.ShouldBe(original.Id);
        %if owner%
                roundTripped.OwnerId.ShouldBe(ownerId);
        %end%
        %FFidelityAsserts%
                roundTripped.Version.ShouldBe(987_654u);
                roundTripped.CreatedAt.ShouldBe(createdAt);
                roundTripped.CreatedBy.ShouldBe(createdBy);
                roundTripped.LastModifiedAt.ShouldBe(modifiedAt);
                roundTripped.LastModifiedBy.ShouldBe(modifiedBy);
            }
        }
        """;

    internal const string FieldApiTests = """
        using System.Net;
        using System.Net.Http.Json;
        using %P%.Api.Features.%F%.Contracts.Requests;
        using %P%.Api.Features.%F%.Contracts.Responses;
        using %P%.Api.IntegrationTests.Infrastructure;
        using Shouldly;
        using Xunit;

        namespace %P%.Api.IntegrationTests.Features.%F%;

        public sealed class %A%FieldTests(ApiFixture fixture) : IntegrationTestBase(fixture)
        {
            private const string _route = "/api/v1/%R%";

            [Fact]
            public async Task %An%%A%_IsCreatedWithEveryField_AndAnswersThemBack()
            {
                var (client, _, _) = await SignInAsync();

                using var response = await client.PostAsJsonAsync(_route, AValidRequest(), TestToken);

                response.StatusCode.ShouldBe(HttpStatusCode.Created);

                var created = await ApiJson.ReadAsync<%A%Response>(response, TestToken);

        %FResponseAsserts%
            }

        %FApiInstants%

        %FApiRefusal%

            private static Create%A%Request AValidRequest() => %FRequestSample%;
        }
        """;

    internal const string FieldSortMap = """
        using System.Globalization;
        using %P%.Application.Core.Common.Collections;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies;
        using %P%.Infrastructure.Persistence.Features.%F%.Rows.Models;

        namespace %P%.Infrastructure.Persistence.Features.%F%.Access.Queries;

        internal static class %A%SortMap
        {
            public static IOrderedQueryable<%A%Record> ApplyOrder(IQueryable<%A%Record> source, SortOrder sort)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(sort);

                IOrderedQueryable<%A%Record>? ordered = null;

                foreach (var term in sort.Terms)
                {
                    ordered = ordered is null ? OrderByTerm(source, term) : ThenByTerm(ordered, term);
                }

                return sort.Terms[^1].Direction == SortDirection.Ascending
                    ? ordered!.ThenBy(record => record.Id)
                    : ordered!.ThenByDescending(record => record.Id);
            }

            public static IQueryable<%A%Record> ApplyKeyset(IQueryable<%A%Record> source, SortTerm term, Cursor cursor)
            {
                ArgumentNullException.ThrowIfNull(source);
                ArgumentNullException.ThrowIfNull(term);
                ArgumentNullException.ThrowIfNull(cursor);

                return term.Field switch
                {
        %FKeysetCases%
                    %A%CollectionPolicy.CreatedAtField => ApplyCreatedAtKeyset(source, term.Direction, cursor),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' has no keyset comparison: the policy should have rejected it first."),
                };
            }

            public static string KeyOf(%A%SummaryDto summary, string field)
            {
                ArgumentNullException.ThrowIfNull(summary);

                return field switch
                {
        %FKeyCases%
                    %A%CollectionPolicy.CreatedAtField => summary.CreatedAt.ToString("O", CultureInfo.InvariantCulture),
                    _ => throw new InvalidOperationException(
                        $"'{field}' has no cursor key: the policy should have rejected it first."),
                };
            }

            private static IOrderedQueryable<%A%Record> OrderByTerm(IQueryable<%A%Record> source, SortTerm term) =>
                term.Field switch
                {
        %FOrderCases%
                    %A%CollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                        ? source.OrderBy(record => record.CreatedAt)
                        : source.OrderByDescending(record => record.CreatedAt),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' is not a sortable field: the policy should have rejected it first."),
                };

            private static IOrderedQueryable<%A%Record> ThenByTerm(IOrderedQueryable<%A%Record> source, SortTerm term) =>
                term.Field switch
                {
        %FThenCases%
                    %A%CollectionPolicy.CreatedAtField => term.Direction == SortDirection.Ascending
                        ? source.ThenBy(record => record.CreatedAt)
                        : source.ThenByDescending(record => record.CreatedAt),
                    _ => throw new InvalidOperationException(
                        $"'{term.Field}' is not a sortable field: the policy should have rejected it first."),
                };

        %FKeysetMethods%

            private static IQueryable<%A%Record> ApplyCreatedAtKeyset(
                IQueryable<%A%Record> source,
                SortDirection direction,
                Cursor cursor)
            {
                if (!DateTimeOffset.TryParse(
                    cursor.Key,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset key))
                {
                    throw new InvalidOperationException(
                        "The cursor's key is not a valid date/time: CursorKeys should have rejected it first.");
                }

                return direction == SortDirection.Ascending
                    ? source.Where(record =>
                        record.CreatedAt.CompareTo(key) > 0
                        || (record.CreatedAt == key && record.Id.CompareTo(cursor.Id) > 0))
                    : source.Where(record =>
                        record.CreatedAt.CompareTo(key) < 0
                        || (record.CreatedAt == key && record.Id.CompareTo(cursor.Id) < 0));
            }
        }
        """;
}

internal static class FieldCode
{
    internal const string AnonymisedText = "[deleted account]";

    internal const int IndexNameMaxLength = 63;

    internal static List<string> Conflicts(
        IReadOnlyList<Field> fields,
        string features,
        string type,
        IEnumerable<string> generatedTypes,
        string deletion,
        bool owner,
        bool list)
    {
        var problems = new List<string>();

        foreach (string index in list ? fields.Where(FieldCode.IsSortable).Select(field => IndexName(features, field, owner)) : [])
        {
            if (index.Length > IndexNameMaxLength)
            {
                problems.Add(
                    $"--fields: the sort index {index} would be {index.Length} characters long, and PostgreSQL cuts a name at "
                    + $"{IndexNameMaxLength}, where two indexes could then share one. Shorten the field or the feature name.");
            }
        }
        var types = new HashSet<string>(generatedTypes, StringComparer.Ordinal) { type, $"{type}Id" };

        foreach (var field in fields.Where(field => types.Contains(field.Name)))
        {
            problems.Add(
                $"--fields: '{field.Name}' is the name of a type or a member the feature {type} generates, and a property "
                + "cannot share it. Choose another name.");
        }

        if (deletion == "anonymise")
        {
            if (!fields.Any(field => FieldCode.Anonymised(field) is not null))
            {
                problems.Add(
                    "--deletion anonymise rewrites the fields that can say who the owner was, its strings, texts and nullable "
                    + "fields, and these fields have none: there is nothing to anonymise. Use --deletion delete, or add such a field.");
            }

            foreach (var field in fields.Where(field => field is { Kind: FieldKind.String, Nullable: false } && field.Length < AnonymisedText.Length))
            {
                problems.Add(
                    $"--fields: {field.Name} is a required string({field.Length}), and --deletion anonymise writes "
                    + $"\"{AnonymisedText}\", {AnonymisedText.Length} characters, in its place. Give it string({AnonymisedText.Length}) "
                    + "or more, or make it nullable, which anonymises to null.");
            }
        }

        if (fields.All(field => !FieldCode.HasRule(field)))
        {
            problems.Add(
                "--fields: no field has a rule to check (a bool, an unbounded int or long, a nullable DateOnly or Guid accept "
                + "every value), so the validator the feature's writes run would be empty. Bound a number, or add a string, a text, "
                + "a decimal or a required date or Guid.");
        }

        return problems;
    }

    internal static string IndexName(string features, Field field, bool owner) =>
        owner ? $"IX_{features}_OwnerId_{field.Name}_Id" : $"IX_{features}_{field.Name}_Id";

    internal static string Label(Field field) => NewFeature.Separated(field.Name, ' ');

    internal static string Parameter(Field field) => CSharpNames.Lower(field.Name);

    internal static string Type(Field field) => field.ClrType + (field.Nullable ? "?" : string.Empty);

    internal static bool IsReference(Field field) => field.Kind is FieldKind.String or FieldKind.Text;

    internal static bool HasRule(Field field) => field.Kind switch
    {
        FieldKind.String or FieldKind.Text or FieldKind.Decimal => true,
        FieldKind.Int or FieldKind.Long => field.Bounds.Any(),
        FieldKind.DateOnly or FieldKind.DateTimeOffset or FieldKind.Guid => !field.Nullable,
        _ => false,
    };

    internal static bool HasRequire(Field field) => HasRule(field) || field.Kind == FieldKind.DateTimeOffset;

    internal static bool IsSortable(Field field) =>
        !field.Nullable && field.Kind is FieldKind.String or FieldKind.Int or FieldKind.Long or FieldKind.DateOnly or FieldKind.DateTimeOffset;

    internal static bool IsKeyset(Field field) => IsSortable(field) && field.Kind is FieldKind.String or FieldKind.DateTimeOffset;

    internal static bool InSummary(Field field) => field.Kind != FieldKind.Text;

    internal static bool IsRequiredOnTheWire(Field field) => !field.Nullable && !IsReference(field);

    internal static string? Anonymised(Field field) =>
        field.Nullable ? "null"
        : field.Kind == FieldKind.String ? "AnonymisedText"
        : field.Kind == FieldKind.Text ? "string.Empty"
        : null;

    internal static bool HasConstants(Field field) => IsReference(field) || field.Kind == FieldKind.Decimal || field.Bounds.Any();

    internal static string Grouped(int value) =>
        value >= 1_000
            ? value.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '_')
            : value.ToString(CultureInfo.InvariantCulture);

    internal static string Literal(Field field, decimal value) =>
        value.ToString(CultureInfo.InvariantCulture) + (field.Kind == FieldKind.Decimal ? "m" : string.Empty);

    internal static string Joined(IEnumerable<string> lines, string separator = "\n") => string.Join(separator, lines);

    internal static string List(IEnumerable<string> items, string indent, string last = "") =>
        string.Join(",\n", items.Select(item => indent + item)) + last;

    internal static string Constants(IEnumerable<Field> fields, bool ceilings) =>
        Joined(
            [
                .. fields.SelectMany(ConstantsOf),
                .. ceilings
                    ? fields.Where(field => field.Kind == FieldKind.Decimal)
                        .Select(field => $"    private const decimal {Ceiling(field)} = 1e{field.Precision - field.Scale}m;")
                    : [],
            ],
            "\n\n");

    private static IEnumerable<string> ConstantsOf(Field field)
    {
        if (IsReference(field))
        {
            yield return $"    public const int {field.Name}MaxLength = {Grouped(field.Length)};";
        }

        if (field.Kind == FieldKind.Decimal)
        {
            yield return $"    public const int {field.Name}Precision = {field.Precision};";
            yield return $"    public const int {field.Name}Scale = {field.Scale};";
        }

        foreach (var bound in field.Bounds)
        {
            yield return $"    public const {field.ClrType} {field.Name}{bound.Suffix} = {Literal(field, bound.Value)};";
        }
    }

    private static string Ceiling(Field field) => $"_{Parameter(field)}Ceiling";

    private static string Subject(Field field) => $"%An% %L% {Label(field)}";

    internal static string Required(Field field) => $"{Subject(field)} is required.";

    private static string RequiredMayBeEmpty(Field field) => $"{Subject(field)} is required; it may be empty.";

    private static string Blank(Field field) => $"{Subject(field)} cannot be blank; leave it out instead.";

    private static string TooLong(Field field, string holder) =>
        $"{Subject(field)} cannot exceed {{{holder}{field.Name}MaxLength}} characters.";

    private static string OutOfBound(Field field, FieldBound bound) =>
        $"{Subject(field)} must be {bound.Wording} {bound.Value.ToString(CultureInfo.InvariantCulture)}.";

    private static string Digits(Field field)
    {
        int before = field.Precision - field.Scale;

        return field.Scale == 0 ? $"{Subject(field)} must be a whole number of at most {Count(before, "digit")}."
            : before == 0 ? $"{Subject(field)} must lie between -1 and 1, with at most {Count(field.Scale, "decimal place")}."
            : $"{Subject(field)} must have at most {Count(before, "digit")} before the decimal point and {field.Scale} after it.";
    }

    private static string Count(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    internal static string Require(Field field)
    {
        string name = $"Require{field.Name}";
        string type = Type(field);

        switch (field.Kind)
        {
            case FieldKind.String when !field.Nullable:
                return $$"""
                        private static {{type}} {{name}}({{type}} value)
                        {
                            string trimmed = value?.Trim() ?? string.Empty;

                            if (trimmed.Length == 0)
                            {
                                throw new DomainException("{{Required(field)}}");
                            }

                            return trimmed.Length <= {{field.Name}}MaxLength
                                ? trimmed
                                : throw new DomainException($"{{TooLong(field, string.Empty)}}");
                        }
                    """;

            case FieldKind.String:
                return $$"""
                        private static {{type}} {{name}}({{type}} value)
                        {
                            if (value is null)
                            {
                                return null;
                            }

                            string trimmed = value.Trim();

                            if (trimmed.Length == 0)
                            {
                                throw new DomainException("{{Blank(field)}}");
                            }

                            return trimmed.Length <= {{field.Name}}MaxLength
                                ? trimmed
                                : throw new DomainException($"{{TooLong(field, string.Empty)}}");
                        }
                    """;

            case FieldKind.Text when !field.Nullable:
                return $$"""
                        private static {{type}} {{name}}({{type}} value)
                        {
                            if (value is null)
                            {
                                throw new DomainException("{{RequiredMayBeEmpty(field)}}");
                            }

                            return value.Length <= {{field.Name}}MaxLength
                                ? value
                                : throw new DomainException($"{{TooLong(field, string.Empty)}}");
                        }
                    """;

            case FieldKind.Text:
                return $$"""
                        private static {{type}} {{name}}({{type}} value) =>
                            value is null || value.Length <= {{field.Name}}MaxLength
                                ? value
                                : throw new DomainException($"{{TooLong(field, string.Empty)}}");
                    """;

            case FieldKind.DateTimeOffset when field.Nullable:
                return $"""
                        private static {type} {name}({type} value) => value?.ToUniversalTime();
                    """;
        }

        string present = field.Nullable ? "present" : "value";
        var lines = new List<string> { $"    private static {type} {name}({type} value)", "    {" };

        if (field.Nullable)
        {
            lines.AddRange(["        if (value is not { } present)", "        {", "            return null;", "        }", string.Empty]);
        }

        void Check(string condition, string message) =>
            lines.AddRange([$"        if ({condition})", "        {", $"            throw new DomainException(\"{message}\");", "        }", string.Empty]);

        if (field.Kind == FieldKind.Decimal)
        {
            Check($"decimal.Round({present}, {field.Name}Scale) != {present} || Math.Abs({present}) >= {Ceiling(field)}", Digits(field));
        }

        foreach (var bound in field.Bounds)
        {
            string comparison = bound.Operator switch
            {
                ">=" => "<",
                ">" => "<=",
                "<=" => ">",
                _ => ">=",
            };

            Check($"{present} {comparison} {field.Name}{bound.Suffix}", OutOfBound(field, bound));
        }

        if (field.Kind is FieldKind.DateOnly or FieldKind.DateTimeOffset or FieldKind.Guid && !field.Nullable)
        {
            Check($"{present} == default", Required(field));
        }

        lines.Add(field.Kind == FieldKind.DateTimeOffset ? $"        return {present}.ToUniversalTime();" : $"        return {present};");
        lines.Add("    }");

        return Joined(lines);
    }

    internal static string Rules(IEnumerable<Field> fields, string subject, string holder) =>
        Joined(fields.Where(HasRule).Select(field => RulesOf(field, subject, holder)), "\n\n");

    private static string RulesOf(Field field, string subject, string holder)
    {
        string limits = $"{holder}.";
        var chain = new List<string>();

        switch (field.Kind)
        {
            case FieldKind.String when !field.Nullable:
                chain.Add($".NotEmpty().WithMessage(\"{Required(field)}\")");
                chain.Add($".Must(value => value.Trim().Length <= {limits}{field.Name}MaxLength)");
                chain.Add($".WithMessage($\"{TooLong(field, limits)}\")");
                break;

            case FieldKind.String:
                chain.Add($".Must(value => value!.Trim().Length > 0).WithMessage(\"{Blank(field)}\")");
                chain.Add($".Must(value => value!.Trim().Length <= {limits}{field.Name}MaxLength)");
                chain.Add($".WithMessage($\"{TooLong(field, limits)}\")");
                chain.Add($".When({subject} => {subject}.{field.Name} is not null)");
                break;

            case FieldKind.Text:
                if (!field.Nullable)
                {
                    chain.Add($".NotNull().WithMessage(\"{RequiredMayBeEmpty(field)}\")");
                }

                chain.Add($".MaximumLength({limits}{field.Name}MaxLength)");
                chain.Add($".WithMessage($\"{TooLong(field, limits)}\")");
                break;

            case FieldKind.DateOnly or FieldKind.DateTimeOffset or FieldKind.Guid:
                chain.Add($".NotEmpty().WithMessage(\"{Required(field)}\")");
                break;
        }

        if (field.Kind == FieldKind.Decimal)
        {
            chain.Add($".PrecisionScale({limits}{field.Name}Precision, {limits}{field.Name}Scale, ignoreTrailingZeros: true)");
            chain.Add($".WithMessage(\"{Digits(field)}\")");
        }

        foreach (var bound in field.Bounds)
        {
            string rule = bound.Operator switch
            {
                ">=" => "GreaterThanOrEqualTo",
                ">" => "GreaterThan",
                "<=" => "LessThanOrEqualTo",
                _ => "LessThan",
            };

            chain.Add($".{rule}({limits}{field.Name}{bound.Suffix}).WithMessage(\"{OutOfBound(field, bound)}\")");
        }

        int rules = chain.Count(link => !link.StartsWith(".WithMessage", StringComparison.Ordinal) && !link.StartsWith(".When", StringComparison.Ordinal));

        if (rules > 1)
        {
            chain.Insert(0, ".Cascade(CascadeMode.Stop)");
        }

        return Joined([$"        RuleFor({subject} => {subject}.{field.Name})", .. chain.Select(link => "            " + link)]) + ";";
    }

    internal static string WireDocumentation(Field field)
    {
        string presence = field.Nullable ? "Optional" : "Required";
        string text = field.Kind switch
        {
            FieldKind.String when field.Nullable => $"Optional; when sent, not blank and at most {field.Length} characters once trimmed",
            FieldKind.String => $"Required, at most {field.Length} characters once trimmed",
            FieldKind.Text when field.Nullable => $"Optional, at most {field.Length} characters",
            FieldKind.Text => $"Required but may be empty, at most {field.Length} characters",
            FieldKind.DateOnly => $"{presence}, as yyyy-MM-dd",
            FieldKind.DateTimeOffset => $"{presence}, with its offset; kept as the instant, in UTC",
            FieldKind.Guid when !field.Nullable => "Required, and not the empty Guid",
            FieldKind.Decimal => $"{presence}, with at most {field.Precision - field.Scale} digits before the decimal point and {field.Scale} after it",
            _ => presence,
        };

        foreach (var bound in field.Bounds)
        {
            text += $", {bound.Wording} {bound.Value.ToString(CultureInfo.InvariantCulture)}";
        }

        return $"/// <param name=\"{field.Name}\">{text}.</param>";
    }

    internal static string WireParameter(Field field) =>
        (IsRequiredOnTheWire(field) ? "[property: JsonRequired] " : string.Empty) + $"{Type(field)} {field.Name}";

    internal static string Store(Field field, string source)
    {
        string read = $"{source}.{field.Name}";

        return field.Kind == FieldKind.DateTimeOffset
            ? read + (field.Nullable ? "?.ToUniversalTime()" : ".ToUniversalTime()")
            : read;
    }

    internal static string RecordProperty(Field field) =>
        field is { Nullable: false } && IsReference(field)
            ? $"    public string {field.Name} {{ get; set; }} = string.Empty;"
            : $"    public {Type(field)} {field.Name} {{ get; set; }}";

    internal static string Configuration(Field field, string holder)
    {
        string property = $"        builder.Property(record => record.{field.Name})";
        var links = new List<string>();

        if (IsReference(field))
        {
            links.Add($".HasMaxLength({holder}.{field.Name}MaxLength)");
        }

        if (field.Kind == FieldKind.Decimal)
        {
            links.Add($".HasPrecision({holder}.{field.Name}Precision, {holder}.{field.Name}Scale)");
        }

        if (!field.Nullable)
        {
            links.Add(".IsRequired()");
        }

        return links.Count switch
        {
            0 => property + ";",
            1 when !IsReference(field) && field.Kind != FieldKind.Decimal => property + links[0] + ";",
            _ => Joined([property, .. links.Select(link => "            " + link)]) + ";",
        };
    }

    internal static string SortIndex(string features, Field field, bool owner) =>
        Joined(
        [
            owner
                ? $"        builder.HasIndex(record => new {{ record.OwnerId, record.{field.Name}, record.Id }})"
                : $"        builder.HasIndex(record => new {{ record.{field.Name}, record.Id }})",
            $"            .HasDatabaseName(\"{FieldCode.IndexName(features, field, owner)}\");",
        ]);

    internal static (decimal Low, decimal High) Range(Field field)
    {
        decimal step = field.Kind == FieldKind.Decimal ? FieldGrammar.Step(field.Scale) : 1m;
        decimal floor = field.Kind switch
        {
            FieldKind.Int => int.MinValue,
            FieldKind.Long => long.MinValue,
            _ => -FieldGrammar.Ceiling(field) + step,
        };
        decimal top = field.Kind switch
        {
            FieldKind.Int => int.MaxValue,
            FieldKind.Long => long.MaxValue,
            _ => FieldGrammar.Ceiling(field) - step,
        };

        return (
            field.Lower is { } lower ? Math.Max(floor, lower.IsInclusive ? lower.Value : lower.Value + step) : floor,
            field.Upper is { } upper ? Math.Min(top, upper.IsInclusive ? upper.Value : upper.Value - step) : top);
    }

    internal static string Sample(Field field, int which)
    {
        static string Fit(string text, int length) => length >= text.Length ? text : text[..length];

        switch (field.Kind)
        {
            case FieldKind.String:
                return $"\"{Fit(which == 1 ? "Alpha" : "Bravo", field.Length)}\"";
            case FieldKind.Text:
                return $"\"{Fit(which == 1 ? "Some text" : "Other text", field.Length)}\"";
            case FieldKind.Bool:
                return which == 1 ? "true" : "false";
            case FieldKind.DateOnly:
                return which == 1 ? "new DateOnly(2026, 3, 4)" : "new DateOnly(2026, 4, 5)";
            case FieldKind.DateTimeOffset:
                return which == 1
                    ? "new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero)"
                    : "new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero)";
            case FieldKind.Guid:
                return which == 1
                    ? "new Guid(\"0199a3c4-2222-7000-8000-000000000002\")"
                    : "new Guid(\"0199a3c4-3333-7000-8000-000000000003\")";
        }

        var (low, high) = Range(field);
        decimal[] candidates = field.Kind == FieldKind.Decimal && field.Scale > 0
            ? which == 1 ? [1.5m, 1m, 0m, low, high] : [2.25m, 2m, 1m, 0m, high, low]
            : which == 1 ? [1m, 0m, low, high] : [2m, 1m, 0m, high, low];

        decimal chosen = candidates.First(candidate =>
            candidate >= low && candidate <= high && decimal.Round(candidate, field.Kind == FieldKind.Decimal ? field.Scale : 0) == candidate);

        return Literal(field, chosen);
    }

    internal static List<(string Case, string Value)> Invalid(Field field, string? holder)
    {
        string Constant(string suffix, decimal value) => holder is null ? Literal(field, value) : $"{holder}.{field.Name}{suffix}";

        var cases = new List<(string, string)>();
        decimal step = field.Kind == FieldKind.Decimal ? FieldGrammar.Step(field.Scale) : 1m;
        string stepText = Literal(field, step);

        switch (field.Kind)
        {
            case FieldKind.String:
                cases.Add(("Blank", "\"   \""));
                break;
            case FieldKind.Text when !field.Nullable:
                cases.Add(("Missing", "null!"));
                break;
            case FieldKind.Decimal when field.Scale < FieldGrammar.DecimalMaxPrecision:
                cases.Add(("WithTooManyDecimals", Literal(field, FieldGrammar.Step(field.Scale + 1))));
                break;
            case FieldKind.DateOnly or FieldKind.DateTimeOffset or FieldKind.Guid when !field.Nullable:
                cases.Add(("Empty", "default"));
                break;
        }

        if (IsReference(field))
        {
            cases.Add(("OverTheLimit", $"new string('a', {Constant("MaxLength", field.Length)} + 1)"));
        }

        if (field.Kind == FieldKind.Decimal)
        {
            cases.Add(("TooLarge", $"1e{field.Precision - field.Scale}m"));
        }

        if (field is { Kind: FieldKind.String, Nullable: false })
        {
            cases.Add(("Missing", "null!"));
        }

        foreach (var bound in field.Bounds)
        {
            cases.Add(bound.Operator switch
            {
                ">=" => ("BelowTheMinimum", holder is null ? Literal(field, bound.Value - step) : $"{Constant(bound.Suffix, bound.Value)} - {stepText}"),
                ">" => ("AtTheExclusiveMinimum", Constant(bound.Suffix, bound.Value)),
                "<=" => ("AboveTheMaximum", holder is null ? Literal(field, bound.Value + step) : $"{Constant(bound.Suffix, bound.Value)} + {stepText}"),
                _ => ("AtTheExclusiveMaximum", Constant(bound.Suffix, bound.Value)),
            });
        }

        return cases;
    }

    internal static string Assertion(string owner, Field field, string expected) =>
        $"        {owner}.{field.Name}.ShouldBe({expected});";

    internal static string ValidatorTests(IReadOnlyList<Field> fields, string message, string builder, string holder)
    {
        var tests = new List<string>
        {
            $$"""
                [Fact]
                public async Task AValid{{message}}_IsAccepted() =>
                    (await _validator.ValidateAsync({{builder}}(), TestToken)).IsValid.ShouldBeTrue();
            """,
        };

        foreach (var field in fields)
        {
            if (field.Nullable)
            {
                tests.Add($$"""
                        [Fact]
                        public async Task {{field.Name}}Null_IsAccepted() =>
                            (await _validator.ValidateAsync({{builder}}() with { {{field.Name}} = null }, TestToken)).IsValid.ShouldBeTrue();
                    """);
            }

            if (field.Kind == FieldKind.String)
            {
                tests.Add($$"""
                        [Fact]
                        public async Task {{field.Name}}AtTheLimitOnceTrimmed_IsAccepted() =>
                            (await _validator.ValidateAsync(
                                {{builder}}() with { {{field.Name}} = $"  {new string('a', {{holder}}.{{field.Name}}MaxLength)}  " },
                                TestToken)).IsValid.ShouldBeTrue();
                    """);
            }

            foreach (var (name, value) in Invalid(field, holder))
            {
                tests.Add($$"""
                        [Fact]
                        public async Task {{field.Name}}{{name}}_IsRefused() =>
                            (await _validator.ValidateAsync({{builder}}() with { {{field.Name}} = {{value}} }, TestToken)).Errors
                                .ShouldContain(error => error.PropertyName == nameof({{message}}.{{field.Name}}));
                    """);
            }
        }

        return Joined(tests, "\n\n");
    }

    internal static string Construction(IEnumerable<string> arguments) => "new(\n" + List(arguments, "        ") + ")";

    internal static string Arguments(IReadOnlyList<Field> fields, Func<Field, string> value) => string.Join(", ", fields.Select(value));

    internal static string WithOne(IReadOnlyList<Field> fields, Field changed, string value) =>
        Arguments(fields, field => ReferenceEquals(field, changed) ? value : Sample(field, 1));

    internal static string AggregateTests(IReadOnlyList<Field> fields, bool create, bool update, string ownerArgument)
    {
        var tests = new List<string>();
        string ones = Arguments(fields, field => Sample(field, 1));
        string twos = Arguments(fields, field => Sample(field, 2));
        string Asserted(int which) => Joined(fields.Select(field => Assertion("%L%", field, Sample(field, which))));
        var refusable = fields.Where(field => Invalid(field, "%A%").Count > 0).ToList();

        if (create)
        {
            string padded = Arguments(fields, field => field.Kind == FieldKind.String ? $"\"  {Sample(field, 1)[1..^1]}  \"" : Sample(field, 1));

            tests.Add(Joined(
            [
                "    [Fact]",
                "    public void Create_KeepsEveryField_WithItsStringsTrimmed()",
                "    {",
                $"        var %L% = %A%.Create({ownerArgument}{padded});",
                string.Empty,
                "        %L%.Id.ShouldNotBe(Guid.Empty);",
                Asserted(1),
                "    }",
            ]));

            foreach (var field in fields.Where(field => field.Kind == FieldKind.DateTimeOffset))
            {
                tests.Add($$"""
                        [Fact]
                        public void Create_Keeps{{field.Name}}AsTheInstant_InUtc()
                        {
                            var sent = new DateTimeOffset(2026, 3, 4, 7, 6, 7, TimeSpan.FromHours(2));

                            var %L% = %A%.Create({{ownerArgument}}{{WithOne(fields, field, "sent")}});

                            %L%.{{field.Name}}.ShouldBe(sent);
                            %L%.{{field.Name}}{{(field.Nullable ? "!.Value" : string.Empty)}}.Offset.ShouldBe(TimeSpan.Zero);
                        }
                    """);
            }

            foreach (var field in refusable)
            {
                foreach (var (name, value) in Invalid(field, "%A%"))
                {
                    tests.Add($$"""
                            [Fact]
                            public void Create_Refuses{{field.Name}}{{name}}() =>
                                Should.Throw<DomainException>(() => %A%.Create({{ownerArgument}}{{WithOne(fields, field, value)}}));
                        """);
                }
            }
        }

        if (update)
        {
            tests.Add(Joined(
            [
                "    [Fact]",
                "    public void Update_ReplacesEveryField()",
                "    {",
                $"        var %L% = %A%.Rehydrate(Guid.CreateVersion7(), {ownerArgument}{ones});",
                string.Empty,
                $"        %L%.Update({twos});",
                string.Empty,
                Asserted(2),
                "    }",
            ]));

            if (refusable.FirstOrDefault() is { } first)
            {
                var (name, value) = Invalid(first, "%A%")[0];

                tests.Add(Joined(
                [
                    "    [Fact]",
                    $"    public void Update_Refuses{first.Name}{name}_AndChangesNothing()",
                    "    {",
                    $"        var %L% = %A%.Rehydrate(Guid.CreateVersion7(), {ownerArgument}{ones});",
                    string.Empty,
                    $"        Should.Throw<DomainException>(() => %L%.Update({Arguments(fields, field => ReferenceEquals(field, first) ? value : Sample(field, 2))}));",
                    string.Empty,
                    Asserted(1),
                    "    }",
                ]));
            }
        }

        if (fields.FirstOrDefault(IsReference) is { } text)
        {
            tests.Add(Joined(
            [
                "    [Fact]",
                "    public void Rehydrate_KeepsWhatWasStored_EvenOverTheCurrentLimit()",
                "    {",
                $"        string stored = new('a', %A%.{text.Name}MaxLength + 1);",
                string.Empty,
                $"        var %L% = %A%.Rehydrate(Guid.CreateVersion7(), {ownerArgument}{WithOne(fields, text, "stored")});",
                string.Empty,
                $"        %L%.{text.Name}.ShouldBe(stored);",
                "    }",
            ]));
        }

        tests.Add(Joined(
        [
            "    [Fact]",
            "    public void Rehydrate_RefusesAnEmptyId() =>",
            $"        Should.Throw<DomainException>(() => %A%.Rehydrate(Guid.Empty, {ownerArgument}{ones}));",
        ]));

        return Joined(tests, "\n\n");
    }

    internal static string ConfigurationTests(IReadOnlyList<Field> fields, string holder)
    {
        var tests = new List<string>();
        var texts = fields.Where(IsReference).ToList();
        var decimals = fields.Where(field => field.Kind == FieldKind.Decimal).ToList();

        if (texts.Count > 0)
        {
            tests.Add(Joined(
            [
                "    [Theory]",
                .. texts.Select(field => $"    [InlineData(nameof(%A%Record.{field.Name}), {holder}.{field.Name}MaxLength)]"),
                "    public void EveryTextColumn_IsAsLongAsTheValidatorAllows(string property, int expected) =>",
                "        Entity().FindProperty(property)!.GetMaxLength().ShouldBe(expected);",
            ]));
        }

        if (decimals.Count > 0)
        {
            tests.Add(Joined(
            [
                "    [Theory]",
                .. decimals.Select(field => $"    [InlineData(nameof(%A%Record.{field.Name}), {holder}.{field.Name}Precision, {holder}.{field.Name}Scale)]"),
                "    public void EveryDecimalColumn_HasThePrecisionAndScaleTheValidatorAllows(string property, int precision, int scale)",
                "    {",
                "        var column = Entity().FindProperty(property)!;",
                string.Empty,
                "        column.GetPrecision().ShouldBe(precision);",
                "        column.GetScale().ShouldBe(scale);",
                "    }",
            ]));
        }

        tests.Add(Joined(
        [
            "    [Fact]",
            "    public void EveryRequiredField_IsANonNullableColumn()",
            "    {",
            .. fields.Select(field =>
                $"        Entity().FindProperty(nameof(%A%Record.{field.Name}))!.IsNullable.ShouldBe{(field.Nullable ? "True" : "False")}();"),
            "    }",
        ]));

        return Joined(tests, "\n\n");
    }
}

internal static class CSharpNames
{
    internal static readonly string[] Keywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if",
        "implicit", "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new",
        "null", "object", "operator", "out", "override", "params", "private", "protected", "public",
        "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static",
        "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong",
        "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    ];

    internal static readonly Regex PascalCase = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    internal static string Lower(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}

internal enum FieldKind
{
    String,
    Text,
    Int,
    Long,
    Decimal,
    Bool,
    DateOnly,
    DateTimeOffset,
    Guid,
}

internal sealed record FieldBound(string Operator, decimal Value, string Literal)
{
    internal bool IsLower => Operator[0] == '>';

    internal bool IsInclusive => Operator.Length == 2;

    internal string Suffix => (IsInclusive ? string.Empty : "Exclusive") + (IsLower ? "Minimum" : "Maximum");

    internal string Wording => Operator switch
    {
        ">=" => "at least",
        ">" => "greater than",
        "<=" => "at most",
        _ => "less than",
    };
}

internal sealed record Field(
    string Name,
    FieldKind Kind,
    bool Nullable,
    int Length,
    int Precision,
    int Scale,
    FieldBound? Lower,
    FieldBound? Upper,
    int Position)
{
    internal IEnumerable<FieldBound> Bounds => new[] { Lower, Upper }.OfType<FieldBound>();

    internal string TypeText => Kind switch
    {
        FieldKind.String => Length == 0 ? "string" : $"string({Length})",
        FieldKind.Text => $"text({Length})",
        FieldKind.Decimal => Precision == 0 ? "decimal" : $"decimal({Precision},{Scale})",
        FieldKind.Int => "int",
        FieldKind.Long => "long",
        FieldKind.Bool => "bool",
        FieldKind.DateOnly => "DateOnly",
        FieldKind.DateTimeOffset => "DateTimeOffset",
        _ => "Guid",
    };

    internal string ClrType => Kind switch
    {
        FieldKind.String or FieldKind.Text => "string",
        FieldKind.Int => "int",
        FieldKind.Long => "long",
        FieldKind.Decimal => "decimal",
        FieldKind.Bool => "bool",
        FieldKind.DateOnly => "DateOnly",
        FieldKind.DateTimeOffset => "DateTimeOffset",
        _ => "Guid",
    };

    internal string Declaration =>
        $"{Name}:{TypeText}{(Nullable ? "?" : string.Empty)}{string.Concat(Bounds.Select(bound => bound.Operator + bound.Literal))}";
}

internal sealed record FieldProfile(
    string Holder,
    string Types,
    string Example,
    FieldKind[] Kinds,
    bool Stored,
    string[] ReservedMembers,
    string ReservedWording,
    bool ReservesGeneratedNames,
    string DuplicateReason,
    string UnstoredReason)
{
    internal static FieldProfile Feature { get; } = new(
        "a feature",
        "string(n), text, text(n), int, long, decimal, decimal(p,s), bool, DateOnly, DateTimeOffset, Guid",
        "--fields \"Title:string(120), Budget:decimal>=0\"",
        Enum.GetValues<FieldKind>(),
        Stored: true,
        [
            "Id", "OwnerId", "Version", "CreatedAt", "CreatedBy", "LastModifiedAt", "LastModifiedBy", "Precondition",
            "AnonymisedText", "DomainEvents", "ClearDomainEvents", "RaiseDomainEvent", "Create", "Rehydrate", "Update",
            "Anonymise", "EqualityContract", "Deconstruct", "PrintMembers", "Equals", "GetHashCode", "GetType", "ToString",
            "MemberwiseClone", "Finalize",
        ],
        "a column or a member the feature already declares (the id, the owner, the version, the audit stamps, or a method "
            + "of the aggregate or of a record)",
        ReservesGeneratedNames: true,
        "two columns a case-insensitive database cannot tell apart",
        string.Empty);

    internal static FieldProfile Event { get; } = new(
        "an event",
        "Guid, string, int, long, bool, decimal, DateOnly, DateTimeOffset",
        "--fields \"InvoiceId:Guid, Amount:decimal\"",
        [FieldKind.Guid, FieldKind.String, FieldKind.Int, FieldKind.Long, FieldKind.Bool, FieldKind.Decimal, FieldKind.DateOnly, FieldKind.DateTimeOffset],
        Stored: false,
        [
            "OccurredOn", "EqualityContract", "Deconstruct", "PrintMembers", "Equals", "GetHashCode", "GetType", "ToString",
            "MemberwiseClone", "Finalize",
        ],
        "a member the event declares itself (OccurredOn, or a method of a record)",
        ReservesGeneratedNames: false,
        "two properties a case-insensitive JSON reader cannot tell apart",
        "No column holds an event's fields, and the aggregate checks a value before it raises the event with it: a length, a "
            + "precision, a bound or a missing value is the aggregate's rule, not the event's");
}

internal static class FieldGrammar
{
    internal const int StringMaxLength = 400;

    internal const int TextDefaultLength = 2_000;

    internal const int TextMaxLength = 65_536;

    internal const int DecimalDefaultPrecision = 18;

    internal const int DecimalDefaultScale = 2;

    internal const int DecimalMaxPrecision = 28;

    internal const int NameMaxLength = 40;

    private static readonly string[] _reservedTypes =
    [
        "Guid", "DateTimeOffset", "DateOnly", "TimeSpan", "String", "Decimal", "Math", "DomainException",
        "ArgumentNullException", "UserId", "JsonRequired",
    ];

    private static readonly string[] _generatedSuffixes =
        ["MaxLength", "Precision", "Scale", "Minimum", "ExclusiveMinimum", "Maximum", "ExclusiveMaximum", "Field"];

    private static readonly string[] _generatedPrefixes = ["Require", "Valid"];

    private static readonly string[] _contextualKeywords = ["await", "nameof", "var", "value"];

    private static readonly Dictionary<string, (string Reason, string Stored, string Unstored)> _refusedTypes = new(StringComparer.Ordinal)
    {
        ["enum"] = (
            "enum is not a field type: an enum's values are a decision of the domain, and a stored enum changes "
                + "meaning when a member is reordered or renamed",
            "Declare a string(n) and check its values by hand, or write a value object",
            "Carry a string, whose values the aggregate checks"),
        ["DateTime"] = ("DateTime is not a field type: it carries no offset, so the instant it names depends on the server", "Use DateTimeOffset", "Use DateTimeOffset"),
        ["double"] = ("double is not a field type: it is not exact, and the providers store it differently", "Use decimal or decimal(p,s)", "Use decimal"),
        ["float"] = ("float is not a field type: it is not exact, and the providers store it differently", "Use decimal or decimal(p,s)", "Use decimal"),
        ["short"] = ("short is not a field type", "Use int, with bounds if the range matters", "Use int"),
        ["byte"] = ("byte is not a field type", "Use int, with bounds if the range matters", "Use int"),
        ["uint"] = ("uint is not a field type: no provider stores it alike", "Use long, with >=0", "Use long"),
        ["ulong"] = ("ulong is not a field type: no provider stores it alike", "Use long, with >=0, or decimal(20,0)", "Use long or decimal"),
        ["char"] = ("char is not a field type", "Use string(1)", "Use string"),
    };

    private static readonly Dictionary<string, FieldKind> _kinds = new(StringComparer.Ordinal)
    {
        ["string"] = FieldKind.String,
        ["text"] = FieldKind.Text,
        ["int"] = FieldKind.Int,
        ["long"] = FieldKind.Long,
        ["decimal"] = FieldKind.Decimal,
        ["bool"] = FieldKind.Bool,
        ["DateOnly"] = FieldKind.DateOnly,
        ["DateTimeOffset"] = FieldKind.DateTimeOffset,
        ["Guid"] = FieldKind.Guid,
    };

    private enum TokenKind
    {
        Name,
        Number,
        Symbol,
        Unknown,
        End,
    }

    private sealed record Token(TokenKind Kind, string Text, int Position);

    internal static List<Field>? Parse(string text, FieldProfile profile, List<string> problems)
    {
        int before = problems.Count;
        var tokens = Tokenize(text);

        if (tokens.Count == 1)
        {
            problems.Add($"--fields is empty. It lists the fields, Name:type, comma separated, as in {profile.Example}.");

            return null;
        }

        var fields = new List<Field>();
        int at = 0;

        while (true)
        {
            if (ParseField(tokens, ref at, profile, problems) is not { } field)
            {
                return null;
            }

            fields.Add(field);

            if (tokens[at].Kind == TokenKind.End)
            {
                break;
            }

            if (tokens[at].Text != ",")
            {
                problems.Add(Expected(tokens[at], "a comma before the next field, or the end of the list"));

                return null;
            }

            at++;
        }

        problems.AddRange(NameProblems(fields, profile));

        return problems.Count == before ? fields : null;
    }

    private static Field? ParseField(List<Token> tokens, ref int at, FieldProfile profile, List<string> problems)
    {
        var name = tokens[at];

        if (name.Kind != TokenKind.Name)
        {
            problems.Add(Expected(name, "a field name, PascalCase, as Title"));

            return null;
        }

        at++;

        if (tokens[at].Text != ":")
        {
            problems.Add(Expected(tokens[at], $"':' after the field name {Shown(name.Text)}, then its type"));

            return null;
        }

        at++;

        var typeToken = tokens[at];

        if (typeToken.Kind != TokenKind.Name)
        {
            problems.Add(Expected(typeToken, $"a type, one of {profile.Types}"));

            return null;
        }

        if (_kinds.TryGetValue(typeToken.Text, out FieldKind named) && !profile.Kinds.Contains(named))
        {
            problems.Add($"--fields, at character {typeToken.Position + 1}: {typeToken.Text} is not a type of {profile.Holder}, which carries {profile.Types}.");

            return null;
        }

        at++;

        if (!profile.Stored && tokens[at].Text == "(")
        {
            problems.Add(Unstored(tokens[at], profile, "length or precision"));

            return null;
        }

        int length = 0, precision = 0, scale = 0;
        FieldKind kind;

        switch (typeToken.Text)
        {
            case "string" when !profile.Stored:
                kind = FieldKind.String;
                break;

            case "string":
                if (tokens[at].Text != "(")
                {
                    problems.Add(
                        $"--fields, at character {tokens[at].Position + 1}: string needs its maximum length, as string(120), "
                        + "so that no column is unbounded; a long free text is text or text(n).");

                    return null;
                }

                if (Arguments(tokens, ref at, 1, problems) is not [var maximum])
                {
                    return null;
                }

                if (maximum < 1 || maximum > StringMaxLength)
                {
                    problems.Add(
                        $"--fields: {name.Text} is string({maximum}), and a string is 1 to {StringMaxLength} characters long: "
                        + $"{StringMaxLength} keeps every string column indexable, and so sortable, on the three providers "
                        + "(SQL Server's 1,700-byte index key holds an nvarchar(400) beside the owner and the id). A longer value is text(n).");
                }

                kind = FieldKind.String;
                length = maximum;
                break;

            case "text":
                length = TextDefaultLength;

                if (tokens[at].Text == "(")
                {
                    if (Arguments(tokens, ref at, 1, problems) is not [var limit])
                    {
                        return null;
                    }

                    length = limit;
                }

                if (length < 1 || length > TextMaxLength)
                {
                    problems.Add(
                        $"--fields: {name.Text} is text({length}), and a text is 1 to {TextMaxLength:N0} characters long: the API "
                        + $"refuses a body over RequestLimits:MaxRequestBodyBytes, {TextMaxLength:N0} bytes by default, so a longer "
                        + "field could not arrive in a request. A document is a file.");
                }

                kind = FieldKind.Text;
                break;

            case "decimal" when !profile.Stored:
                kind = FieldKind.Decimal;
                break;

            case "decimal":
                precision = DecimalDefaultPrecision;
                scale = DecimalDefaultScale;

                if (tokens[at].Text == "(")
                {
                    if (Arguments(tokens, ref at, 2, problems) is not [var digits, var decimals])
                    {
                        return null;
                    }

                    precision = digits;
                    scale = decimals;
                }

                if (precision < 1 || precision > DecimalMaxPrecision || scale < 0 || scale > precision)
                {
                    problems.Add(
                        $"--fields: {name.Text} is decimal({precision},{scale}), and a decimal(p,s) has 1 to {DecimalMaxPrecision} "
                        + $"digits in all, the most a .NET decimal holds exactly, of which 0 to p after the point.");
                }

                kind = FieldKind.Decimal;
                break;

            case "int":
                kind = FieldKind.Int;
                break;

            case "long":
                kind = FieldKind.Long;
                break;

            case "bool":
                kind = FieldKind.Bool;
                break;

            case "DateOnly":
                kind = FieldKind.DateOnly;
                break;

            case "DateTimeOffset":
                kind = FieldKind.DateTimeOffset;
                break;

            case "Guid":
                kind = FieldKind.Guid;
                break;

            default:
                problems.Add(_refusedTypes.TryGetValue(typeToken.Text, out var refusal)
                    ? $"--fields, at character {typeToken.Position + 1}: {refusal.Reason}. {(profile.Stored ? refusal.Stored : refusal.Unstored)}."
                    : Expected(typeToken, $"a type, one of {profile.Types}, written as listed, case included"));

                return null;
        }

        bool nullable = false;

        if (tokens[at].Text == "?" && !profile.Stored)
        {
            problems.Add(Unstored(tokens[at], profile, "missing value"));

            return null;
        }

        if (tokens[at].Text == "?")
        {
            nullable = true;
            at++;
        }

        FieldBound? lower = null, upper = null;

        while (tokens[at].Text is ">=" or "<=" or ">" or "<")
        {
            var operation = tokens[at];

            if (!profile.Stored)
            {
                problems.Add(Unstored(operation, profile, "bound"));

                return null;
            }

            if (kind is not (FieldKind.Int or FieldKind.Long or FieldKind.Decimal))
            {
                problems.Add(
                    $"--fields, at character {operation.Position + 1}: {name.Text} is {typeToken.Text}, and only int, long and decimal "
                    + "take a bound. A string or a text is bounded by its length; a date's rule is usually relative to the clock, "
                    + "\"in the future\", which belongs in the domain beside the clock, not in a constant.");

                return null;
            }

            at++;

            var number = tokens[at];

            if (number.Kind != TokenKind.Number)
            {
                problems.Add(Expected(number, $"a number after '{operation.Text}'"));

                return null;
            }

            at++;

            if (Bound(name.Text, kind, precision, scale, operation.Text, number, problems) is not { } bound)
            {
                continue;
            }

            if (bound.IsLower ? lower is not null : upper is not null)
            {
                problems.Add(
                    $"--fields, at character {operation.Position + 1}: {name.Text} already has {(bound.IsLower ? "a lower" : "an upper")} "
                    + "bound; a field takes at most one of each.");

                continue;
            }

            if (bound.IsLower)
            {
                lower = bound;
            }
            else
            {
                upper = bound;
            }
        }

        var field = new Field(name.Text, kind, nullable, length, precision, scale, lower, upper, name.Position);

        if (lower is not null || upper is not null)
        {
            problems.AddRange(RangeProblems(field));
        }

        return field;
    }

    private static int[]? Arguments(List<Token> tokens, ref int at, int count, List<string> problems)
    {
        at++;
        var values = new List<int>();

        for (int index = 0; index < count; index++)
        {
            if (index > 0)
            {
                if (tokens[at].Text != ",")
                {
                    problems.Add(Expected(tokens[at], "',' between the precision and the scale"));

                    return null;
                }

                at++;
            }

            var number = tokens[at];

            if (number.Kind != TokenKind.Number
                || !int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                problems.Add(Expected(number, "a whole number"));

                return null;
            }

            values.Add(value);
            at++;
        }

        if (tokens[at].Text != ")")
        {
            problems.Add(Expected(tokens[at], "')'"));

            return null;
        }

        at++;

        return [.. values];
    }

    private static FieldBound? Bound(string name, FieldKind kind, int precision, int scale, string operation, Token number, List<string> problems)
    {
        string where = $"--fields, at character {number.Position + 1}";

        if (!decimal.TryParse(number.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
        {
            problems.Add($"{where}: {Shown(number.Text)} is not a number this grammar reads.");

            return null;
        }

        if (kind is FieldKind.Int or FieldKind.Long)
        {
            decimal minimum = kind == FieldKind.Int ? int.MinValue : long.MinValue;
            decimal maximum = kind == FieldKind.Int ? int.MaxValue : long.MaxValue;
            string type = kind == FieldKind.Int ? "int" : "long";

            if (number.Text.Contains('.', StringComparison.Ordinal) || value < minimum || value > maximum)
            {
                problems.Add($"{where}: {Shown(number.Text)} is not a whole number an {type} holds.");

                return null;
            }

            if ((operation == ">=" && value == minimum) || (operation == "<=" && value == maximum))
            {
                problems.Add($"{where}: {name}{operation}{number.Text} bounds nothing: every {type} satisfies it. Leave it out.");

                return null;
            }

            return new FieldBound(operation, value, value.ToString(CultureInfo.InvariantCulture));
        }

        int decimals = number.Text.Contains('.', StringComparison.Ordinal) ? number.Text.Length - number.Text.IndexOf('.', StringComparison.Ordinal) - 1 : 0;
        int digits = decimal.Truncate(Math.Abs(value)) == 0 ? 0 : decimal.Truncate(Math.Abs(value)).ToString(CultureInfo.InvariantCulture).Length;

        if (decimals > scale || digits > precision - scale)
        {
            problems.Add(
                $"{where}: {Shown(number.Text)} does not fit decimal({precision},{scale}), whose values have at most "
                + $"{precision - scale} digits before the point and {scale} after it.");

            return null;
        }

        return new FieldBound(operation, value, value.ToString(CultureInfo.InvariantCulture));
    }

    private static IEnumerable<string> RangeProblems(Field field)
    {
        decimal step = field.Kind == FieldKind.Decimal ? Step(field.Scale) : 1m;
        decimal floor = field.Kind switch
        {
            FieldKind.Int => int.MinValue,
            FieldKind.Long => long.MinValue,
            _ => -Ceiling(field) + step,
        };
        decimal top = field.Kind switch
        {
            FieldKind.Int => int.MaxValue,
            FieldKind.Long => long.MaxValue,
            _ => Ceiling(field) - step,
        };

        decimal low = field.Lower is { } lower ? Math.Max(floor, lower.IsInclusive ? lower.Value : lower.Value + step) : floor;
        decimal high = field.Upper is { } upper ? Math.Min(top, upper.IsInclusive ? upper.Value : upper.Value - step) : top;

        if (low > high)
        {
            yield return $"--fields: no value satisfies the bounds of {field.Declaration}. Widen them.";
        }
    }

    internal static decimal Step(int scale)
    {
        decimal step = 1m;

        for (int index = 0; index < scale; index++)
        {
            step /= 10m;
        }

        return step;
    }

    internal static decimal Ceiling(Field field)
    {
        decimal ceiling = 1m;

        for (int index = 0; index < field.Precision - field.Scale; index++)
        {
            ceiling *= 10m;
        }

        return ceiling;
    }

    private static List<string> NameProblems(List<Field> fields, FieldProfile profile)
    {
        var problems = new List<string>();

        foreach (var field in fields)
        {
            string where = $"--fields, at character {field.Position + 1}";

            if (!CSharpNames.PascalCase.IsMatch(field.Name))
            {
                problems.Add($"{where}: {Shown(field.Name)} is not a field name: PascalCase, letters and digits, as DueDate.");

                continue;
            }

            if (field.Name.Length > NameMaxLength)
            {
                problems.Add($"{where}: {field.Name} is longer than {NameMaxLength} characters, which makes names some tools cannot hold. Shorten it.");
            }

            string parameter = CSharpNames.Lower(field.Name);

            if (CSharpNames.Keywords.Contains(parameter, StringComparer.Ordinal) || _contextualKeywords.Contains(parameter, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{where}: {field.Name} becomes '{parameter}' once its first letter is lowered, which is how the generated "
                    + "parameters are named, and C# reserves that word there. Choose another name.");
            }

            if (profile.ReservedMembers.Contains(field.Name, StringComparer.Ordinal))
            {
                problems.Add($"{where}: {field.Name} is {profile.ReservedWording}. Choose another name.");
            }

            if (_reservedTypes.Contains(field.Name, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{where}: {field.Name} names a type the generated code calls by name, which a property of that name would hide. "
                    + "Choose another name.");
            }

            foreach (var other in fields.Where(other => profile.ReservesGeneratedNames && !ReferenceEquals(other, field)))
            {
                if (_generatedSuffixes.Any(suffix => field.Name == other.Name + suffix)
                    || _generatedPrefixes.Any(prefix => field.Name == prefix + other.Name))
                {
                    problems.Add(
                        $"{where}: {field.Name} is the name the generated code gives a constant, a method or a local of {other.Name}. "
                        + "Choose another name.");
                }
            }
        }

        foreach (var group in fields.GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            problems.Add(
                $"--fields names {string.Join(" and ", group.Select(field => field.Name))}, the same name but for its case: "
                + $"{profile.DuplicateReason}. Keep one.");
        }

        return problems;
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int position = 0;

        while (position < text.Length)
        {
            char current = text[position];

            if (char.IsWhiteSpace(current))
            {
                position++;

                continue;
            }

            int start = position;

            if (char.IsAsciiLetter(current) || current == '_')
            {
                while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
                {
                    position++;
                }

                tokens.Add(new Token(TokenKind.Name, text[start..position], start));
            }
            else if (char.IsAsciiDigit(current) || (current == '-' && position + 1 < text.Length && char.IsAsciiDigit(text[position + 1])))
            {
                position++;

                while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.'))
                {
                    position++;
                }

                tokens.Add(new Token(TokenKind.Number, text[start..position], start));
            }
            else if (current is '>' or '<')
            {
                position += position + 1 < text.Length && text[position + 1] == '=' ? 2 : 1;
                tokens.Add(new Token(TokenKind.Symbol, text[start..position], start));
            }
            else if (current is ':' or ',' or '(' or ')' or '?')
            {
                position++;
                tokens.Add(new Token(TokenKind.Symbol, current.ToString(), start));
            }
            else
            {
                position++;
                tokens.Add(new Token(TokenKind.Unknown, current.ToString(), start));
            }
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, text.Length));

        return tokens;
    }

    private static string Unstored(Token token, FieldProfile profile, string part) =>
        $"--fields, at character {token.Position + 1}: a field of {profile.Holder} takes no {part}. {profile.UnstoredReason}.";

    private static string Expected(Token token, string expected) =>
        $"--fields, at character {token.Position + 1}: expected {expected}, and found "
        + (token.Kind == TokenKind.End ? "the end of the list." : $"{Shown(token.Text)}.");

    private static string Shown(string text) => text.Length <= 40 ? $"'{text}'" : $"'{text[..40]}...'";
}

internal static class TextEdits
{
    private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal sealed record Edit(string Path, string Anchor, string Position, string[] Lines);

    internal sealed record Command(string Display, string WorkingDirectory, string FileName, string[] Arguments)
    {
        internal bool FromLastBuild => Arguments.Contains(_noBuild, StringComparer.Ordinal);
    }

    private const string _noBuild = "--no-build";

    internal sealed class Sheet
    {
        private readonly bool _bom;

        private readonly string _newline;

        private readonly List<string> _lines;

        private readonly List<bool> _inserted;

        internal Sheet(string path, byte[] original)
        {
            Path = path;
            Original = original;
            _bom = original.AsSpan().StartsWith(Encoding.UTF8.Preamble);

            string text = _strictUtf8.GetString(original.AsSpan(_bom ? 3 : 0));

            _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            FinalNewline = text.EndsWith('\n');
            _lines = [.. (FinalNewline ? text[..^_newline.Length] : text).Split(_newline)];
            _inserted = [.. _lines.Select(_ => false)];
        }

        internal string Path { get; }

        internal byte[] Original { get; }

        internal IReadOnlyList<string> Lines => _lines;

        internal bool FinalNewline { get; private set; }

        internal bool Rewritten { get; private set; }

        internal string Text => string.Join('\n', _lines) + (FinalNewline ? "\n" : string.Empty);

        internal void Rewrite(string text)
        {
            bool finalNewline = text.EndsWith('\n');
            string[] next = (finalNewline ? text[..^1] : text).Split('\n');
            int head = 0;

            while (head < next.Length && head < _lines.Count && next[head] == _lines[head])
            {
                head++;
            }

            int tail = 0;

            while (tail < next.Length - head && tail < _lines.Count - head && next[^(tail + 1)] == _lines[^(tail + 1)])
            {
                tail++;
            }

            int removed = _lines.Count - head - tail;

            Rewritten |= removed > 0 || finalNewline != FinalNewline;
            _lines.RemoveRange(head, removed);
            _inserted.RemoveRange(head, removed);
            Insert(head, next[head..^tail]);
            FinalNewline = finalNewline;
        }

        internal void Insert(int index, string[] lines)
        {
            _lines.InsertRange(index, lines);
            _inserted.InsertRange(index, lines.Select(_ => true));
        }

        internal IEnumerable<Edit> Edits()
        {
            for (int start = 0; start < _lines.Count; start++)
            {
                if (!_inserted[start])
                {
                    continue;
                }

                int end = start;

                while (end + 1 < _lines.Count && _inserted[end + 1])
                {
                    end++;
                }

                int next = end + 1 < _lines.Count ? end + 1 : -1;
                int previous = start - 1;
                bool before = next >= 0 && (_lines[next].Trim().Length > 0 || previous < 0 || _lines[previous].Trim().Length == 0);

                yield return new Edit(Path, _lines[before ? next : previous], before ? "before" : "after", [.. _lines.Skip(start).Take(end - start + 1)]);

                start = end;
            }
        }

        internal byte[] Bytes()
        {
            byte[] text = Encoding.UTF8.GetBytes(string.Join(_newline, Lines) + (FinalNewline ? _newline : string.Empty));

            return _bom ? [.. Encoding.UTF8.Preamble, .. text] : text;
        }
    }

    internal sealed class Editor(string root)
    {
        private readonly Dictionary<string, Sheet?> _sheets = new(StringComparer.Ordinal);

        internal string Step { get; set; } = string.Empty;

        internal IReadOnlyList<Edit> Applied =>
            _sheets.Values.OfType<Sheet>().Any(sheet => sheet.Rewritten)
                ? throw new InvalidOperationException("An edit that changed or removed a line cannot be recorded to be undone, only one that adds lines.")
                : [.. _sheets.Values.OfType<Sheet>().SelectMany(sheet => sheet.Edits())];

        internal List<string> Kept { get; } = [];

        internal List<string> Failures { get; } = [];

        internal IEnumerable<Sheet> Changed =>
            _sheets.Values.OfType<Sheet>().Where(sheet => !sheet.Original.AsSpan().SequenceEqual(sheet.Bytes()));

        internal Sheet? Open(string path)
        {
            if (!_sheets.TryGetValue(path, out Sheet? sheet))
            {
                string full = System.IO.Path.Combine(root, path);

                sheet = File.Exists(full) ? new Sheet(path, File.ReadAllBytes(full)) : null;
                _sheets[path] = sheet;
            }

            if (sheet is null)
            {
                Fail($"{path} does not exist.");
            }

            return sheet;
        }

        internal void Fail(string reason) => Failures.Add(Step.Length == 0 ? reason : $"{Step}: {reason}");

        internal static void Insert(Sheet sheet, int anchor, bool before, string[] lines) => sheet.Insert(before ? anchor : anchor + 1, lines);

        internal static int Find(Sheet sheet, Func<string, bool> match, int from = 0, int to = -1)
        {
            int end = to < 0 ? sheet.Lines.Count : Math.Min(to, sheet.Lines.Count);

            for (int index = Math.Max(from, 0); index < end; index++)
            {
                if (match(sheet.Lines[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        internal static int FindLast(Sheet sheet, Func<string, bool> match, int from)
        {
            for (int index = Math.Min(from, sheet.Lines.Count - 1); index >= 0; index--)
            {
                if (match(sheet.Lines[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        internal void Place(string path, string[] anchors, bool before, string[] lines, string present)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (Find(sheet, line => line.Trim().StartsWith(present, StringComparison.Ordinal)) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int anchor = anchors
                .Select(text => Find(sheet, line => line.Trim().StartsWith(text, StringComparison.Ordinal)))
                .FirstOrDefault(index => index >= 0, -1);

            if (anchor < 0)
            {
                Fail($"no line starts with '{string.Join("' or '", anchors)}', the line the edit is placed beside.");

                return;
            }

            Insert(sheet, anchor, before, lines);
        }

        internal void Rewrite(string path, Func<string, string?> edit)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (edit(sheet.Text) is { } text)
            {
                sheet.Rewrite(text);
            }
            else
            {
                Fail($"its anchor is not in {path}, so nothing could be written.");
            }
        }

        internal void PlaceInEachMethod(string path, string anchor, bool before, string line)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            int found = 0;

            for (int index = 0; index < sheet.Lines.Count; index++)
            {
                if (sheet.Lines[index].Trim() != anchor)
                {
                    continue;
                }

                found++;

                int start = FindLast(sheet, candidate => candidate == "    {", index);
                int end = Find(sheet, candidate => candidate == "    }", index);

                if (Find(sheet, candidate => candidate.Trim() == line.Trim(), start, end) >= 0)
                {
                    Kept.Add(Step);

                    continue;
                }

                Insert(sheet, index, before, [line]);
                index++;
            }

            if (found == 0)
            {
                Fail($"no line reads '{anchor}', the line the edit is placed beside in each method.");
            }
        }

        internal void AppendToMethod(string path, string method, string prefix, string line)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            int declaration = Find(sheet, candidate => candidate.Contains(method, StringComparison.Ordinal));
            int start = declaration < 0 ? -1 : Find(sheet, candidate => candidate == "    {", declaration);
            int end = start < 0 ? -1 : Find(sheet, candidate => candidate == "    }", start);

            if (end < 0)
            {
                Fail($"no method{method} with a body was found to take the line.");

                return;
            }

            if (Find(sheet, candidate => candidate.Trim() == line.Trim(), start, end) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int last = FindLast(sheet, candidate => candidate.Trim().StartsWith(prefix, StringComparison.Ordinal), end);

            if (last <= start)
            {
                Fail($"the method{method} has no line starting with '{prefix}' to place the line after.");

                return;
            }

            Insert(sheet, last, before: false, [line]);
        }

        internal void Using(string path, string space)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            string directive = $"using {space};";
            int body = Find(sheet, line => !IsHeader(line));
            int[] usings = [.. Enumerable.Range(0, body < 0 ? sheet.Lines.Count : body).Where(index => IsUsing(sheet.Lines[index]))];
            string? own = sheet.Lines
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("namespace ", StringComparison.Ordinal) && line.EndsWith(';'))?["namespace ".Length..^1];

            if (usings.Any(index => sheet.Lines[index].Trim() == directive)
                || (own is not null && (own == space || own.StartsWith(space + ".", StringComparison.Ordinal))))
            {
                return;
            }

            int following = usings.FirstOrDefault(index => Order(space, sheet.Lines[index].Trim()["using ".Length..].TrimEnd(';')) < 0, -1);

            if (following >= 0)
            {
                Insert(sheet, following, before: true, [directive]);
            }
            else if (usings.Length > 0)
            {
                Insert(sheet, usings[^1], before: false, [directive]);
            }
            else
            {
                Insert(sheet, 0, before: true, [directive, string.Empty]);
            }
        }

        internal void Row(string path, string heading, string row, string present)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (Find(sheet, line => line.Contains(present, StringComparison.Ordinal)) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int level = heading.TakeWhile(character => character == '#').Count();
            int section = Find(sheet, line => line == heading);
            int next = section < 0 ? -1 : Find(sheet, line => line.StartsWith('#') && line.TakeWhile(character => character == '#').Count() <= level, section + 1);
            int rule = section < 0 ? -1 : Find(sheet, line => line.StartsWith("|---", StringComparison.Ordinal), section + 1, next);

            if (rule < 0)
            {
                Fail($"{path} has no table under '{heading}' to take the row.");

                return;
            }

            int last = rule;

            while (last + 1 < sheet.Lines.Count && sheet.Lines[last + 1].StartsWith('|'))
            {
                last++;
            }

            Insert(sheet, last, before: false, [row]);
        }

        internal void Section(string path, string heading, string[] lines, string present)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (Find(sheet, line => line == present) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int section = Find(sheet, line => line == heading);
            int next = section < 0 ? -1 : Find(sheet, line => line.StartsWith("## ", StringComparison.Ordinal), section + 1);

            if (next < 0)
            {
                Fail($"{path} has no '{heading}' section followed by another, before which the section is placed.");

                return;
            }

            Insert(sheet, next, before: true, lines);
        }

        private static bool IsUsing(string line) =>
            line.StartsWith("using ", StringComparison.Ordinal) && line.TrimEnd().EndsWith(';') && !line.Contains('=', StringComparison.Ordinal);

        private static bool IsHeader(string line) =>
            line.Trim().Length == 0 || line.TrimStart().StartsWith("//", StringComparison.Ordinal) || IsUsing(line);

        private static int Order(string left, string right)
        {
            static bool IsSystem(string space) => space == "System" || space.StartsWith("System.", StringComparison.Ordinal);

            int system = IsSystem(right).CompareTo(IsSystem(left));

            return system != 0 ? system
                : StringComparer.OrdinalIgnoreCase.Compare(left, right) is var ignoringCase and not 0 ? ignoringCase
                : StringComparer.Ordinal.Compare(left, right);
        }
    }

    internal static void WriteEdits(Utf8JsonWriter writer, IEnumerable<Edit> edits)
    {
        writer.WriteStartArray("edits");

        foreach (Edit edit in edits)
        {
            writer.WriteStartObject();
            writer.WriteString("path", edit.Path);
            writer.WriteString("anchor", edit.Anchor);
            writer.WriteString("position", edit.Position);
            writer.WriteStartArray("lines");

            foreach (string line in edit.Lines)
            {
                writer.WriteStringValue(line);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    internal static void Commit(string root, IReadOnlyList<(string Path, byte[] Bytes)> created, IReadOnlyList<(string Path, byte[] Original, byte[] Bytes)> replaced)
    {
        var written = new List<string>();
        var directories = new List<string>();
        var overwritten = new List<(string Full, byte[] Original)>();
        string? staged = null;

        try
        {
            foreach (var (path, bytes) in created)
            {
                string full = System.IO.Path.Combine(root, path);
                var missing = new Stack<string>();

                for (string next = System.IO.Path.GetDirectoryName(full)!; !Directory.Exists(next); next = System.IO.Path.GetDirectoryName(next)!)
                {
                    missing.Push(next);
                }

                directories.AddRange(missing);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);

                using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write);

                written.Add(full);
                stream.Write(bytes);
            }

            foreach (var (path, original, bytes) in replaced)
            {
                string full = System.IO.Path.Combine(root, path);

                staged = $"{full}.{Guid.NewGuid():N}.tmp";

                using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(bytes);
                }

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(staged, File.GetUnixFileMode(full));
                }

                File.Move(staged, full, overwrite: true);
                staged = null;
                overwritten.Add((full, original));
            }
        }
        catch
        {
            if (staged is not null && File.Exists(staged))
            {
                File.Delete(staged);
            }

            foreach (var (full, original) in overwritten)
            {
                File.WriteAllBytes(full, original);
            }

            foreach (string full in written.Where(File.Exists))
            {
                File.Delete(full);
            }

            foreach (string directory in Enumerable.Reverse(directories))
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

            throw;
        }
    }

    internal static byte[] Encode(string path, string content)
    {
        byte[] text = Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n"));

        return path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".md", StringComparison.Ordinal)
            ? [.. Encoding.UTF8.GetPreamble(), .. text]
            : text;
    }

    internal static IEnumerable<(string Path, byte[] Original, byte[] Bytes)> Replacements(Editor editor) =>
        editor.Changed.Select(sheet => (sheet.Path, sheet.Original, sheet.Bytes()));

    internal static Command Task(string root, params string[] task) =>
        new($"dotnet run Tools/Tasks.cs {string.Join(' ', task)}", root, "dotnet", ["run", "Tools/Tasks.cs", .. task]);

    internal static Command FromLastBuild(string root, params string[] task) => Task(root, ["--", .. task, _noBuild]);

    internal static List<Command> ContractSteps(string root, string client, bool built) =>
    [
        built ? FromLastBuild(root, "openapi") : Task(root, "openapi"),
        .. Directory.Exists(System.IO.Path.Combine(root, "Src", "Clients", client)) ? [Task(root, "api-client")] : Array.Empty<Command>(),
        .. File.Exists(System.IO.Path.Combine(root, "Src", "Web", "packages", "api-client", "scripts", "generate.mjs"))
            ? [new Command($"cd Src/Web && pnpm api {_noBuild}", System.IO.Path.Combine(root, "Src", "Web"), OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm", ["api", _noBuild])]
            : Array.Empty<Command>(),
    ];

    internal static int Execute(Command command)
    {
        var start = new System.Diagnostics.ProcessStartInfo(command.FileName) { WorkingDirectory = command.WorkingDirectory, UseShellExecute = false };

        foreach (string argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(start)!;

            process.WaitForExit();

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Console.Error.WriteLine($"{command.FileName} could not be started: {exception.Message}");

            return 127;
        }
    }

    internal static bool RunAll(IReadOnlyList<Command> steps, Func<Command, int> run, TextWriter output, TextWriter error, string done)
    {
        for (int index = 0; index < steps.Count; index++)
        {
            output.WriteLine();
            output.WriteLine($"Running {steps[index].Display}");
            output.Flush();

            int code = run(steps[index]);

            if (code == 0)
            {
                continue;
            }

            error.WriteLine($"error: `{steps[index].Display}` failed with exit code {code}.");
            error.WriteLine($"Done: {done}{string.Concat(steps.Take(index).Select(step => $", then `{step.Display}`"))}.");
            error.WriteLine("What remains, in this order, once the failure is fixed:");

            foreach (Command remaining in steps.Skip(index))
            {
                error.WriteLine($"  {remaining.Display}");
            }

            if (steps.Skip(index).Any(remaining => remaining.FromLastBuild))
            {
                error.WriteLine(
                    $"A command with {_noBuild} works from the build this command made: when the fix changes the code, run "
                    + "`dotnet run Tools/Tasks.cs build` before it.");
            }

            return false;
        }

        output.WriteLine();
        output.WriteLine($"Done: {done}{string.Concat(steps.Select(step => $", then `{step.Display}`"))}.");

        return true;
    }
}
