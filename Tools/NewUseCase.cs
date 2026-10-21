#:property UseAppHost=false

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class NewUseCase
{
    private const string ManifestFolder = ".feature-manifest";

    private const string Usage =
        "Usage: dotnet run Tools/NewUseCase.cs <Features> <UseCase> --kind command|query\n"
        + "           [--input id|none]\n"
        + "           [--root <path>]\n"
        + "  <Features>  the folder of an existing feature of the rich shape, e.g. Invoices\n"
        + "  <UseCase>   the operation, PascalCase, a verb and its subject, e.g. ApproveInvoice\n"
        + "  --kind      required. command: changes state and answers a Result.\n"
        + "              query: reads, and answers a Result of a DTO named for it.\n"
        + "  --input     default: id, a request carrying the id of the feature's aggregate, its\n"
        + "              validator and its unit tests. none: no request, no validator, no test.\n"
        + "  --root      the tree to write into, default the current directory.\n"
        + "       dotnet run Tools/NewUseCase.cs --self-test";

    private static readonly string[] OptionNames = ["--kind", "--input", "--root"];

    private static readonly string[] ReservedNames =
    [
        "Access", "Api", "Application", "Auth", "Commands", "Common", "Contracts", "Controllers", "Core",
        "Dtos", "Domain", "Entities", "Errors", "Features", "Infrastructure", "Model", "Models",
        "Persistence", "Policies", "Ports", "Presentation", "Queries", "Rows", "Services", "UseCases",
    ];

    private static readonly string[] ReservedSuffixes = ["UseCase", "Command", "Query", "Validator", "Dto"];

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

    private static readonly Regex TokenPattern = new("%([A-Za-z]+)%", RegexOptions.None, TimeSpan.FromSeconds(2));

    private sealed record Emission(string Path, string Content);

    private sealed record Request(string? Features, string? Name, string? Kind, string? Input, string Root);

    private sealed record Spec(
        string Prefix,
        string Features,
        string Name,
        string Kind,
        string? Aggregate,
        string? Dto,
        bool DtoExists,
        string? Manifest)
    {
        internal bool IsCommand => Kind == "command";

        internal bool HasInput => Aggregate is not null;

        internal string Folder => IsCommand ? "Commands" : "Queries";

        internal string Input => Name + (IsCommand ? "Command" : "Query");

        internal string Namespace => $"{Prefix}.Application.Features.{Features}.UseCases.{Folder}.{Name}";
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

        return Run(args, Console.Out, Console.Error);
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

        var plan = Plan(spec, root, out string? skipped);
        string[] existing = [.. plan.Select(emission => emission.Path).Where(path => File.Exists(Path.Combine(root, path)))];

        if (existing.Length > 0)
        {
            problems.Add(
                $"{existing.Length} of the files to write already exist, and none is ever overwritten:\n"
                + string.Join("\n", existing.Select(path => $"  {path}")));

            return Refuse(error, problems);
        }

        Emission? manifest = spec.Manifest is null ? null : ManifestWith(root, spec.Manifest, plan);

        try
        {
            WriteAll(root, plan, manifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"error: nothing was left behind, but the write failed: {exception.Message}");

            return 1;
        }

        Report(output, root, spec, plan, skipped);

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
                $"Expected two names, <Features> and <UseCase>, and found {positional.Count}. "
                + "The feature's folder, then the operation: Invoices ApproveInvoice.");
        }

        string? Option(string name) => options.TryGetValue(name, out string? value) ? value : null;

        return new Request(
            positional.Count > 0 ? positional[0] : null,
            positional.Count > 1 ? positional[1] : null,
            Option("--kind"),
            Option("--input"),
            Option("--root") ?? ".");
    }

    private static Spec? Check(Request request, string root, List<string> problems)
    {
        string features = request.Features ?? string.Empty;
        string name = request.Name ?? string.Empty;

        problems.AddRange(NameProblems(features, name));

        string kind = request.Kind ?? string.Empty;

        if (request.Kind is null)
        {
            problems.Add(
                "--kind is required and has no default. Say what the use case does: --kind command "
                + "(it changes state) or --kind query (it reads).");
        }
        else if (kind is not ("command" or "query"))
        {
            problems.Add($"--kind '{kind}' is not a choice. It is command or query.");
        }

        string input = request.Input ?? "id";

        if (input is not ("id" or "none"))
        {
            problems.Add($"--input '{input}' is not a choice. It is id or none.");
        }

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

        string applicationFeatures = Path.Combine(root, "Src", "Application", $"{prefix}.Application", "Features");
        string application = Path.Combine(applicationFeatures, features);

        if (!Directory.Exists(application))
        {
            string[] known = Directory.Exists(applicationFeatures)
                ? [.. Directory.EnumerateDirectories(applicationFeatures).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
                : [];

            problems.Add(
                $"No feature called {features}: 'Src/Application/{prefix}.Application/Features/{features}' does not exist. "
                + (known.Length == 0
                    ? "This tree has no feature yet; write one first, as docs/guides/adding-a-feature.md says."
                    : $"The features here are {string.Join(", ", known)}."));

            return null;
        }

        string domain = Path.Combine(root, "Src", "Domain", $"{prefix}.Domain", "Features", features);

        if (!Directory.Exists(domain))
        {
            problems.Add(
                $"{features} has no domain folder, so it is a service over a store: its operations are methods of its "
                + "service, and a use case of its own would make it neither shape. Add a method to the service, or "
                + "promote the feature to an aggregate first (docs/architecture/feature-shapes.md, From simple to rich).");

            return null;
        }

        foreach (string folder in new[] { "Commands", "Queries" })
        {
            if (Directory.Exists(Path.Combine(application, "UseCases", folder, name)))
            {
                problems.Add(
                    $"{features} already has a use case called {name}, under UseCases/{folder}/{name}. A use case is "
                    + "written once; after that it is edited.");

                return null;
            }
        }

        string? aggregate = null;

        if (input == "id")
        {
            string repositories = Path.Combine(domain, "Repositories");
            string[] found = Directory.Exists(repositories)
                ? [.. Directory.EnumerateFiles(repositories, "I*Repository.cs")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OfType<string>()
                    .Select(file => file[1..^"Repository".Length])
                    .Where(type => PascalCase.IsMatch(type))
                    .Order(StringComparer.Ordinal)]
                : [];

            if (found.Length != 1)
            {
                problems.Add(
                    found.Length == 0
                        ? $"--input id carries the id of the feature's aggregate, and {features} declares no repository "
                            + "under Repositories/ to name it. Pass --input none and add the request by hand."
                        : $"--input id carries the id of the feature's aggregate, and {features} declares several "
                            + $"({string.Join(", ", found)}), so which one is ambiguous. Pass --input none and add the "
                            + "request by hand.");

                return null;
            }

            aggregate = found[0];

            if (string.Equals(name, aggregate, StringComparison.Ordinal))
            {
                problems.Add(
                    $"'{name}' is the aggregate's own name, so the use case's namespace would hide the type. Name the "
                    + $"operation: a verb and its subject, e.g. Approve{aggregate}.");

                return null;
            }
        }

        string? dto = null;
        bool dtoExists = false;

        if (kind == "query")
        {
            dto = (name.StartsWith("Get", StringComparison.Ordinal) && name.Length > 3 && char.IsUpper(name[3]) ? name[3..] : name) + "Dto";
            dtoExists = File.Exists(Path.Combine(application, "Model", "Dtos", dto + ".cs"));
        }

        string manifest = $"{ManifestFolder}/{features}.json";
        string manifestFull = Path.Combine(root, manifest);

        if (File.Exists(manifestFull))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(manifestFull)) is not JsonObject top
                    || top["feature"]?.GetValue<string>() != features
                    || top["files"] is not JsonArray)
                {
                    problems.Add($"'{manifest}' does not read as the manifest of {features}, so the files written could not be recorded in it. Repair or delete it first.");

                    return null;
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
            {
                problems.Add($"'{manifest}' cannot be read ({exception.Message}), so the files written could not be recorded in it. Repair or delete it first.");

                return null;
            }
        }

        return new Spec(prefix, features, name, kind, aggregate, dto, dtoExists, File.Exists(manifestFull) ? manifest : null);
    }

    private static List<string> NameProblems(string features, string name)
    {
        var problems = new List<string>();

        if (!PascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a feature folder: <Features> is PascalCase and plural, e.g. Invoices.");
        }

        if (!PascalCase.IsMatch(name))
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

        if (ReservedNames.Contains(name, StringComparer.Ordinal))
        {
            problems.Add($"'{name}' is already a segment of the namespaces around a use case. Name the operation: a verb and its subject.");
        }

        if (ReservedSuffixes.FirstOrDefault(suffix => name.EndsWith(suffix, StringComparison.Ordinal)) is { } suffix)
        {
            problems.Add($"'{name}' ends in '{suffix}', which the generated types append themselves. Name the operation alone, e.g. {name[..^suffix.Length]}.");
        }

        if (Keywords.Contains(Lower(name), StringComparer.Ordinal))
        {
            problems.Add($"'{name}' becomes the C# keyword '{Lower(name)}' once its first letter is lowered, which is how the controller names it. Choose another name.");
        }

        return problems;
    }

    private static string ProductPrefix(string root)
    {
        string domain = Path.Combine(root, "Src", "Domain");

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

    private static string Separated(string pascal, char separator) =>
        string.Concat(pascal.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? separator + char.ToLowerInvariant(character).ToString()
                : char.ToLowerInvariant(character).ToString()));

    private static Dictionary<string, string> Tokens(Spec spec)
    {
        string spoken = spec.Aggregate is null ? string.Empty : Separated(spec.Aggregate, ' ');
        string result = spec.IsCommand ? "Result" : $"Result<{spec.Dto}>";

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["P"] = spec.Prefix,
            ["F"] = spec.Features,
            ["U"] = spec.Name,
            ["A"] = spec.Aggregate ?? string.Empty,
            ["K"] = spec.Folder,
            ["In"] = spec.Input,
            ["in"] = spec.IsCommand ? "command" : "query",
            ["D"] = spec.Dto ?? string.Empty,
            ["Dtos"] = spec.IsCommand ? string.Empty : $"{spec.Prefix}.Application.Features.{spec.Features}.Model.Dtos",
            ["Out"] = result,
            ["Contract"] = spec.HasInput ? $"IUseCase<{spec.Input}, {result}>" : $"IUseCase<{result}>",
            ["Refusal"] = spec.IsCommand ? "validation" : $"validation.To<{spec.Dto}>()",
            ["Subject"] = spoken.Length == 0 ? string.Empty : (IsVowel(spoken[0]) ? "An " : "A ") + spoken,
            ["Unwritten"] = Unwritten(spec),
        };
    }

    private static string Unwritten(Spec spec) => (spec.IsCommand, spec.HasInput) switch
    {
        (true, true) => $"\"{spec.Name} is not written yet: load the {spec.Aggregate}, call the method that holds the rule, then commit through IUnitOfWork.\"",
        (true, false) => $"\"{spec.Name} is not written yet: decide, then commit through IUnitOfWork.\"",
        _ => $"\"{spec.Name} is not written yet: read through the feature's queries port into {spec.Dto}.\"",
    };

    private static string Render(string template, Dictionary<string, string> tokens)
    {
        string text = TokenPattern.Replace(
            template,
            match => tokens.TryGetValue(match.Groups[1].Value, out string? value) ? value : match.Value);

        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        int usings = lines.TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal)).Count();

        string[] sorted = [.. lines[..usings]
            .Where(line => line != "using ;")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(line => line.StartsWith("using System", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(line => line.TrimEnd(';'), StringComparer.OrdinalIgnoreCase)
            .ThenBy(line => line.TrimEnd(';'), StringComparer.Ordinal)];

        return string.Join('\n', [.. sorted, .. lines[usings..]]).TrimEnd('\n') + "\n";
    }

    private static List<Emission> Plan(Spec spec, string root, out string? skippedTests)
    {
        var tokens = Tokens(spec);
        var emissions = new List<Emission>();

        string application = $"Src/Application/{spec.Prefix}.Application/Features/{spec.Features}";
        string folder = $"{application}/UseCases/{spec.Folder}/{spec.Name}";

        void Add(string path, string template) => emissions.Add(new Emission(path, Render(template, tokens)));

        if (spec.HasInput)
        {
            Add($"{folder}/{spec.Input}.cs", spec.IsCommand ? Templates.CommandRequest : Templates.QueryRequest);
            Add($"{folder}/{spec.Input}Validator.cs", Templates.Validator);
        }

        Add($"{folder}/I{spec.Name}UseCase.cs", spec.IsCommand ? Templates.CommandPort : Templates.QueryPort);
        Add($"{folder}/{spec.Name}UseCase.cs", spec.HasInput ? Templates.UseCaseWithInput : Templates.UseCaseWithoutInput);

        if (spec.Dto is not null && !spec.DtoExists)
        {
            Add($"{application}/Model/Dtos/{spec.Dto}.cs", Templates.Dto);
        }

        skippedTests = null;

        if (spec.HasInput)
        {
            string tests = $"Tests/Application/{spec.Prefix}.Application.UnitTests";

            if (Directory.Exists(Path.Combine(root, tests)))
            {
                Add($"{tests}/Features/{spec.Features}/UseCases/{spec.Folder}/{spec.Name}/{spec.Name}UseCaseTests.cs", Templates.UseCaseTests);
            }
            else
            {
                skippedTests = tests;
            }
        }

        return emissions;
    }

    private static byte[] Bytes(Emission emission)
    {
        bool bom = emission.Path.EndsWith(".cs", StringComparison.Ordinal);
        string text = emission.Content.ReplaceLineEndings("\n");

        return bom
            ? [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]
            : Encoding.UTF8.GetBytes(text);
    }

    private static string HashOf(Emission emission) =>
        Convert.ToHexStringLower(SHA256.HashData(Bytes(emission)));

    private static Emission ManifestWith(string root, string manifest, List<Emission> plan)
    {
        var top = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(root, manifest)))!;
        var files = (JsonArray)top["files"]!;

        var entries = files
            .OfType<JsonObject>()
            .Select(file => (Path: file["path"]!.GetValue<string>(), Sha256: file["sha256"]!.GetValue<string>()))
            .Concat(plan.Select(emission => (emission.Path, Sha256: HashOf(emission))))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();

        var merged = new JsonArray();

        foreach (var (path, sha256) in entries)
        {
            merged.Add((JsonNode)new JsonObject { ["path"] = path, ["sha256"] = sha256 });
        }

        top["files"] = merged;

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            top.WriteTo(writer);
        }

        return new Emission(manifest, Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n");
    }

    private static void WriteAll(string root, List<Emission> emissions, Emission? manifest)
    {
        var files = new List<string>();
        var directories = new List<string>();

        try
        {
            foreach (var emission in emissions)
            {
                string full = Path.Combine(root, emission.Path);
                string directory = Path.GetDirectoryName(full)!;

                var missing = new Stack<string>();

                for (string next = directory; !Directory.Exists(next); next = Path.GetDirectoryName(next)!)
                {
                    missing.Push(next);
                }

                directories.AddRange(missing);
                Directory.CreateDirectory(directory);

                using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write);

                stream.Write(Bytes(emission));
                files.Add(full);
            }

            if (manifest is not null)
            {
                string full = Path.Combine(root, manifest.Path);
                string staged = full + ".new";

                File.WriteAllBytes(staged, Bytes(manifest));
                files.Add(staged);
                File.Move(staged, full, overwrite: true);
            }
        }
        catch
        {
            foreach (string file in files.Where(File.Exists))
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

    private static void Report(TextWriter output, string root, Spec spec, List<Emission> plan, string? skipped)
    {
        string p = spec.Prefix;
        string f = spec.Features;
        string u = spec.Name;
        string field = Lower(u);

        output.WriteLine($"Wrote {plan.Count} files for the {spec.Kind} {u} in {f}:");

        foreach (var emission in plan)
        {
            output.WriteLine($"  {emission.Path}");
        }

        output.WriteLine();

        if (spec.DtoExists)
        {
            output.WriteLine($"It answers the {spec.Dto} the feature already declares under Model/Dtos.");
        }

        output.WriteLine(spec.Manifest is null
            ? $"No manifest names {f}, so remove-feature never deletes these files; they are the feature's like any it holds."
            : $"Recorded in {spec.Manifest} with their SHA-256, so remove-feature deletes them with the feature while they are untouched.");

        if (skipped is not null)
        {
            output.WriteLine($"Not written: '{skipped}' is not in this tree, so the use case's tests were skipped.");
        }

        output.WriteLine();
        output.WriteLine($"Nothing to register: AddFeature(\"{f}\") in ApplicationModule finds the use case and its validator by folder.");
        output.WriteLine($"Until its body is written, {u}UseCase answers its validation and then throws NotImplementedException.");
        output.WriteLine();
        output.WriteLine("What it decides is yours to write, each with its test (docs/guides/testing.md):");

        if (spec.IsCommand)
        {
            output.WriteLine($"  - the rule, as a method of the {spec.Aggregate ?? "aggregate"} in the domain, and a domain test per invariant");
            output.WriteLine("  - the body: load through the feature's service or repository, call that method, commit through IUnitOfWork");
        }
        else
        {
            output.WriteLine($"  - {spec.Dto}'s members, and a method of the feature's queries port that projects them");
            output.WriteLine("  - the body: ask ICurrentUser for the caller when the feature is owned, then that method");
        }

        output.WriteLine("  - its dependencies, in the primary constructor beside the validator");
        output.WriteLine("  - one unit test per decision the body adds, each refusal with its control");
        output.WriteLine("  - an error code of its own: its row in docs/reference/api.md, which the error-code tests read");

        string controller = $"Src/Presentation/{p}.Api/Features/{f}/Controllers/{f}Controller.cs";
        string where = File.Exists(Path.Combine(root, controller))
            ? controller
            : $"a controller under Src/Presentation/{p}.Api/Features/{f}/Controllers/";

        output.WriteLine();
        output.WriteLine($"To serve it over HTTP, the edit by hand in {where}:");
        output.WriteLine($"  the usings it lacks  {spec.Namespace};");

        if (!spec.IsCommand)
        {
            output.WriteLine($"                       {p}.Application.Core.Common.Results;");
            output.WriteLine($"                       {p}.Api.Features.{f}.Contracts.Responses;");
        }

        output.WriteLine($"  the parameter        I{u}UseCase {field}, in the primary constructor");
        output.WriteLine("  the action");

        foreach (string line in Action(spec))
        {
            output.WriteLine(line.Length == 0 ? string.Empty : "      " + line);
        }

        if (!spec.IsCommand)
        {
            string response = spec.Dto![..^"Dto".Length] + "Response";

            output.WriteLine($"  and Src/Presentation/{p}.Api/Features/{f}/Contracts/Responses/{response}.cs, with the members a client reads:");
            output.WriteLine($"      namespace {p}.Api.Features.{f}.Contracts.Responses;");
            output.WriteLine(string.Empty);
            output.WriteLine($"      /// <summary>The wire shape of {Separated(response[..^"Response".Length], ' ')}.</summary>");
            output.WriteLine($"      public sealed record {response};");
        }

        output.WriteLine();
        output.WriteLine("Then rewrite the OpenAPI reference the contract test compares:  dotnet run Tools/Tasks.cs openapi");

        string page = $"docs/features/{Kebab(f)}.md";

        if (File.Exists(Path.Combine(root, page)))
        {
            output.WriteLine($"and add the route to {page}.");
        }
    }

    private static List<string> Action(Spec spec)
    {
        string u = spec.Name;
        string field = Lower(u);
        string remainder = spec.Aggregate is { Length: > 0 } aggregate && u.Contains(aggregate, StringComparison.Ordinal) && u.Length > aggregate.Length
            ? u.Replace(aggregate, string.Empty, StringComparison.Ordinal)
            : u;
        string method = remainder;
        string segment = Kebab(!spec.IsCommand && remainder.StartsWith("Get", StringComparison.Ordinal) && remainder.Length > 3 ? remainder[3..] : remainder);
        string id = spec.Aggregate is null ? string.Empty : Lower(spec.Aggregate) + "Id";
        string route = spec.HasInput ? $"{{{id}:guid}}/{segment}" : segment;
        string parameters = spec.HasInput ? $"Guid {id}, CancellationToken cancellationToken" : "CancellationToken cancellationToken";

        var lines = new List<string>
        {
            $"/// <summary>{Sentence(u)}.</summary>",
            $"[Http{(spec.IsCommand ? "Post" : "Get")}(\"{route}\")]",
        };

        if (spec.IsCommand)
        {
            lines.Add("[ProducesResponseType(StatusCodes.Status204NoContent)]");

            if (spec.HasInput)
            {
                lines.AddRange(
                [
                    "[ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]",
                    "[ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]",
                    "[ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]",
                    "[ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]",
                    "[ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]",
                    $"public async Task<ActionResult> {method}({parameters})",
                    "{",
                    "    if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)",
                    "    {",
                    "        return refusal;",
                    "    }",
                    string.Empty,
                    $"    var outcome = await {field}.ExecuteAsync(new {spec.Input}({id}, precondition), cancellationToken);",
                    string.Empty,
                    "    return NoContentOrProblem(RequiringExistence(requiresExistence, outcome));",
                    "}",
                ]);
            }
            else
            {
                lines.AddRange(
                [
                    $"public async Task<ActionResult> {method}({parameters}) =>",
                    $"    NoContentOrProblem(await {field}.ExecuteAsync(cancellationToken));",
                ]);
            }

            return lines;
        }

        string response = spec.Dto![..^"Dto".Length] + "Response";
        string call = spec.HasInput ? $"new {spec.Input}({id}), cancellationToken" : "cancellationToken";

        lines.Add($"[ProducesResponseType(StatusCodes.Status200OK, Type = typeof({response}))]");

        if (spec.HasInput)
        {
            lines.Add("[ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]");
            lines.Add("[ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]");
        }

        lines.AddRange(
        [
            $"public async Task<ActionResult<{response}>> {method}({parameters}) =>",
            $"    OkOrProblem((await {field}.ExecuteAsync({call})).Map(_ => new {response}()));",
        ]);

        return lines;
    }

    private static string Sentence(string pascal)
    {
        string spoken = Separated(pascal, ' ');

        return char.ToUpperInvariant(spoken[0]) + spoken[1..];
    }

    private static int SelfTest()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "newusecase-selftest-" + Guid.CreateVersion7().ToString("N"));
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
            TreeCases(scratch, Expect);
            ShapeCases(scratch, Expect);
            DtoCases(scratch, Expect);
            ManifestCases(scratch, Expect);
            RollbackCases(scratch, Expect);
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

    private static string NewTree(string scratch, params string[] repositories)
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        string[] directories =
        [
            "Src/Domain/Acme.Domain/Features/Invoices/Repositories",
            "Src/Application/Acme.Application/Features/Invoices/UseCases/Commands/CreateInvoice",
            "Src/Application/Acme.Application/Features/Notes/Services",
            "Src/Presentation/Acme.Api/Features/Invoices/Controllers",
            "Tests/Application/Acme.Application.UnitTests",
        ];

        foreach (string directory in directories)
        {
            Directory.CreateDirectory(Path.Combine(root, directory));
        }

        foreach (string repository in repositories.Length == 0 ? ["Invoice"] : repositories)
        {
            File.WriteAllText(
                Path.Combine(root, $"Src/Domain/Acme.Domain/Features/Invoices/Repositories/I{repository}Repository.cs"),
                $"public interface I{repository}Repository;\n");
        }

        File.WriteAllText(Path.Combine(root, "Src/Presentation/Acme.Api/Features/Invoices/Controllers/InvoicesController.cs"), "the controller a person edits\n");

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
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : "<directory>",
                StringComparer.Ordinal);

    private static bool Same(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);

    private static void ExpectRefusal(string root, string[] args, string fragment, Action<bool, string> expect)
    {
        var before = Snapshot(root);
        var result = Invoke(root, args);
        string label = string.Join(' ', args);

        expect(result.Code == 1, $"[{label}] exited {result.Code}, not 1.");
        expect(result.Error.Contains(fragment, StringComparison.Ordinal), $"[{label}] did not say '{fragment}': {result.Error.Split('\n')[0]}");
        expect(result.Error.Contains("Nothing was written.", StringComparison.Ordinal), $"[{label}] did not say nothing was written.");
        expect(Same(before, Snapshot(root)), $"[{label}] changed the tree.");
    }

    private static void ParserCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        (string[] Args, string Fragment)[] refusals =
        [
            (["Invoices", "ApproveInvoice"], "--kind is required"),
            (["Invoices", "ApproveInvoice", "--kind", "event"], "--kind 'event' is not a choice"),
            (["Invoices", "ApproveInvoice", "--kind"], "'--kind' needs a value"),
            (["Invoices", "ApproveInvoice", "--kind", "command", "--kind", "query"], "is given twice"),
            (["Invoices", "ApproveInvoice", "--kind", "command", "--input", "body"], "--input 'body' is not a choice"),
            (["Invoices", "ApproveInvoice", "--kind", "command", "--route", "x"], "'--route' is not an option"),
            (["Invoices", "--kind", "command"], "Expected two names"),
            (["Invoices", "ApproveInvoice", "Extra", "--kind", "command"], "Expected two names"),
            (["invoices", "ApproveInvoice", "--kind", "command"], "is not a feature folder"),
            (["Invoices", "approveInvoice", "--kind", "command"], "is not a use case name"),
            (["Invoices", "Invoices", "--kind", "command"], "are both 'Invoices'"),
            (["Invoices", "Queries", "--kind", "query"], "already a segment"),
            (["Invoices", "ApproveInvoiceUseCase", "--kind", "command"], "ends in 'UseCase'"),
            (["Invoices", "ApproveInvoiceCommand", "--kind", "command"], "ends in 'Command'"),
            (["Invoices", "Event", "--kind", "command"], "C# keyword"),
            (["Invoices", new string('A', 61), "--kind", "command"], "longer than 60"),
        ];

        foreach (var (args, fragment) in refusals)
        {
            ExpectRefusal(root, args, fragment, expect);
        }

        var several = Invoke(root, "invoices", "approve", "--kind", "event", "--input", "body");

        expect(
            several.Error.Split('\n').Count(line => line.StartsWith("error:", StringComparison.Ordinal)) >= 4,
            "Several mistakes in one command were not all reported.");
    }

    private static void TreeCases(string scratch, Action<bool, string> expect)
    {
        string stranger = Path.Combine(scratch, "stranger");
        Directory.CreateDirectory(stranger);

        var elsewhere = Invoke(stranger, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(elsewhere.Code == 1 && elsewhere.Error.Contains("<Prefix>.Domain", StringComparison.Ordinal), "A tree without a domain project was not refused for it.");
        expect(!Directory.EnumerateFileSystemEntries(stranger).Any(), "A refused tree was written to.");

        string root = NewTree(scratch);

        ExpectRefusal(root, ["Orders", "ApproveOrder", "--kind", "command"], "No feature called Orders", expect);
        ExpectRefusal(root, ["Orders", "ApproveOrder", "--kind", "command"], "The features here are Invoices, Notes.", expect);
        ExpectRefusal(root, ["Notes", "ArchiveNote", "--kind", "command"], "it is a service over a store", expect);
        ExpectRefusal(root, ["Invoices", "CreateInvoice", "--kind", "command"], "already has a use case called CreateInvoice", expect);
        ExpectRefusal(root, ["Invoices", "CreateInvoice", "--kind", "query"], "already has a use case called CreateInvoice", expect);
        ExpectRefusal(root, ["Invoices", "Invoice", "--kind", "query"], "the aggregate's own name", expect);

        string none = NewTree(scratch, "Unused");
        File.Delete(Path.Combine(none, "Src/Domain/Acme.Domain/Features/Invoices/Repositories/IUnusedRepository.cs"));

        ExpectRefusal(none, ["Invoices", "ApproveInvoice", "--kind", "command"], "declares no repository", expect);
        expect(Invoke(none, "Invoices", "ApproveInvoice", "--kind", "command", "--input", "none").Code == 0, "--input none was refused where no repository names the aggregate.");

        string two = NewTree(scratch, "Invoice", "CreditNote");

        ExpectRefusal(two, ["Invoices", "ApproveInvoice", "--kind", "command"], "several (CreditNote, Invoice)", expect);

        string bare = NewTree(scratch);
        Directory.Delete(Path.Combine(bare, "Src/Application/Acme.Application/Features"), recursive: true);

        ExpectRefusal(bare, ["Invoices", "ApproveInvoice", "--kind", "command"], "This tree has no feature yet", expect);

        string blocked = NewTree(scratch);
        string inTheWay = Path.Combine(blocked, "Src/Application/Acme.Application/Features/Invoices/Model/Dtos/InvoiceTotalsDto.cs");
        string test = Path.Combine(blocked, "Tests/Application/Acme.Application.UnitTests/Features/Invoices/UseCases/Commands/ApproveInvoice/ApproveInvoiceUseCaseTests.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(inTheWay)!);
        File.WriteAllText(inTheWay, "somebody's own dto\n");
        Directory.CreateDirectory(Path.GetDirectoryName(test)!);
        File.WriteAllText(test, "somebody's own test\n");

        ExpectRefusal(blocked, ["Invoices", "ApproveInvoice", "--kind", "command"], "none is ever overwritten", expect);
        expect(File.ReadAllText(test) == "somebody's own test\n", "An existing file was overwritten.");
    }

    private static void ShapeCases(string scratch, Action<bool, string> expect)
    {
        string useCases = "Src/Application/Acme.Application/Features/Invoices/UseCases";
        string tests = "Tests/Application/Acme.Application.UnitTests/Features/Invoices/UseCases";
        string dtos = "Src/Application/Acme.Application/Features/Invoices/Model/Dtos";

        (string[] Args, string[] Files)[] cases =
        [
            (["ApproveInvoice", "--kind", "command"],
                [$"{useCases}/Commands/ApproveInvoice/ApproveInvoiceCommand.cs", $"{useCases}/Commands/ApproveInvoice/ApproveInvoiceCommandValidator.cs",
                 $"{useCases}/Commands/ApproveInvoice/IApproveInvoiceUseCase.cs", $"{useCases}/Commands/ApproveInvoice/ApproveInvoiceUseCase.cs",
                 $"{tests}/Commands/ApproveInvoice/ApproveInvoiceUseCaseTests.cs"]),
            (["CloseOverdueInvoices", "--kind", "command", "--input", "none"],
                [$"{useCases}/Commands/CloseOverdueInvoices/ICloseOverdueInvoicesUseCase.cs", $"{useCases}/Commands/CloseOverdueInvoices/CloseOverdueInvoicesUseCase.cs"]),
            (["GetInvoiceTotals", "--kind", "query"],
                [$"{useCases}/Queries/GetInvoiceTotals/GetInvoiceTotalsQuery.cs", $"{useCases}/Queries/GetInvoiceTotals/GetInvoiceTotalsQueryValidator.cs",
                 $"{useCases}/Queries/GetInvoiceTotals/IGetInvoiceTotalsUseCase.cs", $"{useCases}/Queries/GetInvoiceTotals/GetInvoiceTotalsUseCase.cs",
                 $"{dtos}/InvoiceTotalsDto.cs", $"{tests}/Queries/GetInvoiceTotals/GetInvoiceTotalsUseCaseTests.cs"]),
            (["SummariseRevenue", "--kind", "query", "--input=none"],
                [$"{useCases}/Queries/SummariseRevenue/ISummariseRevenueUseCase.cs", $"{useCases}/Queries/SummariseRevenue/SummariseRevenueUseCase.cs",
                 $"{dtos}/SummariseRevenueDto.cs"]),
        ];

        foreach (var (args, files) in cases)
        {
            string root = NewTree(scratch);
            var before = Snapshot(root);
            var result = Invoke(root, ["Invoices", .. args]);
            string label = string.Join(' ', args);

            expect(result.Code == 0, $"[{label}] was refused: {result.Error}");

            var after = Snapshot(root);
            string[] created = [.. after.Keys.Except(before.Keys, StringComparer.Ordinal).Where(key => after[key] != "<directory>").Order(StringComparer.Ordinal)];

            expect(created.SequenceEqual(files.Order(StringComparer.Ordinal)), $"[{label}] wrote {string.Join(", ", created)}.");
            expect(before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value), $"[{label}] changed a file it does not own.");
            expect(!Directory.Exists(Path.Combine(root, ManifestFolder)), $"[{label}] wrote a manifest where the feature had none.");
            expect(result.Output.Contains("dotnet run Tools/Tasks.cs openapi", StringComparison.Ordinal), $"[{label}] did not print the OpenAPI step.");
            expect(result.Output.Contains("Src/Presentation/Acme.Api/Features/Invoices/Controllers/InvoicesController.cs", StringComparison.Ordinal), $"[{label}] did not name the feature's controller.");

            foreach (string file in created)
            {
                foreach (string problem in FileProblems(root, file))
                {
                    expect(false, $"[{label}] {problem}");
                }
            }
        }

        string untested = NewTree(scratch);
        Directory.Delete(Path.Combine(untested, "Tests/Application/Acme.Application.UnitTests"), recursive: true);

        var skipped = Invoke(untested, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(skipped.Code == 0 && skipped.Output.Contains("tests were skipped", StringComparison.Ordinal), "A tree without the application tests was not told its tests were skipped.");
        expect(!Directory.Exists(Path.Combine(untested, "Tests/Application/Acme.Application.UnitTests")), "Tests were written into a project that does not exist.");
    }

    private static void DtoCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string dto = Path.Combine(root, "Src/Application/Acme.Application/Features/Invoices/Model/Dtos/InvoiceDto.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(dto)!);
        File.WriteAllText(dto, "public sealed record InvoiceDto(Guid Id);\n");

        var result = Invoke(root, "Invoices", "GetInvoice", "--kind", "query");
        string useCase = Path.Combine(root, "Src/Application/Acme.Application/Features/Invoices/UseCases/Queries/GetInvoice/IGetInvoiceUseCase.cs");

        expect(result.Code == 0, $"A query answering an existing DTO was refused: {result.Error}");
        expect(File.ReadAllText(dto) == "public sealed record InvoiceDto(Guid Id);\n", "The existing DTO was overwritten.");
        expect(File.Exists(useCase) && File.ReadAllText(useCase).Contains("Result<InvoiceDto>", StringComparison.Ordinal), "The query does not answer the existing DTO.");
        expect(result.Output.Contains("already declares", StringComparison.Ordinal), "The reuse of an existing DTO was not reported.");
    }

    private static void ManifestCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string manifest = Path.Combine(root, ManifestFolder, "Invoices.json");
        const string generated = "Src/Application/Acme.Application/Features/Invoices/UseCases/Commands/CreateInvoice/CreateInvoiceUseCase.cs";
        string page = $"docs/features/{Kebab("Invoices")}.md";
        string original =
            "{\n  \"schemaVersion\": 1,\n  \"generator\": \"Tools/NewFeature.cs\",\n  \"feature\": \"Invoices\",\n  \"type\": \"Invoice\",\n"
            + "  \"options\": {\n    \"access\": \"shared\"\n  },\n"
            + $"  \"files\": [\n    {{\n      \"path\": \"{generated}\",\n      \"sha256\": \"{new string('a', 64)}\"\n    }}\n  ],\n"
            + $"  \"page\": \"{page}\"\n}}\n";

        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, original);

        var before = Snapshot(root);
        var first = Invoke(root, "Invoices", "ApproveInvoice", "--kind", "command");
        var second = Invoke(root, "Invoices", "GetInvoiceTotals", "--kind", "query");

        expect(first.Code == 0 && second.Code == 0, $"A use case was refused in a feature with a manifest: {first.Error}{second.Error}");
        expect(first.Output.Contains("remove-feature deletes them", StringComparison.Ordinal), "The manifest record was not reported.");

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var top = document.RootElement;
        var listed = top.GetProperty("files").EnumerateArray()
            .Select(file => (Path: file.GetProperty("path").GetString()!, Sha256: file.GetProperty("sha256").GetString()!))
            .ToList();

        var after = Snapshot(root);
        string[] created = [.. after.Keys.Except(before.Keys, StringComparer.Ordinal).Where(key => after[key] != "<directory>")];

        expect(created.Length == 11, $"Two use cases wrote {created.Length} files, not 11.");
        expect(listed.Select(entry => entry.Path).SequenceEqual(listed.Select(entry => entry.Path).Order(StringComparer.Ordinal)), "The manifest's files are not in path order.");
        expect(listed.Count == created.Length + 1 && listed.Any(entry => entry.Path == generated && entry.Sha256 == new string('a', 64)), "The manifest lost the generator's own entry or gained a stranger.");

        foreach (string key in created)
        {
            expect(listed.Any(entry => entry.Path == key && entry.Sha256 == after[key]), $"The manifest does not hold the hash of {key}.");
        }

        expect(
            top.GetProperty("generator").GetString() == "Tools/NewFeature.cs"
            && top.GetProperty("options").GetProperty("access").GetString() == "shared"
            && top.GetProperty("page").GetString() == page,
            "The manifest's other members were not kept.");
        expect(File.ReadAllText(manifest).EndsWith("}\n", StringComparison.Ordinal) && !File.ReadAllText(manifest).Contains('\r', StringComparison.Ordinal), "The manifest does not end in one LF.");
        expect(!File.Exists(manifest + ".new"), "The staged manifest was left behind.");

        string broken = NewTree(scratch);
        string brokenManifest = Path.Combine(broken, ManifestFolder, "Invoices.json");

        Directory.CreateDirectory(Path.GetDirectoryName(brokenManifest)!);
        File.WriteAllText(brokenManifest, "{ not json");

        ExpectRefusal(broken, ["Invoices", "ApproveInvoice", "--kind", "command"], "cannot be read", expect);

        File.WriteAllText(brokenManifest, "{ \"feature\": \"Orders\", \"files\": [] }");

        ExpectRefusal(broken, ["Invoices", "ApproveInvoice", "--kind", "command"], "does not read as the manifest of Invoices", expect);
    }

    private static void RollbackCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        File.WriteAllText(Path.Combine(root, "Tests/Application/Acme.Application.UnitTests/Features"), "not a folder\n");

        var before = Snapshot(root);
        var result = Invoke(root, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(result.Code == 1 && result.Error.Contains("nothing was left behind", StringComparison.Ordinal), "A write that fails half way was not reported as rolled back.");
        expect(Same(before, Snapshot(root)), "A write that failed half way left files or folders behind.");
    }

    private static IEnumerable<string> FileProblems(string root, string relative)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, relative));

        if (bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF)
        {
            yield return $"{relative}: no UTF-8 BOM, which the formatting gate refuses.";
        }

        string text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        if (text.Contains('\r', StringComparison.Ordinal) || text.EndsWith("\n\n", StringComparison.Ordinal) || !text.EndsWith('\n'))
        {
            yield return $"{relative}: not LF endings closed by exactly one newline.";
        }

        if (TokenPattern.IsMatch(text))
        {
            yield return $"{relative}: a token was left unsubstituted: {TokenPattern.Match(text).Value}";
        }

        if (text.Contains("//", StringComparison.Ordinal) || text.Contains("/*", StringComparison.Ordinal))
        {
            yield return $"{relative}: a comment, where generated code carries none.";
        }

        string[] segments = relative.Split('/');
        int project = Array.FindIndex(segments, segment => segment.Contains('.', StringComparison.Ordinal));

        if (!text.Contains($"namespace {string.Join('.', segments[project..^1])};", StringComparison.Ordinal))
        {
            yield return $"{relative}: its namespace is not its project plus its folders.";
        }

        string stem = Path.GetFileNameWithoutExtension(relative);

        if (!Regex.IsMatch(text, $@"^public (?:sealed )?(?:class|interface|record) {stem}\b", RegexOptions.Multiline, TimeSpan.FromSeconds(2)))
        {
            yield return $"{relative}: does not declare the public '{stem}' it is named for.";
        }

        string[] usings = [.. text.Split('\n').TakeWhile(line => line.StartsWith("using ", StringComparison.Ordinal))];

        if (usings.FirstOrDefault(line => !Regex.IsMatch(line, @"^using [A-Z][\w.]*;$", RegexOptions.None, TimeSpan.FromSeconds(1))) is { } malformed)
        {
            yield return $"{relative}: '{malformed}' names no namespace.";
        }

        if (!usings.SequenceEqual(usings.OrderBy(line => line.TrimEnd(';'), StringComparer.OrdinalIgnoreCase)))
        {
            yield return $"{relative}: its usings are not in order.";
        }
    }
}

