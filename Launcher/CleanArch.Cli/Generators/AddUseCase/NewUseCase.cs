using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.AddUseCase;

internal static class NewUseCase
{
    internal const string Usage =
        "Usage: dotnet cleanarch add use-case <Features> <UseCase> --kind command|query\n"
        + "           [--input id|none|\"[id, ]Name:Type, Other:Type\"]\n"
        + "           [--dry-run]\n"
        + "  <Features>  the folder of an existing feature of the rich shape, e.g. Invoices\n"
        + "  <UseCase>   the operation, PascalCase, a verb and its subject, e.g. ApproveInvoice\n"
        + "  --kind      required. command: changes state and answers a Result.\n"
        + "              query: reads, and answers a Result of a DTO named for it, carrying the\n"
        + "              aggregate's id when its input does.\n"
        + "  --input     what the message carries, default id. id: the id of the feature's aggregate,\n"
        + "              which the route carries; it comes first when inputs follow it.\n"
        + "              Name:Type: an input, PascalCase, of any C# type, written as given, e.g.\n"
        + "              \"Name:string, Tags:IReadOnlyList<string>\". none: a message without input.\n"
        + "              An input of a known type (string, int, long, decimal, bool, Guid,\n"
        + "              DateTimeOffset, DateOnly, or one of them nullable) gets its rule and its test\n"
        + "              value; the output lists any other, whose rule and value are yours to write.\n"
        + "              The validator and its tests, and the handler's test, come with a rule.\n"
        + "  --dry-run   print the files and the edit, and write and run nothing.\n"
        + "  Where the API serves the feature, it also adds the action to the feature's controller, writes\n"
        + "  the request and response contracts and the route's row, every edit or none, then rewrites the\n"
        + "  OpenAPI reference and the clients written from it.";

    private static readonly string[] _reservedNames =
    [
        "Access", "Api", "Application", "Auth", "Commands", "Common", "Contracts", "Controllers", "Core",
        "Dtos", "Domain", "Entities", "Errors", "Features", "Infrastructure", "Model", "Models",
        "Persistence", "Policies", "Ports", "Presentation", "Queries", "Rows", "Services", "UseCases",
    ];

    private static readonly string[] _reservedSuffixes = ["UseCase", "Command", "Query", "Handler", "Validator", "Dto"];

    internal static int Run(AddUseCaseRequest request, GenerationContext context)
    {
        var problems = new List<string>();
        var spec = Check(request, context.Root, context.Product, problems);

        return spec is null
            ? Refusal.Write(context.Error, problems, Usage)
            : new UseCaseGeneration(spec with { Routing = UseCaseHttp.ReadRouting(context.Root, spec) }, context).Generate();
    }

    private static UseCaseSpec? Check(AddUseCaseRequest request, string root, string product, List<string> problems)
    {
        problems.AddRange(NameProblems(request.Features, request.Name));

        if (request.Kind is not ("command" or "query"))
        {
            problems.Add($"--kind '{request.Kind}' is not a choice. It is command or query.");
        }

        var (hasId, inputs) = UseCaseInputs.ReadInputs(request.Input ?? "id", problems);

        return problems.Count > 0 ? null : CheckTree(request, hasId, inputs, root, product, problems);
    }

    private static UseCaseSpec? CheckTree(AddUseCaseRequest request, bool hasId, List<Parameter> inputs, string root, string prefix, List<string> problems)
    {
        string features = request.Features;
        string application = Full(root, $"{SourceProject(prefix, "Application")}/Features/{features}");
        string domain = Full(root, $"{SourceProject(prefix, "Domain")}/Features/{features}");

        if (!FeatureExists(root, prefix, features, problems) || !IsRich(domain, features, problems) || IsWritten(application, features, request.Name, problems))
        {
            return null;
        }

        string? aggregate = hasId ? IdAggregate(domain, request, problems) : null;

        if ((hasId && aggregate is null) || Clashes(request, aggregate, inputs, problems))
        {
            return null;
        }

        var (dto, dtoExists) = Dto(request, application);
        var manifest = FeatureManifest.Read(root, features, out string? unread);

        if (unread is not null)
        {
            problems.Add($"'{FeatureManifest.PathOf(features)}' {unread}, so the files written could not be recorded in it. Repair or delete it first.");

            return null;
        }

        return new UseCaseSpec(prefix, features, request.Name, request.Kind, aggregate, inputs, dto, dtoExists, manifest?.Path);
    }

    private static bool FeatureExists(string root, string prefix, string features, List<string> problems)
    {
        string applicationFeatures = Full(root, $"{SourceProject(prefix, "Application")}/Features");

        if (Directory.Exists(Path.Combine(applicationFeatures, features)))
        {
            return true;
        }

        string[] known = Directory.Exists(applicationFeatures)
            ? [.. Directory.EnumerateDirectories(applicationFeatures).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];

        problems.Add(
            $"No feature called {features}: '{SourceProject(prefix, "Application")}/Features/{features}' does not exist. "
            + (known.Length == 0
                ? "This tree has no feature yet; write one first, as docs/guides/adding-a-feature.md says."
                : $"The features here are {string.Join(", ", known)}."));

        return false;
    }

