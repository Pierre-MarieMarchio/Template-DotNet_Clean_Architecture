using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;
using static CleanArch.Cli.Generators.Shared.CSharpNames;
using static CleanArch.Cli.Generators.Shared.FeatureCode;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.AddFeature;

internal static class NewFeature
{

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

    private static readonly string[] _reservedNames =
    [
        "Access", "Api", "Application", "Auth", "Common", "Contracts", "Controllers", "Core", "Dtos",
        "Domain", "Entities", "Errors", "Features", "Infrastructure", "Model", "Models", "Persistence",
        "Policies", "Ports", "Presentation", "Rows", "Services", "UseCases",
    ];

    internal static int Run(AddFeatureRequest request, GenerationContext context, string version)
    {
        var problems = new List<string>();
        var spec = Check(request, context.Root, context.Project, problems);

        return spec is null
            ? Refusal.Write(context.Error, problems, _usage)
            : new FeatureGeneration(spec, problems, context, version).Generate();
    }

    private sealed class FeatureGeneration(Spec spec, IReadOnlyList<string> checkedProblems, GenerationContext context, string version) : Generation(context)
    {
        private List<Emission> _plan = [];

        private List<string> _skipped = [];

        private Emission? _page;

        private string? _unwritten;

        private TextEdits.Editor _editor = new(context.Root);

        private Emission? _manifest;

        protected override string Usage => _usage;

        protected override bool StopsAtDryRun => true;

        protected override IReadOnlyList<string> EditFailures => _editor.Failures;

        protected override IReadOnlyList<(string Path, byte[] Bytes)> Created =>
            [.. _plan.Select(Written), .. _page is null ? Array.Empty<(string, byte[])>() : [Written(_page)], Written(_manifest!)];

        protected override IReadOnlyList<(string Path, byte[] Original, byte[] Bytes)> Replaced => [.. TextEdits.Replacements(_editor)];

        protected override int? Concluded()
        {
            if (!AlreadyGenerated(Root, spec))
            {
                return null;
            }

            Output.WriteLine(
                $"{ManifestPath(spec)} records {spec.Features} with these options already: its files and edits are written, "
                + "so nothing is written again.");

            return Context.DryRun ? Succeeded : NewFeature.Finish(Root, spec, Output, Error, Context.Executor, resumed: true);
        }

        protected override bool Draft(List<string> problems)
        {
            _plan = Plan(spec, Root, out _skipped);
            problems.AddRange(checkedProblems);
            problems.AddRange(Conflicts(Root, spec, _plan));

            if (problems.Count > 0)
            {
                return false;
            }

            _page = PageOf(Root, spec, _plan, out _unwritten);
            _editor = Edits(Root, spec, _plan);
            _manifest = ManifestOf(spec, _plan, _page, _editor, version);

            return true;
        }

        protected override int RefuseEdits(IReadOnlyList<string> failures) =>
            RefuseAfterReport(
                failures,
                "Nothing was written: every edit is made or none is. Put back the line each failed edit is placed beside, "
                + "or write the feature by hand from the edits above.");

        protected override string? Preconditions() => FeatureCode.Preconditions(Root, spec, Output, Context.Executor);

        protected override void Report(ReportMode mode)
        {
            NewFeature.Report(Output, Root, spec, _plan, _manifest!.Path, _skipped, mode);
            ReportPage(Output, spec, _page, _unwritten, mode);

            if (mode == ReportMode.Applied && _editor.Kept.Count > 0)
            {
                Output.WriteLine();
                Output.WriteLine($"Already in place, so left as they were and not recorded: {string.Join("; ", _editor.Kept.Distinct(StringComparer.Ordinal))}.");
            }
        }

        protected override int Finish() => NewFeature.Finish(Root, spec, Output, Error, Context.Executor, resumed: false);

    }

    private static (string Path, byte[] Bytes) Written(Emission emission) => (emission.Path, emission.Bytes());

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

    private static int Finish(string root, Spec spec, TextWriter output, TextWriter error, IExecutor executor, bool resumed)
    {
        string migrations = System.IO.Path.Combine(root, PersistenceProject(spec), "Migrations");
        bool migrated = resumed
            && Directory.Exists(migrations)
            && Directory.EnumerateFiles(migrations, $"*_Add{spec.Features}.cs").Any();

        string done = resumed
            ? "the feature's files and edits, written by an earlier run"
            : "the feature's files, its edits and its manifest, which are consistent with each other";

        return TextEdits.RunAll(Steps(root, spec, migrated), executor.Run, output, error, done) ? 0 : 1;
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

    internal static Spec? Check(AddFeatureRequest request, string root, LocatedProject project, List<string> problems)
    {
        string features = request.Features;
        string type = request.Type;

        problems.AddRange(NameProblems(features, type));

        string access = request.Access;

        if (access is not ("owner" or "shared"))
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

        string prefix = project.File.Name;
        bool tablePrefixes;

        try
        {
            tablePrefixes = TablePrefixes(project);
        }
        catch (InvalidOperationException exception)
        {
            problems.Add(exception.Message);

            return null;
        }

        string? reaction = null;
        bool hasApi = Directory.Exists(Full(root, $"{Presentation}/{FolderOf(prefix, "Api")}"));

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
        string features = Full(root, $"{Presentation}/{host}/Features");

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
            manifest.Record(emission.Path, emission.Bytes());
        }

        manifest.Record(editor);

        return new Emission(manifest.Path, Encoding.UTF8.GetString(manifest.Bytes()));
    }

    internal static void Report(TextWriter output, string root, Spec spec, List<Emission> plan, string manifest, List<string> skipped, ReportMode mode)
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
            (mode == ReportMode.Applied ? "Wrote" : "Would write")
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
        output.WriteLine(mode == ReportMode.Applied
            ? $"Manifest: {manifest} (the options, the SHA-256 of each file above, and each edit below with the line it is placed beside)."
            : $"Manifest: {manifest} (the options, the SHA-256 of each file above, and each edit below with the line it is placed beside), not written.");

        foreach (string project in skipped)
        {
            output.WriteLine($"Not written: '{project}' is not in this tree, so its tests were skipped.");
        }

        output.WriteLine();
        output.WriteLine(mode switch
        {
            ReportMode.Applied => "The feature composes through these edits, each placed beside a line it found and recorded in the manifest.",
            ReportMode.DryRun => "A run without --dry-run makes these edits, each placed beside a line it finds, or none when a line is missing.",
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

        if (mode != ReportMode.Applied)
        {
            output.WriteLine(mode == ReportMode.DryRun
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

    internal static void ReportPage(TextWriter output, Spec spec, Emission? page, string? unwritten, ReportMode mode)
    {
        string path = PagePath(spec.Features);
        bool applied = mode == ReportMode.Applied;

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