internal static class Templates
{
    internal const string CommandRequest = """
        using %P%.Application.Core.Common.Concurrency;

        namespace %P%.Application.Features.%F%.UseCases.Commands.%U%;

        public sealed record %U%Command(Guid %A%Id, VersionPrecondition? Precondition = null);
        """;

    internal const string QueryRequest = """
        namespace %P%.Application.Features.%F%.UseCases.Queries.%U%;

        public sealed record %U%Query(Guid %A%Id);
        """;

    internal const string Validator = """
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.%K%.%U%;

        public sealed class %In%Validator : AbstractValidator<%In%>
        {
            public %In%Validator() =>
                RuleFor(%in% => %in%.%A%Id)
                    .NotEmpty().WithMessage("%Subject% id is required.");
        }
        """;

    internal const string CommandPort = """
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.UseCases;

        namespace %P%.Application.Features.%F%.UseCases.Commands.%U%;

        public interface I%U%UseCase : %Contract%;
        """;

    internal const string QueryPort = """
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.UseCases;
        using %P%.Application.Features.%F%.Model.Dtos;

        namespace %P%.Application.Features.%F%.UseCases.Queries.%U%;

        public interface I%U%UseCase : %Contract%;
        """;

    internal const string UseCaseWithInput = """
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Core.Common.Validation;
        using %Dtos%;
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.%K%.%U%;

        public sealed class %U%UseCase(IValidator<%In%> validator) : I%U%UseCase
        {
            public async Task<%Out%> ExecuteAsync(
                %In% %in%,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(%in%);

                var validation = await validator.EnsureValidAsync(%in%, cancellationToken);

                if (validation.IsFailure)
                {
                    return %Refusal%;
                }

                throw new NotImplementedException(
                    %Unwritten%);
            }
        }
        """;

