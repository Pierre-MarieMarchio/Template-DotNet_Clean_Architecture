#:property UseAppHost=false

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class NewFeature
{
    private const string ManifestFolder = ".feature-manifest";

    private const string PageTemplate = "docs/_templates/feature.md";

    private const string Usage =
        "Usage: dotnet run Tools/NewFeature.cs <Features> <Feature> --access owner|shared\n"
        + "           [--deletion delete|anonymise]\n"
        + "           [--operations get,list,create,update,delete]\n"
        + "           [--model record|aggregate]\n"
        + "           [--concurrency none|etag]\n"
        + "           [--root <path>]\n"
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
        + "  --root         the tree to write into, default the current directory.\n"
        + "       dotnet run Tools/NewFeature.cs --self-test";

    private static readonly string[] OperationNames = ["get", "list", "create", "update", "delete"];

    private static readonly string[] OptionNames =
        ["--access", "--deletion", "--operations", "--model", "--concurrency", "--root"];

    private static readonly string[] ReservedNames =
    [
        "Access", "Api", "Application", "Auth", "Common", "Contracts", "Controllers", "Core", "Dtos",
        "Domain", "Entities", "Errors", "Features", "Infrastructure", "Model", "Models", "Persistence",
        "Policies", "Ports", "Presentation", "Rows", "Services", "UseCases",
    ];

    private static readonly string[] Keywords =
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

    private static readonly Regex PascalCase = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private sealed record Emission(string Path, string Content);

    private sealed record Request(
        string? Features,
        string? Type,
        string? Access,
        string? Deletion,
        string? Operations,
        string? Model,
        string? Concurrency,
        string Root);

    private sealed record Spec(
        string Prefix,
        string Features,
        string Type,
        string Access,
        string Deletion,
        string[] Operations,
        string Model,
        string Concurrency,
        string? Reaction)
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
            Console.WriteLine(Usage);

            return 0;
        }

        /*#if (!publicApi)*/
        return Run(args, Console.Out, Console.Error);
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

        return Run(args, Console.Out, Console.Error);

        static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
        #endif*/
    }

    private static int Run(string[] args, TextWriter output, TextWriter error)
    {
        var problems = new List<string>();
        var request = Parse(args, problems);

        string root = Path.GetFullPath(request.Root);
        var spec = problems.Count == 0 ? Check(request, root, problems) : null;

        if (spec is null)
        {
            return Refuse(error, problems);
        }

        var plan = Plan(spec, root, out var skipped);

        problems.AddRange(Conflicts(root, spec, plan));

        if (problems.Count > 0)
        {
            return Refuse(error, problems);
        }

        var page = PageOf(root, spec, plan, out string? unwritten);
        var manifest = ManifestOf(spec, plan, page);

        try
        {
            WriteAll(root, [.. plan, .. page is null ? Array.Empty<Emission>() : [page], manifest.Emission]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"error: nothing was left behind, but the write failed: {exception.Message}");

            return 1;
        }

        Report(output, root, spec, plan, manifest.Emission.Path, skipped);
        ReportPage(output, spec, page, unwritten);

        return 0;
    }

    private static int Refuse(TextWriter error, List<string> problems)
    {
        foreach (string problem in problems)
        {
            error.WriteLine($"error: {problem}");
        }

        error.WriteLine();
        error.WriteLine(Usage);
        error.WriteLine();
        error.WriteLine("Nothing was written.");

        return 1;
    }

    private static Request Parse(string[] args, List<string> problems)
    {
        var positional = new List<string>();
        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            if (!argument.StartsWith('-'))
            {
                positional.Add(argument);

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

            if (!OptionNames.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"'{name}' is not an option. The options are {string.Join(", ", OptionNames[..^1])}.");

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
            Option("--root") ?? ".");
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

        try
        {
            prefix = ProductPrefix(root);
        }
        catch (InvalidOperationException exception)
        {
            problems.Add(exception.Message);

            return null;
        }

        string? reaction = null;

        if (access == "owner")
        {
            string policyPort = System.IO.Path.Combine(
                root, "Src", "Application", $"{prefix}.Application.Core", "Common", "Ports", "IDeletedAccountDataPolicy.cs");

            if (!File.Exists(policyPort))
            {
                string apiFeatures = System.IO.Path.Combine(
                    root, "Src", "Presentation", $"{prefix}.Api", "Features");

                if (Directory.Exists(System.IO.Path.Combine(apiFeatures, "Auth")))
                {
                    reaction = "Auth";
                }
                else if (Directory.Exists(System.IO.Path.Combine(apiFeatures, "ExternalIdentity")))
                {
                    reaction = "ExternalIdentity";
                }
                else
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

        return new Spec(prefix, features, type, access, deletion, operations, model, concurrency, reaction);
    }

    private static string[]? ParseOperations(string? text, List<string> problems)
    {
        if (text is null)
        {
            return OperationNames;
        }

        string[] named = [.. text.Split(',', StringSplitOptions.TrimEntries)];
        bool valid = true;

        if (named.Any(name => name.Length == 0))
        {
            problems.Add("--operations has an empty entry. It is a comma-separated list: get,list,create,update,delete.");
            valid = false;
        }

        foreach (string name in named.Where(name => name.Length > 0 && !OperationNames.Contains(name, StringComparer.Ordinal)))
        {
            problems.Add($"--operations '{name}' is not an operation. They are {string.Join(", ", OperationNames)}.");
            valid = false;
        }

        foreach (string name in named.GroupBy(name => name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            problems.Add($"--operations names '{name}' twice.");
            valid = false;
        }

        return valid ? [.. OperationNames.Where(name => named.Contains(name, StringComparer.Ordinal))] : null;
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

        if (!PascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a folder name: <Features> is PascalCase and plural, e.g. Widgets.");
        }

        if (!PascalCase.IsMatch(type))
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
            if (ReservedNames.Contains(name, StringComparer.Ordinal))
            {
                problems.Add(
                    $"'{name}' is already a segment of every namespace in this tree "
                    + $"({string.Join(", ", ReservedNames.Take(6))}, ...). Choose a name that is not one.");
            }
        }

        if (Keywords.Contains(Lower(type), StringComparer.Ordinal))
        {
            problems.Add(
                $"'{type}' becomes the C# keyword '{Lower(type)}' once its first letter is lowered, which is "
                + "how the generated parameters are named. Choose another name.");
        }

        return problems;
    }

    private static string ProductPrefix(string root)
    {
        string domain = System.IO.Path.Combine(root, "Src", "Domain");

        string[] found = Directory.Exists(domain)
            ? [.. Directory.EnumerateDirectories(domain)
                .Select(directory => new DirectoryInfo(directory).Name)
                .Where(name => name.EndsWith(".Domain", StringComparison.Ordinal))]
            : [];

        return found.Length switch
        {
            1 => found[0][..^".Domain".Length],
            0 => throw new InvalidOperationException(
                $"No '<Prefix>.Domain' project under '{domain}'. Run this from a tree generated by "
                + "this template, or name its root with --root."),
            _ => throw new InvalidOperationException(
                $"More than one '<Prefix>.Domain' project under '{domain}' ({string.Join(", ", found)}), "
                + "so the product prefix is ambiguous."),
        };
    }

    private static string Lower(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static bool IsVowel(char letter) => "aeiou".Contains(char.ToLowerInvariant(letter), StringComparison.Ordinal);

    private static string Kebab(string pascal) => Separated(pascal, '-');

    private static string Snake(string pascal) => Separated(pascal, '_');

    private static string Separated(string pascal, char separator) =>
        string.Concat(pascal.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? separator + char.ToLowerInvariant(character).ToString()
                : char.ToLowerInvariant(character).ToString()));

    private static string ProjectOf(Spec spec, string layer, string project) =>
        $"{layer}/{spec.Prefix}.{project}";

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
        string apiTests = ProjectOf(spec, "Tests/Presentation", "Api.UnitTests");

        string feature = "Features/%F%";

        if (spec.Reaction is { } adapter)
        {
            Add(applicationCore, "Common/Ports/IDeletedAccountDataPolicy.cs", Templates.DeletedAccountDataPolicy);
            Add(applicationCore, "Common/Ports/IUserAccountDirectory.cs", Templates.UserAccountDirectory);
            Add(applicationCore, "Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/ISweepDeletedAccountDataUseCase.cs", Templates.SweepUseCasePort);
            Add(applicationCore, "Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountDataUseCase.cs", Templates.SweepUseCase);
            Add(api, "Features/BackgroundJobs/Jobs/DeletedAccountSweepJobsExtensions.cs", Templates.DeletedAccountSweepJobs);
            Add(api, "Features/BackgroundJobs/Jobs/DeletedAccountSweepOptions.cs", Templates.DeletedAccountSweepOptions);

            if (adapter == "Auth")
            {
                Add(api, "Features/Auth/Composition/AccountUserAccountDirectory.cs", Templates.LocalUserAccountDirectory);
            }
            else
            {
                Add(api, "Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory.cs", Templates.ExternalUserAccountDirectory);
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
        Add(application, $"{feature}/Model/Policies/%A%InputPolicy.cs", Templates.InputPolicy, "rec");
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

        Add(application, $"{queries}/Get%A%/Get%A%Query.cs", Templates.GetQuery, "agg&get");
        Add(application, $"{queries}/Get%A%/Get%A%QueryValidator.cs", Templates.GetQueryValidator, "agg&get");
        Add(application, $"{queries}/Get%A%/IGet%A%UseCase.cs", Templates.GetUseCasePort, "agg&get");
        Add(application, $"{queries}/Get%A%/Get%A%UseCase.cs", Templates.GetUseCase, "agg&get");
        Add(application, $"{queries}/Get%F%/Get%F%Query.cs", Templates.ListQuery, "agg&list");
        Add(application, $"{queries}/Get%F%/IGet%F%UseCase.cs", Templates.ListUseCasePort, "agg&list");
        Add(application, $"{queries}/Get%F%/Get%F%UseCase.cs", Templates.ListUseCase, "agg&list");
        Add(application, $"{commands}/Create%A%/Create%A%Command.cs", Templates.CreateCommand, "agg&create");
        Add(application, $"{commands}/Create%A%/Create%A%CommandValidator.cs", Templates.CreateCommandValidator, "agg&create");
        Add(application, $"{commands}/Create%A%/ICreate%A%UseCase.cs", Templates.CreateUseCasePort, "agg&create");
        Add(application, $"{commands}/Create%A%/Create%A%UseCase.cs", Templates.CreateUseCase, "agg&create");
        Add(application, $"{commands}/Update%A%/Update%A%Command.cs", Templates.UpdateCommand, "agg&update");
        Add(application, $"{commands}/Update%A%/Update%A%CommandValidator.cs", Templates.UpdateCommandValidator, "agg&update");
        Add(application, $"{commands}/Update%A%/IUpdate%A%UseCase.cs", Templates.UpdateUseCasePort, "agg&update");
        Add(application, $"{commands}/Update%A%/Update%A%UseCase.cs", Templates.UpdateUseCase, "agg&update");
        Add(application, $"{commands}/Delete%A%/Delete%A%Command.cs", Templates.DeleteCommand, "agg&delete");
        Add(application, $"{commands}/Delete%A%/Delete%A%CommandValidator.cs", Templates.DeleteCommandValidator, "agg&delete");
        Add(application, $"{commands}/Delete%A%/IDelete%A%UseCase.cs", Templates.DeleteUseCasePort, "agg&delete");
        Add(application, $"{commands}/Delete%A%/Delete%A%UseCase.cs", Templates.DeleteUseCase, "agg&delete");

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
        Add(persistence, $"{feature}/Access/Queries/%A%SortMap.cs", Templates.SortMap, "list");

        Add(api, $"{feature}/Contracts/Requests/Create%A%Request.cs", Templates.CreateRequest, "create");
        Add(api, $"{feature}/Contracts/Requests/Update%A%Request.cs", Templates.UpdateRequest, "update");
        Add(api, $"{feature}/Contracts/Requests/Get%F%Request.cs", Templates.ListRequest, "list");
        Add(api, $"{feature}/Contracts/Responses/%A%Response.cs", Templates.Response, "dto");
        Add(api, $"{feature}/Contracts/Responses/%A%SummaryResponse.cs", Templates.SummaryResponse, "list");
        Add(api, $"{feature}/Controllers/%F%Controller.cs", Templates.Controller);
        Add(api, $"{feature}/Contracts/Mapping/%A%ResponseMapping.cs", Templates.ResponseMapping, "dto|list");

        AddTest(applicationTests, $"{feature}/Model/Errors/%A%ErrorsTests.cs", Templates.ErrorsTests, "notfound");
        AddTest(applicationTests, $"{feature}/Model/Dtos/%A%ContentDtoValidatorTests.cs", Templates.ContentValidatorTests, "rec&content");
        AddTest(applicationTests, $"{feature}/Services/%A%ServiceTests.cs", Templates.ServiceTests, "rec");
        AddTest(
            applicationTests,
            $"{feature}/UseCases/Commands/Create%A%/Create%A%CommandValidatorTests.cs",
            Templates.CommandValidatorTests,
            "agg&create",
            new() { ["Op"] = "Create", ["CommandNew"] = "new Create%A%Command(name!, description!)" });
        AddTest(
            applicationTests,
            $"{feature}/UseCases/Commands/Update%A%/Update%A%CommandValidatorTests.cs",
            Templates.CommandValidatorTests,
            "agg&update",
            new() { ["Op"] = "Update", ["CommandNew"] = "new Update%A%Command(Guid.CreateVersion7(), name!, description!)" });
        AddTest(domainTests, $"{feature}/Model/Entities/%A%Tests.cs", Templates.AggregateTests, "agg");
        AddTest(persistenceTests, $"{feature}/Rows/Configurations/%A%RecordConfigurationTests.cs", Templates.ConfigurationTests);
        AddTest(persistenceTests, $"{feature}/%A%MapperFidelityTests.cs", Templates.MapperFidelityTests, "agg");
        AddTest(apiTests, $"{feature}/Contracts/Mapping/%A%ResponseMappingTests.cs", Templates.ResponseMappingTests, "dto");

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

        return flags;
    }

    private static Dictionary<string, string> Tokens(Spec spec)
    {
        bool record = spec.Model == "record";
        bool owner = spec.Access == "owner";
        bool etag = spec.Concurrency == "etag";

        string a = spec.Type;
        string f = spec.Features;
        string l = Lower(a);

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
            .. Flags(spec).Contains("clock") ? ["new StubDateTimeProvider()"] : Array.Empty<string>(),
            .. Flags(spec).Contains("content") ? [$"new {a}ContentDtoValidator()"] : Array.Empty<string>(),
            "currentUser",
        ];

        tokens["ServiceArgs"] = string.Join(", ", serviceArguments);

        string[] fields = [$"get{a}", $"get{f}", $"create{a}", $"update{a}", $"delete{a}"];

        string Use(string operation) => operation switch
        {
            "get" => $"IGet{a}UseCase get{a}",
            "list" => $"IGet{f}UseCase get{f}",
            "create" => $"ICreate{a}UseCase create{a}",
            "update" => $"IUpdate{a}UseCase update{a}",
            _ => $"IDelete{a}UseCase delete{a}",
        };

        tokens["CtorParams"] = record
            ? $"I{a}Service service"
            : string.Join(",\n    ", spec.Operations.Select(Use));

        string pre = etag ? ", precondition" : string.Empty;
        string content = $"request.Name, request.Description";

        tokens["CallList"] = record
            ? "service.ListAsync(query, cancellationToken)"
            : $"{fields[1]}.ExecuteAsync(query, cancellationToken)";
        tokens["CallGet"] = record
            ? $"service.GetAsync({l}Id, cancellationToken)"
            : $"{fields[0]}.ExecuteAsync(new Get{a}Query({l}Id), cancellationToken)";
        tokens["CallCreate"] = record
            ? $"service.CreateAsync(new {a}ContentDto({content}), cancellationToken)"
            : $"{fields[2]}.ExecuteAsync(new Create{a}Command({content}), cancellationToken)";
        tokens["CallUpdate"] = record
            ? $"service.UpdateAsync({l}Id, new {a}ContentDto({content}){pre}, cancellationToken)"
            : $"{fields[3]}.ExecuteAsync(new Update{a}Command({l}Id, {content}{pre}), cancellationToken)";
        tokens["CallDelete"] = record
            ? $"service.DeleteAsync({l}Id{pre}, cancellationToken)"
            : $"{fields[4]}.ExecuteAsync(new Delete{a}Command({l}Id{pre}), cancellationToken)";

        return tokens;
    }

    private static List<string> Conflicts(string root, Spec spec, List<Emission> plan)
    {
        var problems = new List<string>();

        string persistence = ProjectOf(spec, "Src/Infrastructure", "Infrastructure.Persistence");

        string[] projects =
        [
            ProjectOf(spec, "Src/Application", "Application"),
            persistence,
            ProjectOf(spec, "Src/Presentation", "Api"),
            .. spec.Model == "aggregate" ? [ProjectOf(spec, "Src/Domain", "Domain")] : Array.Empty<string>(),
        ];

        foreach (string project in projects.Where(project => !Directory.Exists(System.IO.Path.Combine(root, project))))
        {
            problems.Add($"'{project}' does not exist under '{root}', so this is not a tree of this template.");
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

    private static string ManifestPath(Spec spec) => $"{ManifestFolder}/{spec.Features}.json";

    private static string PagePath(string features) => $"docs/features/{Kebab(features)}.md";

    private static Emission? PageOf(string root, Spec spec, List<Emission> plan, out string? unwritten)
    {
        string path = PagePath(spec.Features);
        string template = System.IO.Path.Combine(root, PageTemplate);

        if (File.Exists(System.IO.Path.Combine(root, path)))
        {
            unwritten = $"'{path}' already exists, so it was left as it is and the manifest does not record it: remove-feature never deletes it.";

            return null;
        }

        if (!File.Exists(template))
        {
            unwritten = $"'{PageTemplate}' is not in this tree, so '{path}' was not written; write it by hand.";

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
        string item = $"{route}/{{{Lower(spec.Type)}Id}}";

        string access = spec.Access == "owner"
            ? $"each authenticated caller, on their own {plural} only; once an account is deleted, its {plural} are "
                + (spec.Deletion == "anonymise" ? "anonymised" : "deleted")
            : $"every authenticated caller, on the same {plural}";

        string shape = (spec.Model == "record"
                ? "a service over a store, with no domain model"
                : $"one use case per operation over the {singular} aggregate")
            + (spec.Concurrency == "etag"
                ? ", and a write sends If-Match with the ETag a read returned"
                : ", and the last write wins");

        (string Operation, string Line)[] endpoints =
        [
            ("list", $"- `GET {route}`: a page of {plural}"),
            ("get", $"- `GET {item}`: {one}, and `HEAD` for its headers alone"),
            ("create", $"- `POST {route}`: creates {one}"),
            ("update", $"- `PUT {item}`: replaces {one}"),
            ("delete", $"- `DELETE {item}`: deletes {one}"),
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

    private static byte[] Bytes(Emission emission)
    {
        bool bom = emission.Path.EndsWith(".cs", StringComparison.Ordinal) || emission.Path.EndsWith(".md", StringComparison.Ordinal);
        string text = emission.Content.ReplaceLineEndings("\n");

        return bom
            ? [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]
            : Encoding.UTF8.GetBytes(text);
    }

    private static string HashOf(Emission emission) =>
        Convert.ToHexStringLower(SHA256.HashData(Bytes(emission)));

    private static (Emission Emission, int Files) ManifestOf(Spec spec, List<Emission> plan, Emission? page)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
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

            writer.WriteEndObject();
        }

        string json = Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n";

        return (new Emission(ManifestPath(spec), json), plan.Count);
    }

    private static void WriteAll(string root, List<Emission> emissions)
    {
        var files = new List<string>();
        var directories = new List<string>();

        try
        {
            foreach (var emission in emissions)
            {
                string full = System.IO.Path.Combine(root, emission.Path);
                string directory = System.IO.Path.GetDirectoryName(full)!;

                var missing = new Stack<string>();

                for (string next = directory; !Directory.Exists(next); next = System.IO.Path.GetDirectoryName(next)!)
                {
                    missing.Push(next);
                }

                directories.AddRange(missing);
                Directory.CreateDirectory(directory);

                using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write);
                byte[] bytes = Bytes(emission);

                stream.Write(bytes);
                files.Add(full);
            }
        }
        catch
        {
            foreach (string file in files)
            {
                File.Delete(file);
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

    private static void Report(TextWriter output, string root, Spec spec, List<Emission> plan, string manifest, List<string> skipped)
    {
        string p = spec.Prefix;
        string f = spec.Features;
        string a = spec.Type;
        bool record = spec.Model == "record";
        bool owner = spec.Access == "owner";
        string verb = spec.Deletion == "anonymise" ? "Anonymise" : "Delete";
        var flags = Flags(spec);

        output.WriteLine(
            $"Wrote {plan.Count} files for {f}/{a} "
            + $"(access {spec.Access}, model {spec.Model}, operations {string.Join(',', spec.Operations)}, "
            + $"concurrency {spec.Concurrency}"
            + (owner ? $", deletion {spec.Deletion}" : string.Empty)
            + "):");

        foreach (var emission in plan)
        {
            output.WriteLine($"  {emission.Path}");
        }

        output.WriteLine();
        output.WriteLine($"Manifest: {manifest} (the options, and the SHA-256 of each file above).");

        foreach (string project in skipped)
        {
            output.WriteLine($"Not written: '{project}' is not in this tree, so its tests were skipped.");
        }

        output.WriteLine();
        output.WriteLine("The solution does not compile until edit 1 is made, and the feature does not compose until all are; nothing here edited a file it does not own.");
        output.WriteLine(spec.Reaction is null
            ? "Six edits, each by hand, each somewhere an absence should be visible:"
            : "Eleven edits, each by hand: six like every feature, and five more because this project was");
        output.WriteLine(spec.Reaction is null
            ? string.Empty
            : "generated with no feature that owns user data, so its account-deleted reaction ships with this one.");

        if (spec.Reaction is not null)
        {
            output.WriteLine(RemovalRefusal);
        }

        output.WriteLine();
        output.WriteLine($"  1. Src/Infrastructure/{p}.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs");
        output.WriteLine($"       public const string {f}Schema = \"{Snake(f)}\";");
        output.WriteLine($"       internal DbSet<{a}Record> {f} => Set<{a}Record>();");
        output.WriteLine($"       modelBuilder.ApplyConfiguration(new {a}RecordConfiguration());");
        output.WriteLine($"     with the usings of {p}.Infrastructure.Persistence.Features.{f}.Rows.Configurations and .Rows.Models");
        output.WriteLine();
        output.WriteLine($"  2. Src/Application/{p}.Application/ApplicationModule.cs: an Add{f}() beside the others");

        if (record || flags.Contains("change"))
        {
            output.WriteLine($"       services.AddFeature(\"{f}\");   // its validators and use cases, found by folder");
            output.WriteLine($"       services.AddScoped<I{a}Service, {a}Service>();");
        }
        else
        {
            output.WriteLine($"       services.AddFeature(\"{f}\");   // its validators and use cases, found by folder");
        }

        if (owner)
        {
            output.WriteLine($"       services.AddDomainEventConsumer<UserAccountDeleted, {verb}{a}OnAccountDeletedConsumer>();");
            output.WriteLine($"       services.AddScoped<IDeletedAccountDataPolicy, {verb}{a}OnAccountDeletedConsumer>();");
            output.WriteLine("     // the declared fate of this feature's data once the account that owned it is gone, applied");
            output.WriteLine("     // by the consumer when the fact arrives and by the sweep when it never does");
        }

        output.WriteLine();
        output.WriteLine($"  3. Src/Infrastructure/{p}.Infrastructure.Persistence/PersistenceModule.cs: an Add{f}Feature(services),");
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
        output.WriteLine($"  4. Src/Presentation/{p}.Api/Program.cs: builder.Services.Add{f}(); beside the other features,");
        output.WriteLine($"     with using {p}.Application; if the file has none yet");
        output.WriteLine();
        output.WriteLine($"  5. Tests/Architecture/{p}.Architecture.Tests/Composition/HostComposition.cs: services.Add{f}(); in each");
        output.WriteLine("     composition that offers the feature, so the container tests resolve what it registers,");
        output.WriteLine($"     with using {p}.Application; if the file has none yet");
        output.WriteLine();

        string[] ports =
        [
            .. record && flags.Contains("stage") ? [$"I{a}Store"] : Array.Empty<string>(),
            .. !record ? [$"I{a}Repository"] : Array.Empty<string>(),
            .. flags.Contains("rq") ? [$"I{a}Queries"] : Array.Empty<string>(),
        ];

        string moduleTests = $"Tests/Application/{p}.Application.UnitTests/ApplicationModuleTests.cs";

        if (File.Exists(Path.Combine(root, moduleTests)))
        {
            output.WriteLine($"  6. {moduleTests}: nameof(ApplicationModule.Add{f}) in");
            output.WriteLine($"     FeatureCalls, services.Add{f}(); in ComposeEveryFeature, and in BuildProvider a double for each port");
            output.WriteLine($"     the application layer declares ({string.Join(", ", ports)}), so the container builds");

            if (!record)
            {
                output.WriteLine($"     and _knownUseCaseCount raised by {spec.Operations.Length}, the use cases this feature declares");
            }
        }
        else
        {
            output.WriteLine($"  6. Nothing to edit: {moduleTests} is only generated with a feature,");
            output.WriteLine("     and this project had none. Step 5 is what makes the container tests resolve the feature.");
        }

        if (owner && spec.Reaction is { } adapter)
        {
            string directory = adapter == "Auth" ? "AccountUserAccountDirectory" : "ExternalIdentityUserAccountDirectory";
            string composition = adapter == "Auth"
                ? $"Src/Presentation/{p}.Api/Features/Auth/Composition/AuthComposition.cs"
                : $"Src/Presentation/{p}.Api/Features/ExternalIdentity/Composition/ExternalAuthenticationComposition.cs";

            output.WriteLine();
            output.WriteLine($"  7. Src/Application/{p}.Application.Core/ApplicationCoreModule.cs: the AddDeletedAccountSweep()");
            output.WriteLine("     method beside AddPurgeExpiredIdempotencyKeys, with using");
            output.WriteLine($"     {p}.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;");
            output.WriteLine();
            output.WriteLine($"  8. Src/Presentation/{p}.Api/Program.cs: builder.Services.AddDeletedAccountSweep(); beside");
            output.WriteLine("     builder.Services.AddPurgeExpiredIdempotencyKeys();");
            output.WriteLine();
            output.WriteLine($"  9. Src/Presentation/{p}.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs:");
            output.WriteLine("     services.AddDeletedAccountSweepJobs(configuration); after services.AddMaintenanceJobs(configuration);");
            output.WriteLine();
            output.WriteLine($" 10. {composition}: services.AddScoped<IUserAccountDirectory, {directory}>();");
            output.WriteLine("     with the using of Application.Core's ports; the directory shares the composition's namespace");
            output.WriteLine();
            output.WriteLine($" 11. Tests/Architecture/{p}.Architecture.Tests/Composition/HostComposition.cs: services.AddDeletedAccountSweep();");
            output.WriteLine("     after services.AddPurgeExpiredIdempotencyKeys(); in each composition, and in AddHostSuppliedAdapters");
            output.WriteLine("     and AddBackgroundOnlyAdapters the double that stands in for the directory the API composes:");
            output.WriteLine("       services.AddScoped<IUserAccountDirectory, ArchitectureTestUserAccountDirectory>();");
            output.WriteLine("     declared beside the other doubles, or the container tests cannot resolve the feature's consumer:");
            output.WriteLine("       internal sealed class ArchitectureTestUserAccountDirectory : IUserAccountDirectory");
            output.WriteLine("       {");
            output.WriteLine("           public Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default) =>");
            output.WriteLine("               Task.FromResult(true);");
            output.WriteLine("       }");
        }

        output.WriteLine();
        output.WriteLine($"When integration tests exercise the feature, AppDbContext.{f}Schema also joins the schemas their database");
        output.WriteLine("fixture truncates between tests (Tests/Integration, TestDatabase).");
        output.WriteLine();
        output.WriteLine("Then the schema, which this does not run. The model changed, so the business context needs a migration.");
        output.WriteLine("On a project no database has applied a migration to, the repository keeps one InitialCreate per context:");
        output.WriteLine($"delete the two InitialCreate files and AppDbContextModelSnapshot.cs under Src/Infrastructure/{p}.Infrastructure.Persistence/Migrations,");
        output.WriteLine("then run  dotnet run Tools/Tasks.cs migration-add InitialCreate");
        output.WriteLine($"Once a database has applied it, add a migration instead:  dotnet run Tools/Tasks.cs migration-add Add{f}");
        output.WriteLine();

        if (!record)
        {
            output.WriteLine("The aggregate raises no event. The first one it raises needs a consumer, or its name in");
            output.WriteLine("DomainEventTests._deliberatelyUnconsumed with the reason (docs/guides/adding-a-feature.md lists the closed lists).");
            output.WriteLine();
        }

        output.WriteLine("Not generated, because it is a decision rather than a shape: the rules the feature protects, any domain");
        output.WriteLine("event of its own, authorisation beyond default-deny, and the integration tests against PostgreSQL.");

        if (spec.Concurrency == "none")
        {
            output.WriteLine();
            output.WriteLine("--concurrency none: an update replaces what is stored whatever its state, so two concurrent writers");
            output.WriteLine("are last-write-wins, and the API publishes no ETag and reads no If-Match.");
        }
    }

    private static void ReportPage(TextWriter output, Spec spec, Emission? page, string? unwritten)
    {
        string path = PagePath(spec.Features);
        string title = Separated(spec.Features, ' ');

        output.WriteLine();

        if (page is null)
        {
            output.WriteLine($"Feature page: {unwritten}");
        }
        else
        {
            output.WriteLine($"Feature page: {path}, from {PageTemplate}. The manifest records it, so remove-feature deletes it, edited or not.");
            output.WriteLine("Each heading says what its section holds; replace those lines with what this feature does.");
        }

        output.WriteLine("Then its row under ## Features in docs/README.md:");
        output.WriteLine($"  | [{char.ToUpperInvariant(title[0]) + title[1..]}](features/{Kebab(spec.Features)}.md) | what a caller can do with it |");
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
            NoOverwriteCases(scratch, Expect);
            ManifestCases(scratch, Expect);
            RollbackCases(scratch, Expect);
            ReactionCases(scratch, Expect);
            PageCases(scratch, Expect);
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

    private static string NewTree(string scratch, bool reaction = true)
    {
        string root = System.IO.Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        string[] directories =
        [
            "Src/Domain/Acme.Domain",
            "Src/Application/Acme.Application",
            "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts",
            "Src/Presentation/Acme.Api",
            "Tests/Application/Acme.Application.UnitTests",
            "Tests/Domain/Acme.Domain.UnitTests",
            "Tests/Infrastructure/Acme.Infrastructure.Persistence.UnitTests",
            "Tests/Presentation/Acme.Api.UnitTests",
        ];

        foreach (string directory in directories)
        {
            Directory.CreateDirectory(System.IO.Path.Combine(root, directory));
        }

        File.WriteAllText(
            System.IO.Path.Combine(root, "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs"),
            "// the context a person edits\n");

        if (reaction)
        {
            string ports = System.IO.Path.Combine(root, "Src/Application/Acme.Application.Core/Common/Ports");

            Directory.CreateDirectory(ports);
            File.WriteAllText(
                System.IO.Path.Combine(ports, "IDeletedAccountDataPolicy.cs"),
                "// the reaction this tree already ships\n");
        }
        else
        {
            Directory.CreateDirectory(System.IO.Path.Combine(root, "Src/Presentation/Acme.Api/Features/Auth"));
        }

        return root;
    }

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = Run([.. args, "--root", root], output, error);

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

        expect(elsewhere.Code == 1 && elsewhere.Error.Contains("<Prefix>.Domain", StringComparison.Ordinal), "A tree without a domain project was not refused for it.");
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
            "Src/Application/Acme.Application/Features/TodoLists/Services/ITodoListService.cs",
            "Src/Application/Acme.Application/Features/TodoLists/Ports/TodoListStore/ITodoListStore.cs",
            "Src/Application/Acme.Application/Features/TodoLists/Ports/TodoListQueries/ITodoListQueries.cs",
            "Src/Infrastructure/Acme.Infrastructure.Persistence/Features/TodoLists/Rows/Models/TodoListRecord.cs",
            "Src/Presentation/Acme.Api/Features/TodoLists/Controllers/TodoListsController.cs",
            "Tests/Application/Acme.Application.UnitTests/Features/TodoLists/Services/TodoListServiceTests.cs",
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

        for (int mask = 1; mask < 1 << OperationNames.Length; mask++)
        {
            string[] operations = [.. OperationNames.Where((_, index) => (mask & (1 << index)) != 0)];

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

                expect(before.All(pair => after[pair.Key] == pair.Value), $"[{label}] edited a file that already existed.");

                SubsetExpectations(root, label, features, type, model, access, concurrency, deletion, operations, created, expect);
            }
        }
    }

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

        string use(string operation, string name) => $"UseCases/{operation}/{name}/{name}UseCase.cs";

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
        expect(second.Code == 1 && second.Error.Contains("already exists", StringComparison.Ordinal), "Generating the same feature twice was not refused.");
        expect(Same(afterFirst, Snapshot(root)), "The refused second generation changed the tree.");

        string other = NewTree(scratch);
        string inTheWay = System.IO.Path.Combine(other, "Tests/Application/Acme.Application.UnitTests/Features/Widgets/Model/Errors/WidgetErrorsTests.cs");

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(inTheWay)!);
        File.WriteAllText(inTheWay, "// somebody's own test\n");

        var before = Snapshot(other);
        var blocked = Invoke(other, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(blocked.Code == 1 && blocked.Error.Contains("none is ever overwritten", StringComparison.Ordinal), "A file in the way was not refused.");
        expect(Same(before, Snapshot(other)), "A refusal over an existing file changed the tree.");
        expect(File.ReadAllText(inTheWay) == "// somebody's own test\n", "An existing file was overwritten.");

        string named = NewTree(scratch);

        File.WriteAllText(
            System.IO.Path.Combine(named, "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs"),
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

        expect(manifest.GetProperty("schemaVersion").GetInt32() == 1, "The manifest has no schema version.");
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

        File.WriteAllText(System.IO.Path.Combine(root, "Src/Presentation/Acme.Api/Features"), "not a folder\n");

        var before = Snapshot(root);
        var result = Invoke(root, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(result.Code == 1 && result.Error.Contains("nothing was left behind", StringComparison.Ordinal), "A write that fails half way was not reported as rolled back.");
        expect(Same(before, Snapshot(root)), "A write that failed half way left files or folders behind.");
    }

    private static void ReactionCases(string scratch, Action<bool, string> expect)
    {
        string shipped = NewTree(scratch);

        var present = Invoke(shipped, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(present.Code == 0, $"An owner feature into a tree with the reaction was refused: {present.Error.Split('\n')[0]}");
        expect(
            !File.Exists(System.IO.Path.Combine(shipped, "Src/Application/Acme.Application.Core/Common/Ports/IUserAccountDirectory.cs")),
            "A tree that ships the reaction had its account directory re-emitted.");
        expect(present.Output.Contains("Six edits", StringComparison.Ordinal), "A tree that ships the reaction was told to edit more than six places.");
        expect(!present.Output.Contains("AddDeletedAccountSweep", StringComparison.Ordinal), "A tree that ships the reaction was told to register its sweep again.");

        string bare = NewTree(scratch, reaction: false);
        var bareTree = Snapshot(bare);

        var first = Invoke(bare, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(first.Code == 0, $"The first owner feature into a bare tree was refused: {first.Error.Split('\n')[0]}");

        string[] reaction =
        [
            "Src/Application/Acme.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs",
            "Src/Application/Acme.Application.Core/Common/Ports/IUserAccountDirectory.cs",
            "Src/Application/Acme.Application.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/ISweepDeletedAccountDataUseCase.cs",
            "Src/Application/Acme.Application.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountDataUseCase.cs",
            "Src/Presentation/Acme.Api/Features/BackgroundJobs/Jobs/DeletedAccountSweepJobsExtensions.cs",
            "Src/Presentation/Acme.Api/Features/BackgroundJobs/Jobs/DeletedAccountSweepOptions.cs",
            "Src/Presentation/Acme.Api/Features/Auth/Composition/AccountUserAccountDirectory.cs",
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

        int eleventh = first.Output.IndexOf(" 11. ", StringComparison.Ordinal);
        string hostEdit = eleventh < 0 ? string.Empty : first.Output[eleventh..];

        expect(
            first.Output.Contains(" 11. Tests/Architecture/Acme.Architecture.Tests/Composition/HostComposition.cs: services.AddDeletedAccountSweep();", StringComparison.Ordinal),
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
        expect(first.Output.Contains(RemovalRefusal, StringComparison.Ordinal), "The first owner feature did not say that remove-feature holds it back while another one uses the shared files.");
        ExpectRemovable(bare, bareTree, "Widgets", 7, expect);

        var second = Invoke(bare, "Sprockets", "Sprocket", "--access", "owner", "--deletion", "anonymise");

        expect(second.Code == 0, $"The second owner feature was refused: {second.Error.Split('\n')[0]}");
        expect(
            !File.ReadAllText(System.IO.Path.Combine(bare, "Src/Application/Acme.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs"))
                .Contains("Sprockets", StringComparison.Ordinal),
            "The second owner feature rewrote a file the first one had created.");
        expect(second.Output.Contains("Six edits", StringComparison.Ordinal), "The second owner feature was told to edit more than six places.");
        expect(!second.Output.Contains(RemovalRefusal, StringComparison.Ordinal), "A feature that brought no shared file was told that remove-feature holds it back.");

        string other = NewTree(scratch, reaction: false);

        var shared = Invoke(other, "Widgets", "Widget", "--access", "shared");

        expect(shared.Code == 0, $"A shared feature into a bare tree was refused: {shared.Error.Split('\n')[0]}");
        expect(!Directory.Exists(System.IO.Path.Combine(other, "Src/Application/Acme.Application.Core")), "A shared feature brought the reaction with it.");
        expect(shared.Output.Contains("Six edits", StringComparison.Ordinal), "A shared feature was told to edit more than six places.");

        string external = NewTree(scratch, reaction: false);

        Directory.Delete(System.IO.Path.Combine(external, "Src/Presentation/Acme.Api/Features/Auth"));
        Directory.CreateDirectory(System.IO.Path.Combine(external, "Src/Presentation/Acme.Api/Features/ExternalIdentity"));

        var externalTree = Snapshot(external);
        var mapped = Invoke(external, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        ExpectRemovable(external, externalTree, "Widgets", 7, expect);

        expect(mapped.Code == 0, $"An owner feature into an externally authenticated bare tree was refused: {mapped.Error.Split('\n')[0]}");
        expect(
            File.Exists(System.IO.Path.Combine(external, "Src/Presentation/Acme.Api/Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory.cs")),
            "The external-identity account directory was not written.");
        expect(mapped.Output.Contains("ExternalAuthenticationComposition", StringComparison.Ordinal), "The external composition was not named for the registration.");

        string publicApi = NewTree(scratch, reaction: false);

        Directory.Delete(System.IO.Path.Combine(publicApi, "Src/Presentation/Acme.Api/Features/Auth"), recursive: true);

        var before = Snapshot(publicApi);
        var refused = Invoke(publicApi, "Widgets", "Widget", "--access", "owner", "--deletion", "delete");

        expect(refused.Code == 1, "An owner feature into a tree with no accounts was not refused.");
        expect(refused.Error.Contains("neither the local accounts", StringComparison.Ordinal), "The refusal did not name the missing account directory.");
        expect(Same(before, Snapshot(publicApi)), "The refused tree was written to.");
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static string WithPageTemplate(string root)
    {
        string shipped = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(SourceFile())!)!, PageTemplate);
        string copy = System.IO.Path.Combine(root, PageTemplate);

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(copy)!);
        File.Copy(shipped, copy);

        return root;
    }

    private static void PageCases(string scratch, Action<bool, string> expect)
    {
        string pagePath = PagePath("TodoLists");

        string root = WithPageTemplate(NewTree(scratch));
        var result = Invoke(
            root, "TodoLists", "TodoList", "--access", "owner", "--deletion", "anonymise", "--concurrency", "etag", "--operations", "get,list,update");

        expect(result.Code == 0, $"The page case was refused: {result.Error}");

        string full = System.IO.Path.Combine(root, pagePath);

        expect(File.Exists(full), $"{pagePath} was not written from {PageTemplate}.");

        if (!File.Exists(full))
        {
            return;
        }

        byte[] bytes = File.ReadAllBytes(full);
        string text = Encoding.UTF8.GetString(bytes);
        string template = File.ReadAllText(System.IO.Path.Combine(root, PageTemplate));

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
            "`Tests/Application/Acme.Application.UnitTests/Features/TodoLists`",
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
        expect(!Directory.Exists(System.IO.Path.Combine(bare, "docs")), "A page was written without a page template.");
        expect(none.Output.Contains($"'{PageTemplate}' is not in this tree", StringComparison.Ordinal), "A missing page template is not reported.");
    }

    private const string RemovalRefusal = "remove-feature refuses to remove this feature while another owner feature still uses these files.";

    private static readonly Regex[] RemovableSharedPaths =
    [
        new(@"^Src/Application/[^/]+\.Application\.Core/Common/Ports/(IDeletedAccountDataPolicy|IUserAccountDirectory)\.cs$", RegexOptions.None, TimeSpan.FromSeconds(2)),
        new(@"^Src/Application/[^/]+\.Application\.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/I?SweepDeletedAccountDataUseCase\.cs$", RegexOptions.None, TimeSpan.FromSeconds(2)),
        new(@"^Src/Presentation/[^/]+\.Api/Features/BackgroundJobs/Jobs/DeletedAccountSweep(JobsExtensions|Options)\.cs$", RegexOptions.None, TimeSpan.FromSeconds(2)),
        new(@"^Src/Presentation/[^/]+\.Api/Features/Auth/Composition/AccountUserAccountDirectory\.cs$", RegexOptions.None, TimeSpan.FromSeconds(2)),
        new(@"^Src/Presentation/[^/]+\.Api/Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory\.cs$", RegexOptions.None, TimeSpan.FromSeconds(2)),
    ];

    private static void ExpectRemovable(string root, Dictionary<string, string> before, string features, int count, Action<bool, string> expect)
    {
        var outside = Snapshot(root)
            .Where(pair => pair.Value != "<directory>" && !before.ContainsKey(pair.Key))
            .Select(pair => pair.Key)
            .Where(path => !path.Contains($"/Features/{features}/", StringComparison.Ordinal) && !path.StartsWith(".feature-manifest/", StringComparison.Ordinal))
            .ToList();

        expect(outside.Count == count, $"The reaction wrote {outside.Count} file(s) outside {features}' folders, not {count}.");

        foreach (string path in outside.Where(path => !RemovableSharedPaths.Any(pattern => pattern.IsMatch(path))))
        {
            expect(false, $"{path} is written with the first owner feature but RemoveFeature.cs's SharedPaths would refuse it.");
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
        string expected = string.Join('.', segments[projectIndex..^1]);

        if (!text.Contains($"namespace {expected};", StringComparison.Ordinal))
        {
            yield return $"{relative}: its namespace is not '{expected}', which is its project plus its folders.";
        }

        string stem = System.IO.Path.GetFileNameWithoutExtension(relative);
        var declaration = Regex.Match(
            text,
            @"^\s*(?:public |internal |private )?(?:sealed |static |abstract )*(?:class|interface|record|enum)\s+([A-Za-z0-9_]+)",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(2));

        if (!declaration.Success || !string.Equals(declaration.Groups[1].Value, stem, StringComparison.Ordinal))
        {
            yield return $"{relative}: declares '{(declaration.Success ? declaration.Groups[1].Value : "nothing")}' "
                + $"where the file is named '{stem}'.";
        }

        int declarations = Regex.Count(
            text,
            @"^(?:public |internal )(?:sealed |static |abstract )*(?:class|interface|record|enum)\s",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(2));

        if (declarations != 1)
        {
            yield return $"{relative}: declares {declarations} top-level types, where a file holds one.";
        }

        foreach (string problem in CommentProblems(relative, text))
        {
            yield return problem;
        }
    }

    private static readonly HashSet<string> DocumentationElements =
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
                         .FirstOrDefault(element => !DocumentationElements.Contains(element)) is { } element)
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
    private static readonly HashSet<string> KnownFlags = new(StringComparer.Ordinal)
    {
        "get", "list", "create", "update", "delete", "owner", "shared", "rec", "agg", "etag",
        "content", "change", "wq", "dto", "single", "rq", "notfound", "clock", "stage",
        "purge", "anon",
    };

    private static readonly Regex TokenPattern = new("%([A-Za-z]+)%", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static readonly Regex DirectivePattern = new(
        @"^\s*%(?:if (?<condition>[^%]+)|(?<else>else)|(?<end>end))%\s*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    private static readonly Regex MarkedUsing = new(
        @"^using (?<namespace>[^;]+);\s*//\?(?<types>.+)$",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    internal static bool Evaluate(string condition, HashSet<string> flags) =>
        condition.Split('|').Any(group => group.Split('&').All(atom =>
        {
            string trimmed = atom.Trim();
            bool negated = trimmed.StartsWith('!');
            string name = negated ? trimmed[1..] : trimmed;

            if (!KnownFlags.Contains(name))
            {
                throw new InvalidOperationException($"'{name}' is not a flag a template may name.");
            }

            return flags.Contains(name) != negated;
        }));

    internal static string Substitute(string text, Dictionary<string, string> tokens)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            string next = TokenPattern.Replace(
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
            var directive = DirectivePattern.Match(line);

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
            var marked = MarkedUsing.Match(line);

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
            public const int NameMaxLength = 200;

            public const int DescriptionMaxLength = 2_000;
        %if anon%

            public const string AnonymisedText = "[deleted account]";
        %end%

            private %A%(
                Guid id,
        %if owner%
                UserId ownerId,
        %end%
                string name,
                string description) : base(id)
            {
        %if owner%
                ArgumentNullException.ThrowIfNull(ownerId);

                OwnerId = ownerId;
        %end%
                Name = name;
                Description = description;
            }

        %if owner%
            public UserId OwnerId { get; }

        %end%
        %if update|anon%
            public string Name { get; private set; }

            public string Description { get; private set; }
        %else%
            public string Name { get; }

            public string Description { get; }
        %end%

        %if create%
            public static %A% Create(
        %if owner%
                UserId ownerId,
        %end%
                string name,
                string description) =>
                new(
                    Guid.CreateVersion7(),
        %if owner%
                    ownerId,
        %end%
                    RequireName(name),
                    RequireDescription(description));

        %end%
            public static %A% Rehydrate(
                Guid id,
        %if owner%
                UserId ownerId,
        %end%
                string name,
                string description)
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
                    name,
                    description);
            }

        %if update%
            public void Update(string name, string description)
            {
                Name = RequireName(name);
                Description = RequireDescription(description);
            }

        %end%
        %if anon%
            public void Anonymise()
            {
                Name = AnonymisedText;
                Description = string.Empty;
            }

        %end%
        %if content%
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
            Task<%A%?> GetAsync(Guid id, CancellationToken cancellationToken);

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

        public sealed record %A%ContentDto(string Name, string Description);
        """;

    internal const string Dto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %A%Dto(
            Guid Id,
            string Name,
            string Description,
            DateTimeOffset CreatedAt,
            DateTimeOffset? LastModifiedAt);
        """;

    internal const string SummaryDto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %A%SummaryDto(
            Guid Id,
            string Name,
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
            public const string NameField = "name";

            public const string CreatedAtField = "createdAt";

            public static readonly %A%CollectionPolicy Instance = new();

            public IReadOnlyList<SortableField> SortableFields { get; } =
            [
                SortableField.Keyset(NameField),
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
            public const int NameMaxLength = 200;

            public const int DescriptionMaxLength = 2_000;
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
            IDateTimeProvider dateTimeProvider,
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
                    %A%CollectionPolicy.CreatedAtField);

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

                store.Add(%SvcOwner%%L%Id, Trimmed(content), dateTimeProvider.UtcNow);
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
                    Trimmed(content),
                    dateTimeProvider.UtcNow,
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
        %if content%
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
        using %P%.Application.Features.%F%.Model.Policies;
        using FluentValidation;

        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed class %A%ContentDtoValidator : AbstractValidator<%A%ContentDto>
        {
            public %A%ContentDtoValidator()
            {
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
                    %L%.Name,
                    %L%.Description,
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
        using %P%.Application.Core.Common.Ownership; //?OwnedAggregate
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

        %if owner%
                return OwnedAggregate.Require(
                    await repository.GetAsync(%L%Id, cancellationToken),
                    userId.Value,
                    %A%Errors.%A%NotFound(%L%Id),
                    precondition);
        %else%
                var aggregate = await repository.GetAsync(%L%Id, cancellationToken);

                if (aggregate is null)
                {
                    return Result.Failure<%A%>(%A%Errors.%A%NotFound(%L%Id));
                }

                return precondition is not null && !precondition.IsSatisfiedBy(aggregate.Version)
                    ? Result.Failure<%A%>(ConcurrencyErrors.PreconditionFailed)
                    : Result.Success(aggregate);
        %end%
            }
        }
        """;

    internal const string GetQuery = """
        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%A%;

        public sealed record Get%A%Query(Guid %A%Id);
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

    internal const string GetUseCasePort = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.UseCases;
        using %P%.Application.Features.%F%.Model.Dtos;

        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%A%;

        public interface IGet%A%UseCase : IUseCase<Get%A%Query, Result<Versioned<%A%Dto>>>;
        """;

    internal const string GetUseCase = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.Validation;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Errors;
        using %P%.Application.Features.%F%.Ports.%A%Queries;
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%A%;

        public sealed class Get%A%UseCase(
            I%A%Queries queries,
            ICurrentUser currentUser,
            IValidator<Get%A%Query> validator) : IGet%A%UseCase
        {
            public async Task<Result<Versioned<%A%Dto>>> ExecuteAsync(
                Get%A%Query query,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(query);

                var validation = await validator.EnsureValidAsync(query, cancellationToken);

                if (validation.IsFailure)
                {
                    return validation.To<Versioned<%A%Dto>>();
                }

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

    internal const string ListUseCasePort = """
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.UseCases;
        using %P%.Application.Features.%F%.Model.Dtos;

        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%F%;

        public interface IGet%F%UseCase : IUseCase<Get%F%Query, Result<PagedResult<%A%SummaryDto>>>;
        """;

    internal const string ListUseCase = """
        using %P%.Application.Core.Common.Collections;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Policies;
        using %P%.Application.Features.%F%.Ports.%A%Queries;

        namespace %P%.Application.Features.%F%.UseCases.Queries.Get%F%;

        public sealed class Get%F%UseCase(I%A%Queries queries, ICurrentUser currentUser) : IGet%F%UseCase
        {
            public async Task<Result<PagedResult<%A%SummaryDto>>> ExecuteAsync(
                Get%F%Query query,
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
                    %A%CollectionPolicy.CreatedAtField);

                if (bound.IsFailure)
                {
                    return bound.To<PagedResult<%A%SummaryDto>>();
                }

                return await queries.%ListQuery%(%SvcOwner%bound.Value, cancellationToken);
            }
        }
        """;

    internal const string CreateCommand = """
        namespace %P%.Application.Features.%F%.UseCases.Commands.Create%A%;

        public sealed record Create%A%Command(string Name, string Description);
        """;

    internal const string CreateCommandValidator = """
        using %P%.Domain.Features.%F%.Model.Entities;
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Create%A%;

        public sealed class Create%A%CommandValidator : AbstractValidator<Create%A%Command>
        {
            public Create%A%CommandValidator()
            {
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
            }
        }
        """;

    internal const string CreateUseCasePort = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.UseCases;
        using %P%.Application.Features.%F%.Model.Dtos;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Create%A%;

        public interface ICreate%A%UseCase : IUseCase<Create%A%Command, Result<Versioned<%A%Dto>>>;
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
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Create%A%;

        public sealed class Create%A%UseCase(
            I%A%Repository repository,
            IUnitOfWork unitOfWork,
            ICurrentUser currentUser,
            IValidator<Create%A%Command> validator) : ICreate%A%UseCase
        {
            public async Task<Result<Versioned<%A%Dto>>> ExecuteAsync(
                Create%A%Command command,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(command);

                var userId = currentUser.RequireUserId();

                if (userId.IsFailure)
                {
                    return userId.To<Versioned<%A%Dto>>();
                }

                var validation = await validator.EnsureValidAsync(command, cancellationToken);

                if (validation.IsFailure)
                {
                    return validation.To<Versioned<%A%Dto>>();
                }

                var %L% = %A%.Create(%SvcOwner%command.Name, command.Description);

                repository.Add(%L%);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                return %A%DtoMapping.Detail(%L%);
            }
        }
        """;

    internal const string UpdateCommand = """
        using %P%.Application.Core.Common.Concurrency;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Update%A%;

        public sealed record Update%A%Command(
            Guid %A%Id,
            string Name,
            string Description,
            VersionPrecondition? Precondition = null);
        """;

    internal const string UpdateCommandValidator = """
        using %P%.Domain.Features.%F%.Model.Entities;
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Update%A%;

        public sealed class Update%A%CommandValidator : AbstractValidator<Update%A%Command>
        {
            public Update%A%CommandValidator()
            {
                RuleFor(command => command.%A%Id)
                    .NotEmpty().WithMessage("%An% %L% id is required.");

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
            }
        }
        """;

    internal const string UpdateUseCasePort = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.UseCases;
        using %P%.Application.Features.%F%.Model.Dtos;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Update%A%;

        public interface IUpdate%A%UseCase : IUseCase<Update%A%Command, Result<Versioned<%A%Dto>>>;
        """;

    internal const string UpdateUseCase = """
        using %P%.Application.Core.Common.Concurrency;
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.Validation;
        using %P%.Application.Features.%F%.Model.Dtos;
        using %P%.Application.Features.%F%.Model.Mapping;
        using %P%.Application.Features.%F%.Services;
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Update%A%;

        public sealed class Update%A%UseCase(
            I%A%Service service,
            IUnitOfWork unitOfWork,
            IValidator<Update%A%Command> validator) : IUpdate%A%UseCase
        {
            public async Task<Result<Versioned<%A%Dto>>> ExecuteAsync(
                Update%A%Command command,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(command);

                var validation = await validator.EnsureValidAsync(command, cancellationToken);

                if (validation.IsFailure)
                {
                    return validation.To<Versioned<%A%Dto>>();
                }

                var loaded = await service.%Load%(command.%A%Id, command.Precondition, cancellationToken);

                if (loaded.IsFailure)
                {
                    return loaded.To<Versioned<%A%Dto>>();
                }

                var %L% = loaded.Value;

                %L%.Update(command.Name, command.Description);

                await unitOfWork.SaveChangesAsync(cancellationToken);

                return %A%DtoMapping.Detail(%L%);
            }
        }
        """;

    internal const string DeleteCommand = """
        using %P%.Application.Core.Common.Concurrency;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Delete%A%;

        public sealed record Delete%A%Command(Guid %A%Id, VersionPrecondition? Precondition = null);
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

    internal const string DeleteUseCasePort = """
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.UseCases;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Delete%A%;

        public interface IDelete%A%UseCase : IUseCase<Delete%A%Command, Result>;
        """;

    internal const string DeleteUseCase = """
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.Validation;
        using %P%.Application.Features.%F%.Services;
        using %P%.Domain.Features.%F%.Repositories;
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.Commands.Delete%A%;

        public sealed class Delete%A%UseCase(
            I%A%Service service,
            I%A%Repository repository,
            IUnitOfWork unitOfWork,
            IValidator<Delete%A%Command> validator) : IDelete%A%UseCase
        {
            public async Task<Result> ExecuteAsync(
                Delete%A%Command command,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(command);

                var validation = await validator.EnsureValidAsync(command, cancellationToken);

                if (validation.IsFailure)
                {
                    return validation;
                }

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
            public string Name { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;

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
            public string Name { get; set; } = string.Empty;

            public string Description { get; set; } = string.Empty;

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
        using %P%.Application.Features.%F%.Model.Policies;
        %else%
        using %P%.Domain.Features.%F%.Model.Entities;
        %end%
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
                builder.HasIndex(record => new { record.OwnerId, record.Name, record.Id })
                    .HasDatabaseName("IX_%F%_OwnerId_Name_Id");

                builder.HasIndex(record => new { record.OwnerId, record.CreatedAt, record.Id })
                    .HasDatabaseName("IX_%F%_OwnerId_CreatedAt_Id");
        %else%
                builder.HasIndex(record => new { record.Name, record.Id })
                    .HasDatabaseName("IX_%F%_Name_Id");

                builder.HasIndex(record => new { record.CreatedAt, record.Id })
                    .HasDatabaseName("IX_%F%_CreatedAt_Id");
        %end%

        %else%
        %if owner%
                builder.HasIndex(record => record.OwnerId)
                    .HasDatabaseName("IX_%F%_OwnerId");

        %end%
        %end%
                builder.Property(record => record.Name)
                    .HasMaxLength(%Limits%.NameMaxLength)
                    .IsRequired();

                builder.Property(record => record.Description)
                    .HasMaxLength(%Limits%.DescriptionMaxLength)
                    .IsRequired();

                builder.Property(record => record.CreatedAt).IsRequired();
        %if agg%
                builder.Property(record => record.CreatedBy);
        %end%
                builder.Property(record => record.LastModifiedAt);
        %if agg%
                builder.Property(record => record.LastModifiedBy);
        %end%
        %if etag%

                builder.Property(record => record.Version)
                    .HasColumnName("xmin")
                    .HasColumnType("xid")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
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
                    Name = content.Name,
                    Description = content.Description,
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
                    record.Name = content.Name;
                    record.Description = content.Description;
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
                    record.Name = AnonymisedText;
                    record.Description = string.Empty;
                }

                return records.Count;
            }

            private const string AnonymisedText = "[deleted account]";
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
                    record.Name,
                    record.Description);

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
                    Name = aggregate.Name,
                    Description = aggregate.Description,

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

                record.Name = aggregate.Name;
                record.Description = aggregate.Description;
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
        %if change%
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

    internal const string SweepUseCasePort = """
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.UseCases;

        namespace %P%.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;

        public interface ISweepDeletedAccountDataUseCase : IUseCase<Result<int>>
        {
        }
        """;

    internal const string SweepUseCase = """
        using %P%.Application.Core.Common.Ports;
        using %P%.Application.Core.Common.Results;
        using Microsoft.Extensions.Logging;

        namespace %P%.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;

        public sealed class SweepDeletedAccountDataUseCase(
            IEnumerable<IDeletedAccountDataPolicy> policies,
            ILeaderLease lease,
            ILogger<SweepDeletedAccountDataUseCase> logger) : ISweepDeletedAccountDataUseCase
        {
            private const string _leaseName = "apptemplate.accounts.sweep-deleted-data";

            public async Task<Result<int>> ExecuteAsync(CancellationToken cancellationToken = default)
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
        using %P%.Api.Features.Maintenance.Jobs;
        using %P%.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;
        using Microsoft.Extensions.Options;

        namespace %P%.Api.Features.BackgroundJobs.Jobs;

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

                services.AddMaintenanceTask<ISweepDeletedAccountDataUseCase>(
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
        namespace %P%.Api.Features.BackgroundJobs.Jobs;

        internal sealed class DeletedAccountSweepOptions
        {
            public bool SweepDeletedAccountsDataEnabled { get; set; } = true;
        }
        """;

    internal const string LocalUserAccountDirectory = """
        using %P%.Application.Auth.Features.Accounts.Ports.Accounts;
        using %P%.Application.Core.Common.Ports;
        using %P%.Domain.Core.Common.Primitives;

        namespace %P%.Api.Features.Auth.Composition;

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

        namespace %P%.Api.Features.ExternalIdentity.Composition;

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
                        new %A%Dto(record.Id, record.Name, record.Description, record.CreatedAt, record.LastModifiedAt),
                        record.Version))
        %else%
                    .Select(record => new %A%Dto(
                        record.Id,
                        record.Name,
                        record.Description,
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
                record => new %A%SummaryDto(record.Id, record.Name, record.CreatedAt, record.LastModifiedAt);
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
        namespace %P%.Api.Features.%F%.Contracts.Requests;

        %if owner%
        /// <summary>The %L% to create, whose owner is the authenticated caller.</summary>
        %else%
        /// <summary>The %L% to create.</summary>
        %end%
        /// <param name="Name">Required, at most 200 characters once trimmed.</param>
        /// <param name="Description">Required but may be empty, at most 2000 characters.</param>
        public sealed record Create%A%Request(string Name, string Description);
        """;

    internal const string UpdateRequest = """
        namespace %P%.Api.Features.%F%.Contracts.Requests;

        /// <summary>The new text of %a% %L%, whose id travels in the route.</summary>
        /// <param name="Name">Required, at most 200 characters once trimmed.</param>
        /// <param name="Description">Required but may be empty, at most 2000 characters.</param>
        public sealed record Update%A%Request(string Name, string Description);
        """;

    internal const string ListRequest = """
        namespace %P%.Api.Features.%F%.Contracts.Requests;

        /// <summary>The paging and sorting of a list, bound from the query string.</summary>
        /// <param name="Paging"><c>offset</c> (the default) or <c>cursor</c>.</param>
        /// <param name="Page">The page number, in offset mode.</param>
        /// <param name="PageSize">The number of items per page.</param>
        /// <param name="Cursor">The previous page's <c>nextCursor</c>, in cursor mode.</param>
        /// <param name="Sort">Comma-separated fields (<c>name</c>, <c>createdAt</c>), each optionally suffixed <c>:asc</c> or <c>:desc</c>; at most one in cursor mode.</param>
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
            string Name,
            string Description,
            DateTimeOffset CreatedAt,
            DateTimeOffset? LastModifiedAt);
        """;

    internal const string SummaryResponse = """
        namespace %P%.Api.Features.%F%.Contracts.Responses;

        /// <summary>The wire shape of one %L% inside a page, without its description.</summary>
        public sealed record %A%SummaryResponse(
            Guid Id,
            string Name,
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

                return new %A%Response(%L%.Id, %L%.Name, %L%.Description, %L%.CreatedAt, %L%.LastModifiedAt);
            }

        %end%
        %if list%
            public static %A%SummaryResponse ToResponse(%A%SummaryDto summary)
            {
                ArgumentNullException.ThrowIfNull(summary);

                return new %A%SummaryResponse(summary.Id, summary.Name, summary.CreatedAt, summary.LastModifiedAt);
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
            /// <summary>Lists %Scope%%F%, sorted and paginated, names only.</summary>
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
            /// <summary>Replaces %a% %L%'s name and description, on the version an <c>If-Match</c> names.</summary>
        %else%
            /// <summary>Replaces %a% %L%'s name and description; of two concurrent writers the later one wins.</summary>
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

    internal const string ErrorsTests = """
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.Model.Errors;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.Model.Errors;

        public sealed class %A%ErrorsTests
        {
            [Fact]
            public void %A%NotFound_IsANotFoundErrorNamingThe%A%()
            {
                var %L%Id = Guid.CreateVersion7();

                var error = %A%Errors.%A%NotFound(%L%Id);

                error.Type.ShouldBe(ErrorType.NotFound);
                error.Code.ShouldBe("%L%.notFound");
                error.Message.ShouldContain(%L%Id.ToString());
            }
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

        %if content%
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
            public async Task GetAsync_A%A%ThatIsNotThere_IsNotFound()
            {
                _queries.GetAsync(%AnyOwner%Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                    .Returns((%ReadDto%?)null);

                var result = await Service().GetAsync(Guid.CreateVersion7(), TestToken);

                result.Error!.Type.ShouldBe(ErrorType.NotFound);
                result.Error.Code.ShouldBe("%L%.notFound");
            }

        %end%
        %if create%
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
            public async Task DeleteAsync_A%A%ThatCannotBeStaged_FailsAndCommitsNothing()
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

            private %A%Service Service() => ServiceFor(StubCurrentUser.WithId(_callerId));
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
        using %P%.Domain.Features.%F%.Model.Entities;
        %end%
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
                version.GetColumnName().ShouldBe("xmin");
                version.GetColumnType().ShouldBe("xid");
            }
        %else%
            [Fact]
            public void NoPropertyIsAConcurrencyToken() =>
                Entity().GetProperties().ShouldAllBe(property => !property.IsConcurrencyToken);
        %end%

            [Theory]
            [InlineData(nameof(%A%Record.Name), %Limits%.NameMaxLength)]
            [InlineData(nameof(%A%Record.Description), %Limits%.DescriptionMaxLength)]
            public void EveryTextColumn_IsAsLongAsTheValidatorAllows(string property, int expected) =>
                Entity().FindProperty(property)!.GetMaxLength().ShouldBe(expected);

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
            public void TheTable_LivesInItsOwnSchema()
            {
                Entity().GetTableName().ShouldBe("%F%");
                Entity().GetSchema().ShouldBe(AppDbContext.%F%Schema);
            }

            private static IEntityType Entity()
            {
                using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                    .UseNpgsql("Host=localhost;Database=probe;Username=none;Password=none")
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

    internal const string ResponseMappingTests = """
        using %P%.Api.Features.%F%.Contracts.Mapping;
        using %P%.Application.Core.Common.Concurrency; //?Versioned
        using %P%.Application.Core.Common.Results; //?Result\.
        using %P%.Application.Features.%F%.Model.Dtos;
        using Shouldly;
        using Xunit;

        namespace %P%.Api.UnitTests.Features.%F%.Contracts.Mapping;

        public sealed class %A%ResponseMappingTests
        {
            private static readonly DateTimeOffset _createdAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

            [Fact]
            public void ToResponse_CarriesEveryFieldOfTheDto()
            {
                var dto = new %A%Dto(Guid.CreateVersion7(), "Groceries", "Milk, eggs", _createdAt, _createdAt.AddMinutes(5));

                var response = %A%ResponseMapping.ToResponse(dto);

                response.Id.ShouldBe(dto.Id);
                response.Name.ShouldBe(dto.Name);
                response.Description.ShouldBe(dto.Description);
                response.CreatedAt.ShouldBe(dto.CreatedAt);
                response.LastModifiedAt.ShouldBe(dto.LastModifiedAt);
            }

        %if list%
            [Fact]
            public void ToResponse_CarriesEveryFieldOfTheSummary()
            {
                var summary = new %A%SummaryDto(Guid.CreateVersion7(), "Groceries", _createdAt, null);

                var response = %A%ResponseMapping.ToResponse(summary);

                response.Id.ShouldBe(summary.Id);
                response.Name.ShouldBe(summary.Name);
                response.CreatedAt.ShouldBe(summary.CreatedAt);
                response.LastModifiedAt.ShouldBeNull();
            }

        %end%
        %if etag%
            [Fact]
            public void To%A%Response_CarriesTheVersionBesideTheResponse()
            {
                var dto = new %A%Dto(Guid.CreateVersion7(), "Groceries", "Milk, eggs", _createdAt, null);

                var result = %A%ResponseMapping.To%A%Response(Result.Success(new Versioned<%A%Dto>(dto, 7)));

                result.Value.Version.ShouldBe(7u);
                result.Value.Value.Id.ShouldBe(dto.Id);
            }
        %else%
            [Fact]
            public void To%A%Response_AnswersTheResponseAlone()
            {
                var dto = new %A%Dto(Guid.CreateVersion7(), "Groceries", "Milk, eggs", _createdAt, null);

                var result = %A%ResponseMapping.To%A%Response(Result.Success(dto));

                result.Value.Id.ShouldBe(dto.Id);
            }
        %end%
        }
        """;
}
