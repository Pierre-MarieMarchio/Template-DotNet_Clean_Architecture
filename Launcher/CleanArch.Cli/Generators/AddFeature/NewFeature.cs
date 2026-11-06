using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;
using static CleanArch.Cli.Generators.Shared.FeatureCode;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.AddFeature;

internal static class NewFeature
{

    internal const string Usage =
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
            ? Refusal.Write(context.Error, problems, Usage)
            : new FeatureGeneration(spec, problems, context, version).Generate();
    }

    internal static FeatureSpec? Check(AddFeatureRequest request, string root, LocatedProject project, List<string> problems)
    {
        problems.AddRange(NameProblems(request.Features, request.Type));
        CheckAccess(request, problems);

        string model = request.Model ?? "record";
        string concurrency = request.Concurrency ?? "none";

        CheckChoices(model, concurrency, problems);

        string[]? operations = ParseOperations(request.Operations, problems);
        var fields = request.Fields is null ? null : FieldGrammar.Parse(request.Fields, FieldProfile.Feature, problems);

        if (problems.Count > 0 || operations is null)
        {
            return null;
        }

        problems.AddRange(Contradictions(operations, model, concurrency));

        if (problems.Count > 0 || TablePrefixesOf(project, problems) is not { } tablePrefixes)
        {
            return null;
        }

        string prefix = project.File.Name;
        bool hasApi = Directory.Exists(Full(root, $"{Presentation}/{FolderOf(prefix, "Api")}"));
        string? reaction = request.Access == "owner" ? Reaction(root, prefix, hasApi, problems) : null;

        return new FeatureSpec(prefix, request.Features, request.Type, request.Access, request.Deletion ?? string.Empty, operations, model, concurrency, reaction, hasApi, fields, tablePrefixes);
    }

    private static void CheckAccess(AddFeatureRequest request, List<string> problems)
    {
        string access = request.Access;

        if (access is not ("owner" or "shared"))
        {
            problems.Add($"--access '{access}' is not a choice. It is owner or shared.");
        }

        if (access == "owner")
        {
            CheckDeletion(request.Deletion, problems);
        }
        else if (access == "shared" && request.Deletion is not null)
        {
            problems.Add(
                "--deletion names the fate of user data and --access shared holds none: every authenticated "
                + "caller reaches the same rows, which no account owns. Leave --deletion out.");
        }
    }

    private static void CheckDeletion(string? deletion, List<string> problems)
    {
        if (deletion is null)
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

    private static void CheckChoices(string model, string concurrency, List<string> problems)
    {
        if (model is not ("record" or "aggregate"))
        {
            problems.Add($"--model '{model}' is not a choice. It is record or aggregate.");
        }

        if (concurrency is not ("none" or "etag"))
        {
            problems.Add($"--concurrency '{concurrency}' is not a choice. It is none or etag.");
        }
    }

    private static string? Reaction(string root, string prefix, bool hasApi, List<string> problems)
    {
        string policyPort = System.IO.Path.Combine(
            root, "Src", "Application", FolderOf(prefix, "Application.Core"), "Common", "Ports", "IDeletedAccountDataPolicy.cs");

        if (File.Exists(policyPort))
        {
            return null;
        }

        string[] identityHosts = hasApi ? [FolderOf(prefix, "Api")] : [.. PresentationHosts(root, prefix)];
        string? reaction = identityHosts.Select(host => IdentityFeature(root, host)).FirstOrDefault(found => found is not null);

        if (reaction is null)
        {
            problems.Add(
                "This tree ships none of the account-deleted reaction, and it has neither the local accounts "
                + "(Features/Auth) nor an external-identity mapping (Features/ExternalIdentity) to read whether "
                + "an id still names an account, so the reaction's precondition has no authority to re-derive "
                + "from. Scaffold the feature into a tree this template generated, or write an "
                + "IUserAccountDirectory adapter by hand first.");
        }

        return reaction;
    }

    private static string? IdentityFeature(string root, string host)
    {
        string features = Full(root, $"{Presentation}/{host}/Features");

        return Array.Find(["Auth", "ExternalIdentity"], adapter => Directory.Exists(System.IO.Path.Combine(features, adapter)));
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

        problems.AddRange(new[] { features, type }
            .Distinct(StringComparer.Ordinal)
            .Where(name => _reservedNames.Contains(name, StringComparer.Ordinal))
            .Select(name =>
                $"'{name}' is already a segment of every namespace in this tree "
                + $"({string.Join(", ", _reservedNames.Take(6))}, ...). Choose a name that is not one."));

        if (CSharpNames.Keywords.Contains(CSharpNames.Lower(type), StringComparer.Ordinal))
        {
            problems.Add(
                $"'{type}' becomes the C# keyword '{CSharpNames.Lower(type)}' once its first letter is lowered, which is "
                + "how the generated parameters are named. Choose another name.");
        }

        return problems;
    }

    internal static List<string> Conflicts(string root, FeatureSpec spec, List<Emission> plan)
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
}