    internal const string UseCaseWithoutInput = """
        using %P%.Application.Core.Common.Results;
        using %Dtos%;

        namespace %P%.Application.Features.%F%.UseCases.%K%.%U%;

        public sealed class %U%UseCase : I%U%UseCase
        {
            public Task<%Out%> ExecuteAsync(CancellationToken cancellationToken = default) =>
                throw new NotImplementedException(
                    %Unwritten%);
        }
        """;

    internal const string Dto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %D%;
        """;

    internal const string UseCaseTests = """
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.UseCases.%K%.%U%;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.UseCases.%K%.%U%;

        public sealed class %U%UseCaseTests
        {
            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

            [Fact]
            public async Task AnEmpty%A%Id_IsRefusedBeforeTheUseCaseActs()
            {
                var result = await new %U%UseCase(new %In%Validator())
                    .ExecuteAsync(new %In%(Guid.Empty), TestToken);

                result.IsFailure.ShouldBeTrue();
                result.Error!.Type.ShouldBe(ErrorType.Validation);
            }

            [Fact]
            public async Task AReal%A%Id_PassesValidation() =>
                (await new %In%Validator().ValidateAsync(new %In%(Guid.CreateVersion7()), TestToken))
                    .IsValid.ShouldBeTrue();
        }
        """;
}