    private static bool IsRich(string domain, string features, List<string> problems)
    {
        if (Directory.Exists(domain))
        {
            return true;
        }

        problems.Add(
            $"{features} has no domain folder, so it is a service over a store: its operations are methods of its "
            + "service, and a use case of its own would make it neither shape. Add a method to the service, or "
            + "promote the feature to an aggregate first (docs/architecture/feature-shapes.md, From simple to rich).");

        return false;
    }

    private static bool IsWritten(string application, string features, string name, List<string> problems)
    {
        string? folder = Array.Find(["Commands", "Queries"], folder => Directory.Exists(Path.Combine(application, "UseCases", folder, name)));

        if (folder is null)
        {
            return false;
        }

        problems.Add(
            $"{features} already has a use case called {name}, under UseCases/{folder}/{name}. A use case is "
            + "written once; after that it is edited.");

        return true;
    }

    private static string? IdAggregate(string domain, AddUseCaseRequest request, List<string> problems)
    {
        string[] found = DomainFolders.Aggregates(domain);

        if (found.Length != 1)
        {
            problems.Add(
                found.Length == 0
                    ? $"id in --input carries the id of the feature's aggregate, and {request.Features} declares no repository "
                        + "under Repositories/ to name it. Leave id out, or pass the id as an input of its own, e.g. "
                        + "--input \"InvoiceId:Guid\"."
                    : $"id in --input carries the id of the feature's aggregate, and {request.Features} declares several "
                        + $"({string.Join(", ", found)}), so which one is ambiguous. Leave id out, or pass the id as an "
                        + "input of its own, e.g. --input \"InvoiceId:Guid\".");

            return null;
        }

        if (string.Equals(request.Name, found[0], StringComparison.Ordinal))
        {
            problems.Add(
                $"'{request.Name}' is the aggregate's own name, so the use case's namespace would hide the type. Name the "
                + $"operation: a verb and its subject, e.g. Approve{found[0]}.");

            return null;
        }

        return found[0];
    }

    private static bool Clashes(AddUseCaseRequest request, string? aggregate, List<Parameter> inputs, List<string> problems)
    {
        bool isCommand = request.Kind == "command";
        string[] taken =
        [
            .. aggregate is null ? [] : new[] { aggregate + "Id" },
            .. aggregate is not null && isCommand ? new[] { "Precondition" } : [],
            request.Name + (isCommand ? "Command" : "Query"),
            request.Name + "Request",
        ];

        if (inputs.FirstOrDefault(input => taken.Contains(input.Name, StringComparer.Ordinal)) is not { } clash)
        {
            return false;
        }

        problems.Add(
            $"The input {clash.Name} would clash with a name the generated types already hold ({string.Join(", ", taken)}). "
            + "Name it otherwise.");

        return true;
    }

    private static (string? Dto, bool Exists) Dto(AddUseCaseRequest request, string application)
    {
        if (request.Kind != "query")
        {
            return (null, false);
        }

        string name = request.Name;
        string dto = (name.StartsWith("Get", StringComparison.Ordinal) && name.Length > 3 && char.IsUpper(name[3]) ? name[3..] : name) + "Dto";

        return (dto, File.Exists(Path.Combine(application, "Model", "Dtos", dto + ".cs")));
    }

    private static List<string> NameProblems(string features, string name)
    {
        var problems = new List<string>();

        if (!CSharpNames.PascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a feature folder: <Features> is PascalCase and plural, e.g. Invoices.");
        }

        if (!CSharpNames.PascalCase.IsMatch(name))
        {
            problems.Add($"'{name}' is not a use case name: <UseCase> is PascalCase, e.g. ApproveInvoice.");
        }

        if (problems.Count > 0)
        {
            return problems;
        }

        if (string.Equals(features, name, StringComparison.Ordinal))
        {
            problems.Add($"<Features> and <UseCase> are both '{name}'. Name the operation: a verb and its subject, e.g. ApproveInvoice.");
        }

        if (name.Length > 60)
        {
            problems.Add("A use case name longer than 60 characters makes a path that some tools cannot open. Shorten it.");
        }

        if (_reservedNames.Contains(name, StringComparer.Ordinal))
        {
            problems.Add($"'{name}' is already a segment of the namespaces around a use case. Name the operation: a verb and its subject.");
        }

        if (_reservedSuffixes.FirstOrDefault(suffix => name.EndsWith(suffix, StringComparison.Ordinal)) is { } suffix)
        {
            problems.Add($"'{name}' ends in '{suffix}', which the generated types append themselves. Name the operation alone, e.g. {name[..^suffix.Length]}.");
        }

        if (CSharpNames.Keywords.Contains(CSharpNames.Lower(name), StringComparer.Ordinal))
        {
            problems.Add($"'{name}' becomes the C# keyword '{CSharpNames.Lower(name)}' once its first letter is lowered, which is how the controller names it. Choose another name.");
        }

        return problems;
    }
}
