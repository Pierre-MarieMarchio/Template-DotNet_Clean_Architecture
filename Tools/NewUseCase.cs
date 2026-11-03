#:property UseAppHost=false

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class NewUseCase
{
    private const string _usage =
        "Usage: dotnet run Tools/NewUseCase.cs <Features> <UseCase> --kind command|query\n"
        + "           [--input id|none|\"[id, ]Name:Type, Other:Type\"]\n"
        + "           [--root <path>] [--dry-run]\n"
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
        + "  --root      the tree to write into, default the current directory.\n"
        + "  --dry-run   print the files and the edit, and write and run nothing.\n"
        + "  Where the API serves the feature, it also adds the action to the feature's controller, writes\n"
        + "  the request and response contracts and the route's row, every edit or none, then rewrites the\n"
        + "  OpenAPI reference and the clients written from it.\n"
        + "       dotnet run Tools/NewUseCase.cs --self-test";

    private static readonly string[] _optionNames = ["--kind", "--input", "--root"];

    private static readonly string[] _recordMembers =
        ["Deconstruct", "EqualityContract", "Equals", "GetHashCode", "GetType", "MemberwiseClone", "PrintMembers", "ToString"];

    private static readonly string[] _predefinedTypes =
        ["bool", "byte", "char", "decimal", "double", "float", "int", "long", "object", "sbyte", "short", "string", "uint", "ulong", "ushort"];

    private static readonly Dictionary<string, KnownType> _knownTypes = new(StringComparer.Ordinal)
    {
        ["string"] = new(null, "string.Empty"),
        ["int"] = new("1", "0"),
        ["long"] = new("1L", "0L"),
        ["decimal"] = new("1m", "0m"),
        ["bool"] = new("true", null),
        ["Guid"] = new("Guid.CreateVersion7()", "Guid.Empty"),
        ["DateTimeOffset"] = new("new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)", "default"),
        ["DateOnly"] = new("new DateOnly(2026, 1, 1)", "default"),
    };

    private static readonly string[] _reservedNames =
    [
        "Access", "Api", "Application", "Auth", "Commands", "Common", "Contracts", "Controllers", "Core",
        "Dtos", "Domain", "Entities", "Errors", "Features", "Infrastructure", "Model", "Models",
        "Persistence", "Policies", "Ports", "Presentation", "Queries", "Rows", "Services", "UseCases",
    ];

    private static readonly string[] _reservedSuffixes = ["UseCase", "Command", "Query", "Handler", "Validator", "Dto"];

    private static readonly Regex _tokenPattern = new("%([A-Za-z]+)%", RegexOptions.None, TimeSpan.FromSeconds(2));

    private sealed record Emission(string Path, string Content);

    private sealed record Request(string? Features, string? Name, string? Kind, string? Input, string Root, bool DryRun);

    private sealed record KnownType(string? Valid, string? Empty);

    private sealed record Parameter(string Name, string Type, string Required)
    {
        private bool IsNullable => Type.EndsWith('?');

        private KnownType? Known => _knownTypes.GetValueOrDefault(IsNullable ? Type[..^1] : Type);

        internal bool IsKnown => Known is not null;

        internal string Valid => Known switch
        {
            null => "default!",
            { Valid: null } => $"\"{Separated(Name, ' ')}\"",
            { Valid: { } valid } => valid,
        };

        internal string? Empty => IsNullable ? null : Known?.Empty;

        internal string Argument => CSharpNames.Keywords.Contains(CSharpNames.Lower(Name), StringComparer.Ordinal) ? "@" + CSharpNames.Lower(Name) : CSharpNames.Lower(Name);
    }

    private sealed record Spec(
        string Prefix,
        string Features,
        string Name,
        string Kind,
        string? Aggregate,
        IReadOnlyList<Parameter> Inputs,
        string? Dto,
        bool DtoExists,
        string? Manifest)
    {
        internal bool IsCommand => Kind == "command";

        internal bool HasId => Aggregate is not null;

        internal string Folder => IsCommand ? "Commands" : "Queries";

        internal string Input => Name + (IsCommand ? "Command" : "Query");

        internal string Request => Name + "Request";

        internal string Namespace => $"{Prefix}.Application.Features.{Features}.UseCases.{Folder}.{Name}";

        internal IReadOnlyList<Parameter> Parameters =>
            Aggregate is null ? Inputs : [new Parameter($"{Aggregate}Id", "Guid", $"{Subject(Aggregate)} id is required."), .. Inputs];

        internal Parameter[] Ruled => [.. Parameters.Where(parameter => parameter.Empty is not null)];

        internal bool HasValidator => Ruled.Length > 0;

        internal bool HasRequest => Inputs.Count > 0;

        internal Parameter[] Unknown => [.. Inputs.Where(parameter => !parameter.IsKnown)];
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

        return Run(args, Console.Out, Console.Error, TextEdits.Execute);
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

        var plan = Plan(spec, root, out string? skipped);
        var editor = new TextEdits.Editor(root);
        List<Emission> contracts = Http(editor, root, spec);
        string[] existing = [.. plan.Concat(contracts).Select(emission => emission.Path).Where(path => File.Exists(Path.Combine(root, path)))];

        if (existing.Length > 0)
        {
            problems.Add(
                $"{existing.Length} of the files to write already exist, and none is ever overwritten:\n"
                + string.Join("\n", existing.Select(path => $"  {path}")));

            return Refuse(error, problems);
        }

        if (editor.Failures.Count > 0)
        {
            Report(output, root, spec, plan, skipped, Mode.Refused);

            foreach (string failure in editor.Failures)
            {
                error.WriteLine($"error: {failure}");
            }

            error.WriteLine("Nothing was written: every edit is made or none is. Put back the line the failed edit is placed beside, or make the edit above by hand.");

            return 1;
        }

        if (request.DryRun)
        {
            Report(output, root, spec, plan, skipped, Mode.DryRun);
            output.WriteLine();
            output.WriteLine("--dry-run: nothing was written and nothing was run.");

            return 0;
        }

        var replaced = TextEdits.Replacements(editor).ToList();

        if (spec.Manifest is not null)
        {
            replaced.Add(ManifestWith(root, spec.Features, [.. plan, .. contracts], editor));
        }

        try
        {
            TextEdits.Commit(root, [.. plan.Concat(contracts).Select(emission => (emission.Path, Bytes(emission)))], replaced);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"error: nothing was left behind, but the write failed: {exception.Message}");

            return 1;
        }

        Report(output, root, spec, plan, skipped, Mode.Applied);

        return contracts.Count == 0 && editor.Applied.Count == 0
            || TextEdits.RunAll(TextEdits.ContractSteps(root, FolderOf(spec.Prefix, "Client"), built: false), run, output, error, "the use case's files, its action, its contracts and its route's row")
                ? 0
                : 1;
    }

    private enum Mode
    {
        Applied,
        DryRun,
        Refused,
    }

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
                $"Expected two names, <Features> and <UseCase>, and found {positional.Count}. "
                + "The feature's folder, then the operation: Invoices ApproveInvoice.");
        }

        string? Option(string name) => options.TryGetValue(name, out string? value) ? value : null;

        return new Request(
            positional.Count > 0 ? positional[0] : null,
            positional.Count > 1 ? positional[1] : null,
            Option("--kind"),
            Option("--input"),
            Option("--root") ?? ".",
            dryRun);
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

        var (hasId, inputs) = ReadInputs(request.Input ?? "id", problems);

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

        string applicationFeatures = Path.Combine(root, "Src", "Application", FolderOf(prefix, "Application"), "Features");
        string application = Path.Combine(applicationFeatures, features);

        if (!Directory.Exists(application))
        {
            string[] known = Directory.Exists(applicationFeatures)
                ? [.. Directory.EnumerateDirectories(applicationFeatures).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
                : [];

            problems.Add(
                $"No feature called {features}: 'Src/Application/{FolderOf(prefix, "Application")}/Features/{features}' does not exist. "
                + (known.Length == 0
                    ? "This tree has no feature yet; write one first, as docs/guides/adding-a-feature.md says."
                    : $"The features here are {string.Join(", ", known)}."));

            return null;
        }

        string domain = Path.Combine(root, "Src", "Domain", FolderOf(prefix, "Domain"), "Features", features);

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

        string repositories = Path.Combine(domain, "Repositories");
        string[] found = Directory.Exists(repositories)
            ? [.. Directory.EnumerateFiles(repositories, "I*Repository.cs")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Select(file => file[1..^"Repository".Length])
                .Where(type => CSharpNames.PascalCase.IsMatch(type))
                .Order(StringComparer.Ordinal)]
            : [];

        if (hasId)
        {
            if (found.Length != 1)
            {
                problems.Add(
                    found.Length == 0
                        ? $"id in --input carries the id of the feature's aggregate, and {features} declares no repository "
                            + "under Repositories/ to name it. Leave id out, or pass the id as an input of its own, e.g. "
                            + "--input \"InvoiceId:Guid\"."
                        : $"id in --input carries the id of the feature's aggregate, and {features} declares several "
                            + $"({string.Join(", ", found)}), so which one is ambiguous. Leave id out, or pass the id as an "
                            + "input of its own, e.g. --input \"InvoiceId:Guid\".");

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

        string message = name + (kind == "command" ? "Command" : "Query");
        string[] taken =
        [
            .. aggregate is null ? [] : new[] { aggregate + "Id" },
            .. aggregate is not null && kind == "command" ? new[] { "Precondition" } : [],
            message,
            name + "Request",
        ];

        if (inputs.FirstOrDefault(input => taken.Contains(input.Name, StringComparer.Ordinal)) is { } clash)
        {
            problems.Add(
                $"The input {clash.Name} would clash with a name the generated types already hold ({string.Join(", ", taken)}). "
                + "Name it otherwise.");

            return null;
        }

        string? dto = null;
        bool dtoExists = false;

        if (kind == "query")
        {
            dto = (name.StartsWith("Get", StringComparison.Ordinal) && name.Length > 3 && char.IsUpper(name[3]) ? name[3..] : name) + "Dto";
            dtoExists = File.Exists(Path.Combine(application, "Model", "Dtos", dto + ".cs"));
        }

        var manifest = FeatureManifest.Read(root, features, out string? unread);

        if (unread is not null)
        {
            problems.Add($"'{FeatureManifest.PathOf(features)}' {unread}, so the files written could not be recorded in it. Repair or delete it first.");

            return null;
        }

        return new Spec(prefix, features, name, kind, aggregate, inputs, dto, dtoExists, manifest?.Path);
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

    private static (bool HasId, List<Parameter> Inputs) ReadInputs(string input, List<string> problems)
    {
        var inputs = new List<Parameter>();
        bool hasId = false;

        if (input == "none")
        {
            return (false, inputs);
        }

        string[] entries = [.. TopLevelEntries(input).Select(entry => entry.Trim())];

        for (int index = 0; index < entries.Length; index++)
        {
            string entry = entries[index];
            int colon = entry.IndexOf(':', StringComparison.Ordinal);

            if (entry.Length == 0)
            {
                problems.Add($"--input '{input}' holds an empty entry, a comma with nothing on one side of it.");
            }
            else if (entry == "id")
            {
                if (index > 0)
                {
                    problems.Add("id comes first in --input, once: it is the aggregate's id the route carries, e.g. --input \"id, Note:string\".");
                }

                hasId = true;
            }
            else if (entry == "none")
            {
                problems.Add("none stands alone in --input: it is a message without input.");
            }
            else if (colon < 0)
            {
                problems.Add($"'{entry}' in --input is neither id nor an input written Name:Type, e.g. MaxCount:int.");
            }
            else
            {
                string name = entry[..colon].Trim();
                string type = entry[(colon + 1)..].Trim();
                var (canonical, problem) = TypeSyntax.Read(type);
                int refusedBefore = problems.Count;

                if (!CSharpNames.PascalCase.IsMatch(name))
                {
                    problems.Add($"'{name}' in --input is not an input name: it is PascalCase, e.g. MaxCount.");
                }
                else if (_recordMembers.Contains(name, StringComparer.Ordinal))
                {
                    problems.Add($"'{name}' in --input is a member every record declares already. Name the input otherwise.");
                }

                if (problem is not null)
                {
                    problems.Add($"'{type}' in --input, the type of {name}, is not a C# type: {problem}.");
                }

                if (problems.Count == refusedBefore)
                {
                    inputs.Add(new Parameter(name, canonical!, $"{Sentence(name)} is required."));
                }
            }
        }

        foreach (string twice in inputs.GroupBy(parameter => parameter.Name, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
        {
            problems.Add($"--input names {twice} more than once; an input is named once.");
        }

        return (hasId, inputs);
    }

    private static IEnumerable<string> TopLevelEntries(string input)
    {
        int depth = 0;
        int start = 0;

        for (int index = 0; index < input.Length; index++)
        {
            switch (input[index])
            {
                case '<' or '(' or '[':
                    depth++;
                    break;
                case '>' or ')' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    yield return input[start..index];
                    start = index + 1;
                    break;
            }
        }

        yield return input[start..];
    }

    private sealed class TypeSyntax
    {
        private static readonly Regex _token = new(@"\s*(?:([A-Za-z_][A-Za-z0-9_]*)|(::)|([<>,?\[\]().])|(\S))", RegexOptions.None, TimeSpan.FromSeconds(1));

        private readonly List<string> _tokens;

        private int _at;

        private TypeSyntax(List<string> tokens) => _tokens = tokens;

        private string? Peek => _at < _tokens.Count ? _tokens[_at] : null;

        internal static (string? Type, string? Problem) Read(string text)
        {
            var tokens = new List<string>();

            foreach (Match match in _token.Matches(text))
            {
                if (match.Groups[4].Success)
                {
                    return (null, $"'{match.Groups[4].Value}' has no place in a type name");
                }

                tokens.Add(match.Value.Trim());
            }

            var reader = new TypeSyntax(tokens);

            try
            {
                string type = reader.Type();

                return reader.Peek is { } extra
                    ? (null, $"'{extra}' follows a type that was already complete")
                    : (type, null);
            }
            catch (FormatException exception)
            {
                return (null, exception.Message);
            }
        }

        private string Next() =>
            _at < _tokens.Count ? _tokens[_at++] : throw new FormatException("it stops short of a complete type");

        private void Expect(string token)
        {
            if (Peek != token)
            {
                throw new FormatException(Peek is null ? $"it stops where '{token}' is due" : $"'{Peek}' stands where '{token}' is due");
            }

            _at++;
        }

        private string Identifier()
        {
            string token = Next();

            if (!char.IsAsciiLetter(token[0]) && token[0] != '_')
            {
                throw new FormatException($"'{token}' stands where a name is due");
            }

            return token;
        }

        private string Type()
        {
            string type = Peek == "(" ? Tuple() : Named();

            while (Peek is "?" or "[")
            {
                if (Next() == "?")
                {
                    type = type.EndsWith('?') ? throw new FormatException("a type is made nullable once") : type + "?";

                    continue;
                }

                int rank = 0;

                while (Peek == ",")
                {
                    _at++;
                    rank++;
                }

                Expect("]");
                type += "[" + new string(',', rank) + "]";
            }

            return type;
        }

        private string Tuple()
        {
            Expect("(");
            var elements = new List<string> { Element() };

            while (Peek == ",")
            {
                _at++;
                elements.Add(Element());
            }

            Expect(")");

            return elements.Count >= 2
                ? "(" + string.Join(", ", elements) + ")"
                : throw new FormatException("a tuple holds two elements or more");
        }

        private string Element()
        {
            string type = Type();

            if (Peek is not { } name || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            {
                return type;
            }

            return CSharpNames.Keywords.Contains(name, StringComparer.Ordinal)
                ? throw new FormatException($"'{name}' is a C# keyword, not an element name")
                : type + " " + Next();
        }

        private string Named()
        {
            string global = string.Empty;

            if (Peek == "global" && _at + 1 < _tokens.Count && _tokens[_at + 1] == "::")
            {
                _at += 2;
                global = "global::";
            }

            string first = Identifier();

            if (CSharpNames.Keywords.Contains(first, StringComparer.Ordinal))
            {
                return _predefinedTypes.Contains(first, StringComparer.Ordinal) && global.Length == 0 && Peek is not ("." or "<")
                    ? first
                    : throw new FormatException($"'{first}' is a C# keyword, not a type");
            }

            string named = global + first + TypeArguments();

            while (Peek == ".")
            {
                _at++;
                string segment = Identifier();

                named += "." + (CSharpNames.Keywords.Contains(segment, StringComparer.Ordinal) ? throw new FormatException($"'{segment}' is a C# keyword, not a type") : segment) + TypeArguments();
            }

            return named;
        }

        private string TypeArguments()
        {
            if (Peek != "<")
            {
                return string.Empty;
            }

            _at++;
            var arguments = new List<string> { Type() };

            while (Peek == ",")
            {
                _at++;
                arguments.Add(Type());
            }

            Expect(">");

            return "<" + string.Join(", ", arguments) + ">";
        }
    }

    private static string ProductPrefix(string root)
    {
        string projectFile = Path.Combine(root, "cleanarch.json");

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

    private static string FolderOf(string prefix, string project) =>
        string.Concat(prefix.Split('.').Select(SegmentInitials)) + "." + project;

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));

    private static bool IsVowel(char letter) => "aeiou".Contains(char.ToLowerInvariant(letter), StringComparison.Ordinal);

    private static string Kebab(string pascal) => Separated(pascal, '-');

    private static string Separated(string pascal, char separator) =>
        string.Concat(pascal.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? separator + char.ToLowerInvariant(character).ToString()
                : char.ToLowerInvariant(character).ToString()));

    private static string Subject(string pascal)
    {
        string spoken = Separated(pascal, ' ');

        return (IsVowel(spoken[0]) ? "An " : "A ") + spoken;
    }

    private static Dictionary<string, string> Tokens(Spec spec)
    {
        string result = spec.IsCommand ? "Result" : $"Result<{spec.Dto}>";
        string lambda = spec.IsCommand ? "command" : "query";

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["P"] = spec.Prefix,
            ["F"] = spec.Features,
            ["U"] = spec.Name,
            ["K"] = spec.Folder,
            ["In"] = spec.Input,
            ["in"] = lambda,
            ["D"] = spec.Dto ?? string.Empty,
            ["Dtos"] = spec.IsCommand ? string.Empty : $"{spec.Prefix}.Application.Features.{spec.Features}.Model.Dtos",
            ["Out"] = result,
            ["Kind"] = spec.IsCommand ? "Command" : "Query",
            ["Concurrency"] = spec.IsCommand && spec.HasId ? $"{spec.Prefix}.Application.Core.Common.Concurrency" : string.Empty,
            ["Message"] = Message(spec, result),
            ["Unwritten"] = Unwritten(spec),
            ["Rules"] = string.Join(
                "\n\n",
                spec.Ruled.Select(parameter =>
                    $"        RuleFor({lambda} => {lambda}.{parameter.Name})\n"
                    + $"            .NotEmpty().WithMessage(\"{parameter.Required}\");")),
            ["Refusals"] = string.Concat(
                spec.Ruled.Select(parameter =>
                    "\n"
                    + "    [Fact]\n"
                    + $"    public async Task AnEmpty{parameter.Name}_IsRefused() =>\n"
                    + $"        (await _validator.ValidateAsync(AWellFormed{(spec.IsCommand ? "Command" : "Query")}() with {{ {parameter.Name} = {parameter.Empty} }}, TestToken))\n"
                    + "            .IsValid.ShouldBeFalse();\n")),
            ["Values"] = string.Join(", ", spec.Parameters.Select(parameter => parameter.Valid)) is { Length: <= 60 } values
                ? values
                : "\n" + string.Join(",\n", spec.Parameters.Select(parameter => "            " + parameter.Valid)),
            ["Malformed"] = spec.Ruled.Length == 0 ? string.Empty : $"{spec.Ruled[0].Name} = {spec.Ruled[0].Empty}",
        };
    }

    private static string Message(Spec spec, string result)
    {
        string[] declared =
        [
            .. spec.Parameters.Select(parameter => $"{parameter.Type} {parameter.Name}"),
            .. spec.IsCommand && spec.HasId ? new[] { "VersionPrecondition? Precondition = null" } : [],
        ];

        return Record(spec.Input, declared, $" : I{(spec.IsCommand ? "Command" : "Query")}<{result}>");
    }

    private static string Unwritten(Spec spec) => (spec.IsCommand, spec.HasId) switch
    {
        (true, true) => $"\"{spec.Name} is not written yet: load the {spec.Aggregate}, call the method that holds the rule, then commit through IUnitOfWork.\"",
        (true, false) => $"\"{spec.Name} is not written yet: decide, then commit through IUnitOfWork.\"",
        _ => $"\"{spec.Name} is not written yet: read through the feature's queries port into {spec.Dto}.\"",
    };

    private static string Render(string template, Dictionary<string, string> tokens)
    {
        string text = _tokenPattern.Replace(
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

        string application = $"Src/Application/{FolderOf(spec.Prefix, "Application")}/Features/{spec.Features}";
        string folder = $"{application}/UseCases/{spec.Folder}/{spec.Name}";

        void Add(string path, string template) => emissions.Add(new Emission(path, Render(template, tokens)));

        Add($"{folder}/{spec.Name}.cs", Templates.UseCase);

        if (spec.HasValidator)
        {
            Add($"{folder}/{spec.Input}Validator.cs", Templates.Validator);
        }

        if (spec.Dto is not null && !spec.DtoExists)
        {
            Add($"{application}/Model/Dtos/{spec.Dto}.cs", spec.HasId ? Templates.DtoWithId : Templates.Dto);
        }

        skippedTests = null;

        if (spec.HasValidator)
        {
            string tests = $"Tests/Application/{FolderOf(spec.Prefix, "Application.UnitTests")}";
            string useCaseTests = $"{tests}/Features/{spec.Features}/UseCases/{spec.Folder}/{spec.Name}";

            if (Directory.Exists(Path.Combine(root, tests)))
            {
                Add($"{useCaseTests}/{spec.Input}ValidatorTests.cs", Templates.ValidatorTests);

                if (File.Exists(Path.Combine(root, tests, "TestDoubles", "ValidatedHandler.cs")))
                {
                    Add($"{useCaseTests}/{spec.Name}Tests.cs", Templates.HandlerTests);
                }
            }
            else
            {
                skippedTests = tests;
            }
        }

        return emissions;
    }

    private static byte[] Bytes(Emission emission) => TextEdits.Encode(emission.Path, emission.Content);

    private static (string Path, byte[] Original, byte[] Bytes) ManifestWith(string root, string features, List<Emission> written, TextEdits.Editor editor)
    {
        var manifest = FeatureManifest.Read(root, features, out _)!;

        foreach (var emission in written)
        {
            manifest.Record(emission.Path, Bytes(emission));
        }

        manifest.Record(editor);

        return manifest.Replacement;
    }

    private static void Report(TextWriter output, string root, Spec spec, List<Emission> plan, string? skipped, Mode mode)
    {
        string p = spec.Prefix;
        string f = spec.Features;
        string u = spec.Name;
        string kind = spec.IsCommand ? "Command" : "Query";
        string validatorTests = $"{spec.Input}ValidatorTests";
        bool testsWritten = plan.Any(emission => emission.Path.EndsWith($"/{validatorTests}.cs", StringComparison.Ordinal));
        bool handlerTested = plan.Any(emission => emission.Path.EndsWith($"/{u}Tests.cs", StringComparison.Ordinal));
        string api = $"Src/Presentation/{FolderOf(p, "Api")}";
        bool hasApi = Directory.Exists(Path.Combine(root, api));

        output.WriteLine($"{(mode == Mode.Applied ? "Wrote" : "Would write")} {plan.Count} files for the {spec.Kind} {u} in {f}:");

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
            : $"Recorded in {spec.Manifest} with their SHA-256, the contracts among them, and with each edit made outside the feature's files, so remove-feature deletes them and undoes those edits with the feature.");

        if (skipped is not null)
        {
            output.WriteLine($"Not written: '{skipped}' is not in this tree, so the use case's tests were skipped.");
        }
        else if (testsWritten && !handlerTested)
        {
            output.WriteLine($"Not written: {u}Tests.cs, whose UseCase() runs the handler behind its validator through TestDoubles/ValidatedHandler.cs, which this tree's application tests do not hold.");
        }

        output.WriteLine();
        output.WriteLine($"Nothing to register: AddFeature(\"{f}\") in ApplicationModule finds its validator by folder, and each host's generated mediator registers its handler.");
        output.WriteLine(spec.HasValidator
            ? $"Until its body is written, {spec.Input}Handler throws NotImplementedException; the pipeline answers what its validator refuses before the handler is reached."
            : $"Until its body is written, {spec.Input}Handler throws NotImplementedException.");

        if (!spec.HasValidator && spec.Parameters.Count > 0)
        {
            output.WriteLine(
                "No validator and no test were written: no input is one this command writes a rule for, a bool or a nullable accepting every value. "
                + $"Write {spec.Input}Validator beside {u}.cs once a value is refused, with {validatorTests} and {u}Tests beside the feature's other tests.");
        }

        if (spec.Unknown.Length > 0)
        {
            output.WriteLine();
            output.WriteLine("Written as given, of a type this command knows no rule and no test value for:");

            foreach (var parameter in spec.Unknown)
            {
                output.WriteLine($"  {parameter.Name}  {parameter.Type}");
            }

            output.WriteLine("For each, yours to write:");
            output.WriteLine(spec.HasValidator
                ? $"  - its rule in {spec.Input}Validator, when a value of it is refused, and that refusal's test in {validatorTests}"
                : "  - its rule, when a value of it is refused, in the validator above");

            if (testsWritten)
            {
                output.WriteLine($"  - a real value in AWellFormed{kind}() in {validatorTests}, which passes default! for it until then");
            }

            output.WriteLine(hasApi && spec.HasRequest
                ? $"  - a using of its namespace in {u}.cs and in {spec.Request}.cs, unless the type is imported already"
                : $"  - a using of its namespace in {u}.cs, unless the type is imported already");
        }

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

        output.WriteLine(handlerTested
            ? $"  - its dependencies, in the handler's primary constructor; in {u}Tests, each one a substitute field passed where UseCase() builds the handler, the one place that does"
            : "  - its dependencies, in the handler's primary constructor");
        output.WriteLine("  - one unit test per decision the body adds, each refusal with its control");

        //#if (api)
        if (hasApi)
        {
            output.WriteLine("  - an error code of its own: its row in docs/reference/api.md, which the error-code tests read");
        }

        //#endif
        string page = $"docs/features/{Kebab(f)}.md";

        if (!hasApi)
        {
            output.WriteLine();
            output.WriteLine($"To offer it, send {Construction(spec)} through ISender by hand where a host serves {f}, under:");

            foreach (string host in Hosts(root))
            {
                output.WriteLine($"  Src/Presentation/{host}/");
            }

            if (File.Exists(Path.Combine(root, page)))
            {
                output.WriteLine($"and add the operation to {page}.");
            }

            return;
        }

        string controller = $"{api}/Features/{f}/Controllers/{f}Controller.cs";
        string where = File.Exists(Path.Combine(root, controller))
            ? controller
            : $"a controller under {api}/Features/{f}/Controllers/";

        output.WriteLine();
        output.WriteLine(mode switch
        {
            Mode.Applied => $"To serve it over HTTP, the edit made in {where}:",
            Mode.DryRun => $"To serve it over HTTP, the edit a run without --dry-run makes in {where}:",
            _ => $"To serve it over HTTP, the edit by hand in {where}:",
        });
        output.WriteLine($"  the usings it lacks  {ControllerUsings(spec)[0]};");

        foreach (string space in ControllerUsings(spec).Skip(1))
        {
            output.WriteLine($"                       {space};");
        }

        output.WriteLine("  the parameter        ISender sender, in the primary constructor, unless it has one");
        output.WriteLine("  the action");

        foreach (string line in Action(spec))
        {
            output.WriteLine(line.Length == 0 ? string.Empty : "      " + line);
        }

        if (spec.HasRequest)
        {
            output.WriteLine($"  and {RequestPath(spec)}, with what a client sends:");

            foreach (string line in RequestContract(spec))
            {
                output.WriteLine(line.Length == 0 ? string.Empty : "      " + line);
            }
        }

        if (!spec.IsCommand)
        {
            output.WriteLine($"  and {ResponsePath(spec)}, with the members a client reads:");

            foreach (string line in ResponseContract(spec))
            {
                output.WriteLine(line.Length == 0 ? string.Empty : "      " + line);
            }
        }

        if (mode != Mode.Applied)
        {
            output.WriteLine();
            output.WriteLine(mode == Mode.DryRun
                ? "Then a run without --dry-run rewrites what the contract feeds, in this order:"
                : "Then rewrite what the contract feeds, once the edit is made:");

            foreach (var step in TextEdits.ContractSteps(root, FolderOf(p, "Client"), built: false))
            {
                output.WriteLine($"  {step.Display}");
            }
        }

        //#if (api)
        output.WriteLine(mode == Mode.Applied
            ? $"and its row, added to the table of {spec.Features} under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads, relative to that table's 'Under' prefix:"
            : $"and its row in the table of {spec.Features} under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads, relative to that table's 'Under' prefix:");
        output.WriteLine("  " + RouteRow(spec));
        //#endif

        if (File.Exists(Path.Combine(root, page)))
        {
            output.WriteLine($"and add the route to {page}.");
        }
    }

    private static string ApiFolder(Spec spec) => $"Src/Presentation/{FolderOf(spec.Prefix, "Api")}";

    private static string RequestPath(Spec spec) => $"{ApiFolder(spec)}/Features/{spec.Features}/Contracts/Requests/{spec.Request}.cs";

    private static string ResponseName(Spec spec) => spec.Dto![..^"Dto".Length] + "Response";

    private static string ResponsePath(Spec spec) => $"{ApiFolder(spec)}/Features/{spec.Features}/Contracts/Responses/{ResponseName(spec)}.cs";

    private static string[] ControllerUsings(Spec spec) =>
    [
        spec.Namespace,
        "Mediator",
        .. spec.HasRequest ? [$"{spec.Prefix}.Api.Features.{spec.Features}.Contracts.Requests"] : Array.Empty<string>(),
        .. spec.IsCommand
            ? Array.Empty<string>()
            : [$"{spec.Prefix}.Application.Core.Common.Results", $"{spec.Prefix}.Api.Features.{spec.Features}.Contracts.Responses"],
    ];

    private static string[] RequestContract(Spec spec)
    {
        string carrier = spec.IsCommand ? "body" : "query string";
        string summary = spec.HasId
            ? $"The {carrier} of {Separated(spec.Name, ' ')}; the {Separated(spec.Aggregate!, ' ')} id travels in the route."
            : $"The {carrier} of {Separated(spec.Name, ' ')}.";

        return
        [
            $"namespace {spec.Prefix}.Api.Features.{spec.Features}.Contracts.Requests;",
            string.Empty,
            $"/// <summary>{summary}</summary>",
            .. Record(spec.Request, spec.Inputs.Select(parameter => $"{parameter.Type} {parameter.Name}"), string.Empty).Split('\n'),
        ];
    }

    private static string[] ResponseContract(Spec spec) =>
    [
        $"namespace {spec.Prefix}.Api.Features.{spec.Features}.Contracts.Responses;",
        string.Empty,
        $"/// <summary>The wire shape of {Separated(ResponseName(spec)[..^"Response".Length], ' ')}.</summary>",
        $"public sealed record {ResponseName(spec)};",
    ];

    private static List<Emission> Http(TextEdits.Editor editor, string root, Spec spec)
    {
        if (!Directory.Exists(Path.Combine(root, ApiFolder(spec))))
        {
            return [];
        }

        string controller = $"{ApiFolder(spec)}/Features/{spec.Features}/Controllers/{spec.Features}Controller.cs";

        editor.Step = $"the action in {controller}";

        if (editor.Open(controller) is { } sheet)
        {
            Action(editor, sheet, spec);
        }

        //#if (api)
        editor.Step = "the route's row in docs/reference/api.md";
        RouteRow(editor, spec);
        //#endif

        return
        [
            .. spec.HasRequest ? [new Emission(RequestPath(spec), string.Join('\n', RequestContract(spec)) + "\n")] : Array.Empty<Emission>(),
            .. spec.IsCommand ? Array.Empty<Emission>() : [new Emission(ResponsePath(spec), string.Join('\n', ResponseContract(spec)) + "\n")],
        ];
    }

    private static void Action(TextEdits.Editor editor, TextEdits.Sheet sheet, Spec spec)
    {
        string declaration = $" {Remainder(spec)}(";

        if (TextEdits.Editor.Find(sheet, line => line.Contains("Task<ActionResult", StringComparison.Ordinal) && line.Contains(declaration, StringComparison.Ordinal)) >= 0)
        {
            editor.Kept.Add(editor.Step);

            return;
        }

        int type = TextEdits.Editor.Find(sheet, line => line.Contains(" class ", StringComparison.Ordinal) && line.Contains("Controller(", StringComparison.Ordinal));
        int parameters = type < 0 ? -1 : TextEdits.Editor.Find(sheet, line => line.Contains(") :", StringComparison.Ordinal), type);
        int closing = TextEdits.Editor.FindLast(sheet, line => line == "}", sheet.Lines.Count - 1);

        if (parameters < 0 || closing < parameters)
        {
            editor.Fail("it declares no controller with a primary constructor, and a closing brace at the start of a line, to take the action.");

            return;
        }

        if (TextEdits.Editor.Find(sheet, line => line.Contains("ISender ", StringComparison.Ordinal), type, parameters + 1) < 0)
        {
            TextEdits.Editor.Insert(sheet, type, before: false, ["    ISender sender,"]);
            closing++;
        }

        TextEdits.Editor.Insert(sheet, closing, before: true, ["", .. Action(spec).Select(line => line.Length == 0 ? line : "    " + line)]);

        foreach (string space in ControllerUsings(spec))
        {
            editor.Using(sheet.Path, space);
        }
    }

    //#if (api)
    private static void RouteRow(TextEdits.Editor editor, Spec spec)
    {
        const string page = "docs/reference/api.md";

        if (editor.Open(page) is not { } sheet)
        {
            return;
        }

        string row = RouteRow(spec);
        int routes = TextEdits.Editor.Find(sheet, line => line == "## Routes");
        int end = routes < 0 ? -1 : TextEdits.Editor.Find(sheet, line => line.StartsWith("## ", StringComparison.Ordinal), routes + 1);
        int section = routes < 0 ? -1 : TextEdits.Editor.Find(
            sheet,
            line => line.StartsWith("### ", StringComparison.Ordinal) && Letters(line[4..]) == Letters(spec.Features),
            routes + 1,
            end);
        int next = section < 0 ? -1 : TextEdits.Editor.Find(sheet, line => line.StartsWith('#'), section + 1);
        int rule = section < 0 ? -1 : TextEdits.Editor.Find(sheet, line => line.StartsWith("|---", StringComparison.Ordinal), section + 1, next);

        if (rule < 0)
        {
            editor.Fail($"{page} has no route table for {spec.Features} under '## Routes' to take the use case's row.");

            return;
        }

        int last = rule;

        while (last + 1 < sheet.Lines.Count && sheet.Lines[last + 1].StartsWith('|'))
        {
            last++;
        }

        if (TextEdits.Editor.Find(sheet, line => line == row, rule, last + 1) >= 0)
        {
            editor.Kept.Add(editor.Step);

            return;
        }

        TextEdits.Editor.Insert(sheet, last, before: false, [row]);
    }

    private static string Letters(string text) => new([.. text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    //#endif
    private static string Construction(Spec spec) =>
        $"new {spec.Input}({string.Join(", ", spec.Parameters.Select(parameter => parameter.Argument))})";

    private static string Record(string type, IEnumerable<string> declared, string implemented)
    {
        string[] members = [.. declared];

        return members.Length switch
        {
            0 => $"public sealed record {type}{implemented};",
            1 => $"public sealed record {type}({members[0]}){implemented};",
            _ => $"public sealed record {type}(\n{string.Join(",\n", members.Select(member => "    " + member))}){implemented};",
        };
    }

    private static IEnumerable<string> Hosts(string root)
    {
        string presentation = Path.Combine(root, "Src", "Presentation");

        return Directory.Exists(presentation)
            ? Directory.GetDirectories(presentation)
                .Where(folder => File.Exists(Path.Combine(folder, "Program.cs")))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
            : [];
    }

    private static string Remainder(Spec spec) =>
        spec.Aggregate is { Length: > 0 } aggregate && spec.Name.Contains(aggregate, StringComparison.Ordinal) && spec.Name.Length > aggregate.Length
            ? spec.Name.Replace(aggregate, string.Empty, StringComparison.Ordinal)
            : spec.Name;

    private static string IdName(Spec spec) => spec.Aggregate is null ? string.Empty : CSharpNames.Lower(spec.Aggregate) + "Id";

    private static string RouteTemplate(Spec spec)
    {
        string remainder = Remainder(spec);
        string segment = Kebab(!spec.IsCommand && remainder.StartsWith("Get", StringComparison.Ordinal) && remainder.Length > 3 ? remainder[3..] : remainder);

        return spec.HasId ? $"{{{IdName(spec)}:guid}}/{segment}" : segment;
    }

    private static string RouteRow(Spec spec)
    {
        string documented = "/" + RouteTemplate(spec).Replace(":guid", string.Empty, StringComparison.Ordinal);

        return $"| {(spec.IsCommand ? "POST" : "GET")} | `{documented}` | {(spec.IsCommand ? "204" : "200")} | |";
    }

    private static List<string> Action(Spec spec)
    {
        string method = Remainder(spec);
        string id = IdName(spec);
        string[] signature =
        [
            .. spec.HasId ? new[] { $"Guid {id}" } : [],
            .. spec.HasRequest ? new[] { $"[{(spec.IsCommand ? "FromBody" : "FromQuery")}] {spec.Request} request" } : [],
            "CancellationToken cancellationToken",
        ];
        string[] arguments =
        [
            .. spec.HasId ? new[] { id } : [],
            .. spec.Inputs.Select(parameter => "request." + parameter.Name),
            .. spec.IsCommand && spec.HasId ? new[] { "precondition" } : [],
        ];
        string message = $"new {spec.Input}({string.Join(", ", arguments)})";
        string response = spec.IsCommand ? string.Empty : spec.Dto![..^"Dto".Length] + "Response";
        string declaration = $"public async Task<ActionResult{(spec.IsCommand ? string.Empty : $"<{response}>")}> {method}";

        var lines = new List<string>
        {
            $"/// <summary>{Sentence(spec.Name)}.</summary>",
            $"[Http{(spec.IsCommand ? "Post" : "Get")}(\"{RouteTemplate(spec)}\")]",
            spec.IsCommand
                ? "[ProducesResponseType(StatusCodes.Status204NoContent)]"
                : $"[ProducesResponseType(StatusCodes.Status200OK, Type = typeof({response}))]",
        };

        if (spec.HasId || spec.HasRequest)
        {
            lines.Add("[ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]");
        }

        if (spec.HasId)
        {
            lines.Add("[ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]");
        }

        if (spec.IsCommand && spec.HasId)
        {
            lines.AddRange(
            [
                "[ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]",
                "[ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]",
                "[ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]",
            ]);
        }

        string Answer(string sent) => spec.IsCommand
            ? $"NoContentOrProblem(await sender.Send({sent}, cancellationToken))"
            : $"OkOrProblem((await sender.Send({sent}, cancellationToken)).Map(_ => new {response}()))";

        if (!spec.HasRequest && !(spec.IsCommand && spec.HasId))
        {
            lines.AddRange([$"{declaration}({string.Join(", ", signature)}) =>", $"    {Answer(message)};"]);

            return lines;
        }

        lines.Add(spec.HasRequest
            ? $"{declaration}(\n{string.Join(",\n", signature.Select(parameter => "    " + parameter))})"
            : $"{declaration}({string.Join(", ", signature)})");
        lines.Add("{");

        if (spec.HasRequest)
        {
            lines.AddRange(["    ArgumentNullException.ThrowIfNull(request);", string.Empty]);
        }

        if (spec.IsCommand && spec.HasId)
        {
            lines.AddRange(
            [
                "    if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)",
                "    {",
                "        return refusal;",
                "    }",
                string.Empty,
                $"    var outcome = await sender.Send({message}, cancellationToken);",
                string.Empty,
                "    return NoContentOrProblem(RequiringExistence(requiresExistence, outcome));",
            ]);
        }
        else
        {
            string local = spec.IsCommand ? "command" : "query";

            lines.AddRange([$"    var {local} = {message};", string.Empty, $"    return {Answer(local)};"]);
        }

        lines.Add("}");

        return [.. lines.SelectMany(line => line.Split('\n'))];
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
            InputCases(scratch, Expect);
            TreeCases(scratch, Expect);
            AppliedCases(scratch, Expect);
            AllOrNothingCases(scratch, Expect);
            IdempotenceCases(scratch, Expect);
            ShapeCases(scratch, Expect);
            ContentCases(scratch, Expect);
            DtoCases(scratch, Expect);
            ManifestCases(scratch, Expect);
            RollbackCases(scratch, Expect);
            WithoutTheApiCases(scratch, Expect);
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
            "Src/Domain/A.Domain/Features/Invoices/Repositories",
            "Src/Application/A.Application/Features/Invoices/UseCases/Commands/CreateInvoice",
            "Src/Application/A.Application/Features/Notes/Services",
            "Src/Presentation/A.Api/Features/Invoices/Controllers",
            "Tests/Application/A.Application.UnitTests/TestDoubles",
        ];

        foreach (string directory in directories)
        {
            Directory.CreateDirectory(Path.Combine(root, directory));
        }

        File.WriteAllText(Path.Combine(root, "cleanarch.json"), "{\n  \"name\": \"Acme\"\n}\n");
        File.WriteAllText(Path.Combine(root, "Tests/Application/A.Application.UnitTests/TestDoubles/ValidatedHandler.cs"), "the test double a person keeps\n");

        foreach (string repository in repositories.Length == 0 ? ["Invoice"] : repositories)
        {
            File.WriteAllText(
                Path.Combine(root, $"Src/Domain/A.Domain/Features/Invoices/Repositories/I{repository}Repository.cs"),
                $"public interface I{repository}Repository;\n");
        }

        File.WriteAllText(Path.Combine(root, _controller), _controllerText);
        Directory.CreateDirectory(Path.Combine(root, "docs/reference"));
        File.WriteAllText(Path.Combine(root, _apiPage), _apiPageText);

        return root;
    }

    private const string _controller = "Src/Presentation/A.Api/Features/Invoices/Controllers/InvoicesController.cs";

    private const string _controllerText =
        """
        using Microsoft.AspNetCore.Mvc;

        namespace Acme.Api.Features.Invoices.Controllers;

        [ApiController]
        public sealed class InvoicesController(
            IInvoiceService service) : ControllerBase
        {
            [HttpGet]
            public Task<ActionResult> List() => service.ListAsync();
        }

        """;

    private const string _apiPage = "docs/reference/api.md";

    private const string _apiPageText =
        """
        # API

        ## Routes

        ### Invoices

        Under `/api/v1/invoices`.

        | Method | Route | Success | Notes |
        |---|---|---|---|
        | GET | `/` | 200 | |

        ## Error codes

        """;

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args) => Invoke(root, _ => 0, args);

    private static (int Code, string Output, string Error) Invoke(string root, Func<TextEdits.Command, int> run, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = Run([.. args, "--root", root], output, error, run);

        return (code, output.ToString(), error.ToString());
    }

    private static Func<TextEdits.Command, int> Recording(List<TextEdits.Command> ran, string? failing = null) =>
        command =>
        {
            ran.Add(command);

            return command.Display == failing ? 1 : 0;
        };

    private static int Occurrences(string root, string path, string line) =>
        File.Exists(Path.Combine(root, path)) ? File.ReadAllLines(Path.Combine(root, path)).Count(candidate => candidate.Trim() == line) : -1;

    private static void AppliedCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        var ran = new List<TextEdits.Command>();
        var result = Invoke(root, Recording(ran), "Invoices", "FindByCustomer", "--kind", "query", "--input", "id, Customer:string");

        expect(result.Code == 0, $"The applied use case was refused: {result.Error}");

        (string Path, string Line, int Count)[] expected =
        [
            (_controller, "ISender sender,", 1),
            (_controller, "IInvoiceService service) : ControllerBase", 1),
            (_controller, "[HttpGet(\"{invoiceId:guid}/find-by-customer\")]", 1),
            (_controller, "[FromQuery] FindByCustomerRequest request,", 1),
            (_controller, "using Acme.Application.Features.Invoices.UseCases.Queries.FindByCustomer;", 1),
            (_controller, "using Acme.Api.Features.Invoices.Contracts.Requests;", 1),
            (_controller, "using Acme.Api.Features.Invoices.Contracts.Responses;", 1),
            (_controller, "using Mediator;", 1),
            //#if (api)
            (_apiPage, "| GET | `/{invoiceId}/find-by-customer` | 200 | |", 1),
            //#endif
            ("Src/Presentation/A.Api/Features/Invoices/Contracts/Requests/FindByCustomerRequest.cs", "public sealed record FindByCustomerRequest(string Customer);", 1),
            ("Src/Presentation/A.Api/Features/Invoices/Contracts/Responses/FindByCustomerResponse.cs", "public sealed record FindByCustomerResponse;", 1),
        ];

        foreach (var (path, line, count) in expected)
        {
            expect(Occurrences(root, path, line) == count, $"The applied use case did not leave '{line}' {count} time(s) in {path}.");
        }

        string[] controller = File.ReadAllLines(Path.Combine(root, _controller));

        expect(controller[^1] == "}" && controller[^2] == "    }" && controller.Count(line => line == "}") == 1, "The action was not placed inside the controller, before its closing brace.");

        string[] usings = [.. controller.Where(line => line.StartsWith("using ", StringComparison.Ordinal))];

        expect(usings.SequenceEqual(usings.Order(StringComparer.OrdinalIgnoreCase)), $"The controller's usings are out of order: {string.Join(" ", usings)}");
        expect(
            ran.Select(command => command.Display).SequenceEqual(["dotnet run Tools/Tasks.cs openapi"]),
            $"The applied use case did not rewrite the OpenAPI reference alone: {string.Join(" | ", ran.Select(command => command.Display))}");

        var again = Invoke(root, "Invoices", "FindByCustomer", "--kind", "query", "--input", "id, Customer:string");

        expect(again.Code == 1 && again.Error.Contains("already has a use case called FindByCustomer", StringComparison.Ordinal), "Running the same use case again was not refused.");
        expect(Occurrences(root, _controller, "ISender sender,") == 1, "A second action asked for a second ISender.");
    }

    private static void AllOrNothingCases(string scratch, Action<bool, string> expect)
    {
        (string Label, Action<string> Break, string Fragment)[] breaks =
        [
            //#if (api)
            ("no route table", root => File.WriteAllText(Path.Combine(root, _apiPage), "# API\n\n## Routes\n\n## Error codes\n"), "has no route table for Invoices"),
            ("no API page", root => File.Delete(Path.Combine(root, _apiPage)), "docs/reference/api.md does not exist"),
            //#endif
            ("no closing brace", root => File.WriteAllText(Path.Combine(root, _controller), _controllerText.Replace("\n}\n", "\n", StringComparison.Ordinal)), "closing brace"),
            ("no controller", root => File.Delete(Path.Combine(root, _controller)), "InvoicesController.cs does not exist"),
        ];

        foreach (var (label, breaking, fragment) in breaks)
        {
            string root = NewTree(scratch);

            breaking(root);

            var before = Snapshot(root);
            var ran = new List<TextEdits.Command>();
            var result = Invoke(root, Recording(ran), "Invoices", "GetInvoiceTotals", "--kind", "query");

            expect(result.Code == 1 && Same(before, Snapshot(root)) && ran.Count == 0, $"[{label}] a missing anchor did not stop the use case before it wrote or ran.");
            expect(result.Error.Contains(fragment, StringComparison.Ordinal), $"[{label}] the refusal does not say '{fragment}': {result.Error}");
            expect(result.Output.Contains("the edit by hand in", StringComparison.Ordinal), $"[{label}] the refusal does not print the edit to make by hand.");
        }
    }

    private static void IdempotenceCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string withAction = _controllerText.Replace(
            "    public Task<ActionResult> List()",
            "    public async Task<ActionResult> Approve(Guid invoiceId, CancellationToken cancellationToken) => NoContent();\n\n    public Task<ActionResult> List()",
            StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(root, _controller), withAction);
        File.WriteAllText(Path.Combine(root, _apiPage), _apiPageText.Replace("| GET | `/` | 200 | |", "| GET | `/` | 200 | |\n| POST | `/{invoiceId}/approve` | 204 | |", StringComparison.Ordinal));

        var result = Invoke(root, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(result.Code == 0, $"A use case whose action a person had written was refused: {result.Error}");
        expect(File.ReadAllText(Path.Combine(root, _controller)) == withAction, "An action already in place was written a second time.");
        //#if (api)
        expect(Occurrences(root, _apiPage, "| POST | `/{invoiceId}/approve` | 204 | |") == 1, "A route row already in place was written a second time.");
        //#endif

        string dry = NewTree(scratch);
        var before = Snapshot(dry);
        var ran = new List<TextEdits.Command>();
        var preview = Invoke(dry, Recording(ran), "Invoices", "ApproveInvoice", "--kind", "command", "--dry-run");

        expect(preview.Code == 0 && Same(before, Snapshot(dry)) && ran.Count == 0, "A dry run wrote or ran something.");
        expect(
            preview.Output.Contains("makes in Src/Presentation/A.Api/Features/Invoices/Controllers/InvoicesController.cs", StringComparison.Ordinal)
                && preview.Output.Contains("nothing was written and nothing was run", StringComparison.Ordinal),
            "A dry run did not print the edit it would make.");

        string failing = NewTree(scratch);
        var failed = Invoke(failing, Recording(ran = [], failing: "dotnet run Tools/Tasks.cs openapi"), "Invoices", "ApproveInvoice", "--kind", "command");

        expect(
            failed.Code == 1 && failed.Error.Contains("  dotnet run Tools/Tasks.cs openapi", StringComparison.Ordinal) && Occurrences(failing, _controller, "ISender sender,") == 1,
            "A failed OpenAPI step did not fail the command and name what remains, or undid the edit.");
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
            (["Invoices", "ApproveInvoice", "--kind", "command", "--input", "body"], "'body' in --input is neither id nor an input"),
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

    private static void InputCases(string scratch, Action<bool, string> expect)
    {
        (string Input, bool HasId, string[] Declared)[] accepted =
        [
            ("none", false, []),
            ("id", true, []),
            ("Name:string, MaxCount:int", false, ["string Name", "int MaxCount"]),
            ("id, Tags:IReadOnlyList<string>", true, ["IReadOnlyList<string> Tags"]),
            ("Lookup:Dictionary<string,int>", false, ["Dictionary<string, int> Lookup"]),
            (" Map : Dictionary< string , List<int?> > , Ids:Guid[]", false, ["Dictionary<string, List<int?>> Map", "Guid[] Ids"]),
            ("Grid:int[,], Note:string?, Due:DateOnly?, Matrix:int?[][]", false, ["int[,] Grid", "string? Note", "DateOnly? Due", "int?[][] Matrix"]),
            ("Pairs:List<(string Key, int Value)>", false, ["List<(string Key, int Value)> Pairs"]),
            ("Owner:global::System.Guid, Nested:Outer<int>.Inner", false, ["global::System.Guid Owner", "Outer<int>.Inner Nested"]),
        ];

        foreach (var (input, hasId, declared) in accepted)
        {
            var problems = new List<string>();
            var (readId, inputs) = ReadInputs(input, problems);
            string[] read = [.. inputs.Select(parameter => $"{parameter.Type} {parameter.Name}")];

            expect(problems.Count == 0, $"[--input {input}] was refused: {string.Join(" ", problems)}");
            expect(readId == hasId, $"[--input {input}] read the aggregate's id as {(readId ? "present" : "absent")}.");
            expect(read.SequenceEqual(declared), $"[--input {input}] read {string.Join("; ", read)}, not {string.Join("; ", declared)}.");
        }

        (string Input, string Fragment)[] refused =
        [
            ("Name", "neither id nor an input written Name:Type"),
            ("name:string", "'name' in --input is not an input name"),
            (":string", "'' in --input is not an input name"),
            ("Name:", "it stops short of a complete type"),
            ("Name:List<string", "it stops where '>' is due"),
            ("Name:List<>", "'>' stands where a name is due"),
            ("Name:List<string>>", "'>' follows a type that was already complete"),
            ("Name:int x", "'x' follows a type that was already complete"),
            ("Name:int??", "a type is made nullable once"),
            ("Name:class", "'class' is a C# keyword, not a type"),
            ("Name:void", "'void' is a C# keyword, not a type"),
            ("Name:System.string", "'string' is a C# keyword, not a type"),
            ("Name:1abc", "'1' has no place in a type name"),
            ("Name:(int)", "a tuple holds two elements or more"),
            ("Name:(int, string in)", "'in' is a C# keyword, not an element name"),
            ("Name:string, Name:int", "--input names Name more than once"),
            ("Tags:string, id", "id comes first in --input"),
            ("id, id", "id comes first in --input"),
            ("none, Name:string", "none stands alone in --input"),
            ("Name:string,", "holds an empty entry"),
            ("ToString:string", "a member every record declares already"),
        ];

        string root = NewTree(scratch);

        foreach (var (input, fragment) in refused)
        {
            ExpectRefusal(root, ["Invoices", "ApproveInvoice", "--kind", "command", "--input", input], fragment, expect);
        }

        ExpectRefusal(root, ["Invoices", "ApproveInvoice", "--kind", "command", "--input", "id, InvoiceId:Guid"], "The input InvoiceId would clash", expect);
        ExpectRefusal(root, ["Invoices", "ApproveInvoice", "--kind", "command", "--input", "id, Precondition:string"], "The input Precondition would clash", expect);
        ExpectRefusal(root, ["Invoices", "ApproveInvoice", "--kind", "command", "--input", "ApproveInvoiceCommand:int"], "would clash", expect);
        expect(
            Invoke(root, "Invoices", "ReviewInvoice", "--kind", "query", "--input", "id, Precondition:string").Code == 0,
            "A query was refused an input named Precondition, a name only a command with an id holds.");
    }

    private static void ContentCases(string scratch, Action<bool, string> expect)
    {
        string useCases = "Src/Application/A.Application/Features/Invoices/UseCases";
        string tests = "Tests/Application/A.Application.UnitTests/Features/Invoices/UseCases";
        string root = NewTree(scratch);

        string Read(string relative) => File.Exists(Path.Combine(root, relative)) ? File.ReadAllText(Path.Combine(root, relative)) : string.Empty;

        void Holds(string text, string fragment, string what) =>
            expect(text.ReplaceLineEndings("\n").Contains(fragment, StringComparison.Ordinal), $"{what} does not hold '{fragment}'.");

        var query = Invoke(root, "Invoices", "FindInvoicesByCustomer", "--kind", "query", "--input", "Customer:string, Limit:int, Since:DateOnly?, Until:DateTimeOffset?");
        string queryFolder = $"{useCases}/Queries/FindInvoicesByCustomer";
        string queryTests = $"{tests}/Queries/FindInvoicesByCustomer";

        expect(query.Code == 0, $"The parameterised query was refused: {query.Error}");
        Holds(Read($"{queryFolder}/FindInvoicesByCustomer.cs"), "public sealed record FindInvoicesByCustomerQuery(\n    string Customer,\n    int Limit,\n    DateOnly? Since,\n    DateTimeOffset? Until) : IQuery<Result<FindInvoicesByCustomerDto>>;", "The query");
        Holds(Read($"{queryFolder}/FindInvoicesByCustomerQueryValidator.cs"), "RuleFor(query => query.Customer)\n            .NotEmpty().WithMessage(\"Customer is required.\");", "The query's validator");
        Holds(Read($"{queryFolder}/FindInvoicesByCustomerQueryValidator.cs"), "RuleFor(query => query.Limit)", "The query's validator");
        expect(!Read($"{queryFolder}/FindInvoicesByCustomerQueryValidator.cs").Contains("Since", StringComparison.Ordinal), "A nullable input was given a rule.");
        expect(!Read("Src/Application/A.Application/Features/Invoices/Model/Dtos/FindInvoicesByCustomerDto.cs").Contains("Id", StringComparison.Ordinal), "A query without the id answers a DTO carrying it.");
        Holds(Read($"{queryTests}/FindInvoicesByCustomerQueryValidatorTests.cs"), "new(\n            \"customer\",\n            1,\n            new DateOnly(2026, 1, 1),\n            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));", "The query's validator tests");
        Holds(Read($"{queryTests}/FindInvoicesByCustomerQueryValidatorTests.cs"), "AWellFormedQuery() with { Customer = string.Empty }", "The query's validator tests");
        Holds(Read($"{queryTests}/FindInvoicesByCustomerQueryValidatorTests.cs"), "AWellFormedQuery() with { Limit = 0 }", "The query's validator tests");
        Holds(Read($"{queryTests}/FindInvoicesByCustomerTests.cs"), "new(new FindInvoicesByCustomerQueryHandler().Handle, new FindInvoicesByCustomerQueryValidator());", "The query's handler test");
        expect(
            Regex.Count(Read($"{queryTests}/FindInvoicesByCustomerTests.cs"), "QueryHandler\\(", RegexOptions.None, TimeSpan.FromSeconds(1)) == 1,
            "The query's handler test builds its handler in more than one place.");
        Holds(query.Output, "      [FromQuery] FindInvoicesByCustomerRequest request,", "The query's action");
        Holds(query.Output, "          var query = new FindInvoicesByCustomerQuery(request.Customer, request.Limit, request.Since, request.Until);", "The query's action");
        Holds(query.Output, "          return OkOrProblem((await sender.Send(query, cancellationToken)).Map(_ => new FindInvoicesByCustomerResponse()));", "The query's action");
        Holds(query.Output, "                       Acme.Api.Features.Invoices.Contracts.Requests;", "The query's usings");
        Holds(query.Output, "  and Src/Presentation/A.Api/Features/Invoices/Contracts/Requests/FindInvoicesByCustomerRequest.cs, with what a client sends:", "The query's request contract");
        Holds(query.Output, "      public sealed record FindInvoicesByCustomerRequest(\n          string Customer,", "The query's request contract");
        expect(!query.Output.Contains("Written as given", StringComparison.Ordinal), "Inputs of known types were listed as unknown.");

        var command = Invoke(root, "Invoices", "RetagInvoice", "--kind", "command", "--input", "id, Tags:IReadOnlyList<string>");
        string commandFolder = $"{useCases}/Commands/RetagInvoice";

        expect(command.Code == 0, $"The command with the id and a body was refused: {command.Error}");
        Holds(Read($"{commandFolder}/RetagInvoice.cs"), "    Guid InvoiceId,\n    IReadOnlyList<string> Tags,\n    VersionPrecondition? Precondition = null) : ICommand<Result>;", "The command");
        Holds(Read($"{tests}/Commands/RetagInvoice/RetagInvoiceCommandValidatorTests.cs"), "        new(Guid.CreateVersion7(), default!);", "The command's validator tests");
        Holds(Read($"{tests}/Commands/RetagInvoice/RetagInvoiceTests.cs"), "AWellFormedCommand() with { InvoiceId = Guid.Empty }", "The command's handler test");
        Holds(command.Output, "Written as given, of a type this command knows no rule and no test value for:\n  Tags  IReadOnlyList<string>", "The command's report");
        Holds(command.Output, "a real value in AWellFormedCommand() in RetagInvoiceCommandValidatorTests, which passes default! for it", "The command's report");
        Holds(command.Output, "      [FromBody] RetagInvoiceRequest request,", "The command's action");
        Holds(command.Output, "new RetagInvoiceCommand(invoiceId, request.Tags, precondition)", "The command's action");
        Holds(command.Output, "/// <summary>The body of retag invoice; the invoice id travels in the route.</summary>", "The command's request contract");

        var bare = Invoke(root, "Invoices", "FlagInvoices", "--kind", "command", "--input", "Labels:HashSet<string>, Urgent:bool");

        expect(bare.Code == 0, $"A command without a rule to write was refused: {bare.Error}");
        Holds(bare.Output, "No validator and no test were written", "The report of a command without a rule");
        Holds(Read($"{useCases}/Commands/FlagInvoices/FlagInvoices.cs"), "    HashSet<string> Labels,\n    bool Urgent) : ICommand<Result>;", "The command without a rule");

        var none = Invoke(root, "Invoices", "PingInvoices", "--kind", "command", "--input", "none");

        expect(none.Code == 0, $"A command without input was refused: {none.Error}");
        Holds(Read($"{useCases}/Commands/PingInvoices/PingInvoices.cs"), "public sealed record PingInvoicesCommand : ICommand<Result>;", "The command without input");
    }

    private static void TreeCases(string scratch, Action<bool, string> expect)
    {
        string stranger = Path.Combine(scratch, "stranger");
        Directory.CreateDirectory(stranger);

        var elsewhere = Invoke(stranger, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(elsewhere.Code == 1 && elsewhere.Error.Contains("No cleanarch.json", StringComparison.Ordinal), "A tree without a project file was not refused for it.");
        expect(!Directory.EnumerateFileSystemEntries(stranger).Any(), "A refused tree was written to.");

        string root = NewTree(scratch);

        ExpectRefusal(root, ["Orders", "ApproveOrder", "--kind", "command"], "No feature called Orders", expect);
        ExpectRefusal(root, ["Orders", "ApproveOrder", "--kind", "command"], "The features here are Invoices, Notes.", expect);
        ExpectRefusal(root, ["Notes", "ArchiveNote", "--kind", "command"], "it is a service over a store", expect);
        ExpectRefusal(root, ["Invoices", "CreateInvoice", "--kind", "command"], "already has a use case called CreateInvoice", expect);
        ExpectRefusal(root, ["Invoices", "CreateInvoice", "--kind", "query"], "already has a use case called CreateInvoice", expect);
        ExpectRefusal(root, ["Invoices", "Invoice", "--kind", "query"], "the aggregate's own name", expect);

        string none = NewTree(scratch, "Unused");
        File.Delete(Path.Combine(none, "Src/Domain/A.Domain/Features/Invoices/Repositories/IUnusedRepository.cs"));

        ExpectRefusal(none, ["Invoices", "ApproveInvoice", "--kind", "command"], "declares no repository", expect);
        expect(Invoke(none, "Invoices", "ApproveInvoice", "--kind", "command", "--input", "none").Code == 0, "--input none was refused where no repository names the aggregate.");

        string two = NewTree(scratch, "Invoice", "CreditNote");

        ExpectRefusal(two, ["Invoices", "ApproveInvoice", "--kind", "command"], "several (CreditNote, Invoice)", expect);

        string bare = NewTree(scratch);
        Directory.Delete(Path.Combine(bare, "Src/Application/A.Application/Features"), recursive: true);

        ExpectRefusal(bare, ["Invoices", "ApproveInvoice", "--kind", "command"], "This tree has no feature yet", expect);

        string blocked = NewTree(scratch);
        string inTheWay = Path.Combine(blocked, "Src/Application/A.Application/Features/Invoices/Model/Dtos/InvoiceTotalsDto.cs");
        string test = Path.Combine(blocked, "Tests/Application/A.Application.UnitTests/Features/Invoices/UseCases/Commands/ApproveInvoice/ApproveInvoiceCommandValidatorTests.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(inTheWay)!);
        File.WriteAllText(inTheWay, "somebody's own dto\n");
        Directory.CreateDirectory(Path.GetDirectoryName(test)!);
        File.WriteAllText(test, "somebody's own test\n");

        ExpectRefusal(blocked, ["Invoices", "ApproveInvoice", "--kind", "command"], "none is ever overwritten", expect);
        expect(File.ReadAllText(test) == "somebody's own test\n", "An existing file was overwritten.");
    }

    private static void ShapeCases(string scratch, Action<bool, string> expect)
    {
        string useCases = "Src/Application/A.Application/Features/Invoices/UseCases";
        string tests = "Tests/Application/A.Application.UnitTests/Features/Invoices/UseCases";
        string dtos = "Src/Application/A.Application/Features/Invoices/Model/Dtos";

        (string[] Args, string[] Files)[] cases =
        [
            (["ApproveInvoice", "--kind", "command"],
                [$"{useCases}/Commands/ApproveInvoice/ApproveInvoice.cs", $"{useCases}/Commands/ApproveInvoice/ApproveInvoiceCommandValidator.cs",
                 $"{tests}/Commands/ApproveInvoice/ApproveInvoiceCommandValidatorTests.cs", $"{tests}/Commands/ApproveInvoice/ApproveInvoiceTests.cs"]),
            (["CloseOverdueInvoices", "--kind", "command", "--input", "none"],
                [$"{useCases}/Commands/CloseOverdueInvoices/CloseOverdueInvoices.cs"]),
            (["GetInvoiceTotals", "--kind", "query"],
                [$"{useCases}/Queries/GetInvoiceTotals/GetInvoiceTotals.cs", $"{useCases}/Queries/GetInvoiceTotals/GetInvoiceTotalsQueryValidator.cs",
                 $"{dtos}/InvoiceTotalsDto.cs", $"{tests}/Queries/GetInvoiceTotals/GetInvoiceTotalsQueryValidatorTests.cs",
                 $"{tests}/Queries/GetInvoiceTotals/GetInvoiceTotalsTests.cs"]),
            (["SummariseRevenue", "--kind", "query", "--input=none"],
                [$"{useCases}/Queries/SummariseRevenue/SummariseRevenue.cs", $"{dtos}/SummariseRevenueDto.cs"]),
            (["FindInvoicesByCustomer", "--kind", "query", "--input", "Customer:string, Limit:int, Since:DateOnly?"],
                [$"{useCases}/Queries/FindInvoicesByCustomer/FindInvoicesByCustomer.cs", $"{useCases}/Queries/FindInvoicesByCustomer/FindInvoicesByCustomerQueryValidator.cs",
                 $"{dtos}/FindInvoicesByCustomerDto.cs", $"{tests}/Queries/FindInvoicesByCustomer/FindInvoicesByCustomerQueryValidatorTests.cs",
                 $"{tests}/Queries/FindInvoicesByCustomer/FindInvoicesByCustomerTests.cs"]),
            (["RetagInvoice", "--kind", "command", "--input", "id, Tags:IReadOnlyList<string>"],
                [$"{useCases}/Commands/RetagInvoice/RetagInvoice.cs", $"{useCases}/Commands/RetagInvoice/RetagInvoiceCommandValidator.cs",
                 $"{tests}/Commands/RetagInvoice/RetagInvoiceCommandValidatorTests.cs", $"{tests}/Commands/RetagInvoice/RetagInvoiceTests.cs"]),
            (["FlagInvoices", "--kind", "command", "--input", "Labels:HashSet<string>, Urgent:bool"],
                [$"{useCases}/Commands/FlagInvoices/FlagInvoices.cs"]),
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

            expect(
                created.Where(path => !path.Contains("/Contracts/", StringComparison.Ordinal)).SequenceEqual(files.Order(StringComparer.Ordinal)),
                $"[{label}] wrote {string.Join(", ", created)}.");
            expect(
                before.Where(pair => pair.Key is not (_controller or _apiPage)).All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value),
                $"[{label}] changed a file it neither owns nor serves the use case through.");
            expect(!Directory.Exists(Path.Combine(root, FeatureManifest.Folder)), $"[{label}] wrote a manifest where the feature had none.");
            expect(result.Output.Contains("dotnet run Tools/Tasks.cs openapi", StringComparison.Ordinal), $"[{label}] did not print the OpenAPI step.");
            expect(
                !result.Output.Contains("api-client", StringComparison.Ordinal) && !result.Output.Contains("pnpm api", StringComparison.Ordinal),
                $"[{label}] printed a step for a client and a web workspace the project does not hold.");
            expect(result.Output.Contains("Src/Presentation/A.Api/Features/Invoices/Controllers/InvoicesController.cs", StringComparison.Ordinal), $"[{label}] did not name the feature's controller.");

            //#if (api)
            string row = args[0] switch
            {
                "ApproveInvoice" => "| POST | `/{invoiceId}/approve` | 204 | |",
                "CloseOverdueInvoices" => "| POST | `/close-overdue-invoices` | 204 | |",
                "GetInvoiceTotals" => "| GET | `/{invoiceId}/totals` | 200 | |",
                "FindInvoicesByCustomer" => "| GET | `/find-invoices-by-customer` | 200 | |",
                "RetagInvoice" => "| POST | `/{invoiceId}/retag` | 204 | |",
                "FlagInvoices" => "| POST | `/flag-invoices` | 204 | |",
                _ => "| GET | `/summarise-revenue` | 200 | |",
            };

            expect(result.Output.Contains("ApiRouteTableTests", StringComparison.Ordinal), $"[{label}] did not name the test that reads the route table.");
            expect(result.Output.Contains($"  {row}", StringComparison.Ordinal), $"[{label}] did not print the route's row '{row}'.");
            //#endif

            foreach (string file in created)
            {
                foreach (string problem in FileProblems(root, file))
                {
                    expect(false, $"[{label}] {problem}");
                }
            }
        }

        string contractRoot = NewTree(scratch);

        Directory.CreateDirectory(Path.Combine(contractRoot, "Src/Clients/A.Client"));
        Directory.CreateDirectory(Path.Combine(contractRoot, "Src/Web/packages/api-client/scripts"));
        File.WriteAllText(Path.Combine(contractRoot, "Src/Web/packages/api-client/scripts/generate.mjs"), "// the script pnpm api runs\n");

        var ran = new List<TextEdits.Command>();
        var contract = Invoke(contractRoot, Recording(ran), "Invoices", "ApproveInvoice", "--kind", "command");

        string[] launched = [.. ran.Select(command => string.Join(' ', [Path.GetFileNameWithoutExtension(command.FileName), .. command.Arguments]))];

        expect(
            contract.Code == 0
                && launched.SequenceEqual(["dotnet run Tools/Tasks.cs openapi", "dotnet run Tools/Tasks.cs api-client", "pnpm api --no-build"]),
            $"The OpenAPI reference, which builds the API, then the client and the web schema from that build were not all run, in that order, for a project that holds the client and the web workspace: {string.Join(" | ", launched)}");

        string untested = NewTree(scratch);
        Directory.Delete(Path.Combine(untested, "Tests/Application/A.Application.UnitTests"), recursive: true);

        var skipped = Invoke(untested, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(skipped.Code == 0 && skipped.Output.Contains("tests were skipped", StringComparison.Ordinal), "A tree without the application tests was not told its tests were skipped.");
        expect(!Directory.Exists(Path.Combine(untested, "Tests/Application/A.Application.UnitTests")), "Tests were written into a project that does not exist.");

        string undoubled = NewTree(scratch);
        File.Delete(Path.Combine(undoubled, "Tests/Application/A.Application.UnitTests/TestDoubles/ValidatedHandler.cs"));

        var halfTested = Invoke(undoubled, "Invoices", "ApproveInvoice", "--kind", "command");

        expect(
            halfTested.Code == 0 && halfTested.Output.Contains("Not written: ApproveInvoiceTests.cs", StringComparison.Ordinal),
            "A tree without the ValidatedHandler double was not told the handler's test was left out.");
        expect(
            File.Exists(Path.Combine(undoubled, $"{tests}/Commands/ApproveInvoice/ApproveInvoiceCommandValidatorTests.cs"))
            && !File.Exists(Path.Combine(undoubled, $"{tests}/Commands/ApproveInvoice/ApproveInvoiceTests.cs")),
            "Without the ValidatedHandler double, the validator's tests were not written alone.");
    }

    private static void DtoCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string dto = Path.Combine(root, "Src/Application/A.Application/Features/Invoices/Model/Dtos/InvoiceDto.cs");

        Directory.CreateDirectory(Path.GetDirectoryName(dto)!);
        File.WriteAllText(dto, "public sealed record InvoiceDto(Guid Id);\n");

        var result = Invoke(root, "Invoices", "GetInvoice", "--kind", "query");
        string useCase = Path.Combine(root, "Src/Application/A.Application/Features/Invoices/UseCases/Queries/GetInvoice/GetInvoice.cs");

        expect(result.Code == 0, $"A query answering an existing DTO was refused: {result.Error}");
        expect(File.ReadAllText(dto) == "public sealed record InvoiceDto(Guid Id);\n", "The existing DTO was overwritten.");
        expect(File.Exists(useCase) && File.ReadAllText(useCase).Contains("Result<InvoiceDto>", StringComparison.Ordinal), "The query does not answer the existing DTO.");
        expect(result.Output.Contains("already declares", StringComparison.Ordinal), "The reuse of an existing DTO was not reported.");

        string fresh = NewTree(scratch);
        string freshDto = Path.Combine(fresh, "Src/Application/A.Application/Features/Invoices/Model/Dtos/InvoiceTotalsDto.cs");

        expect(Invoke(fresh, "Invoices", "GetInvoiceTotals", "--kind", "query").Code == 0, "A query on a feature with a repository was refused.");
        expect(File.Exists(freshDto) && File.ReadAllText(freshDto).Contains("InvoiceTotalsDto(Guid Id);", StringComparison.Ordinal), "The generated DTO does not carry the aggregate's id.");

        string unrelated = Path.Combine(fresh, "Src/Application/A.Application/Features/Invoices/Model/Dtos/SummariseRevenueDto.cs");

        expect(Invoke(fresh, "Invoices", "SummariseRevenue", "--kind", "query", "--input", "none").Code == 0, "A query without input was refused.");
        expect(File.Exists(unrelated) && !File.ReadAllText(unrelated).Contains("Id", StringComparison.Ordinal), "A query without input answers a DTO carrying an id it was never given.");

        string bare = NewTree(scratch, "Unused");
        File.Delete(Path.Combine(bare, "Src/Domain/A.Domain/Features/Invoices/Repositories/IUnusedRepository.cs"));
        string bareDto = Path.Combine(bare, "Src/Application/A.Application/Features/Invoices/Model/Dtos/InvoiceTotalsDto.cs");

        var bareResult = Invoke(bare, "Invoices", "GetInvoiceTotals", "--kind", "query", "--input", "none");

        expect(bareResult.Code == 0, $"A query on a feature without a repository was refused: {bareResult.Error}");
        expect(File.Exists(bareDto) && !File.ReadAllText(bareDto).Contains("Id", StringComparison.Ordinal), "The generated DTO invents an id for a feature with no aggregate.");
    }

    private static void ManifestCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);
        string manifest = Path.Combine(root, FeatureManifest.Folder, "Invoices.json");
        const string generated = "Src/Application/A.Application/Features/Invoices/UseCases/Commands/CreateInvoice/CreateInvoice.cs";
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

        expect(created.Length == 10, $"Two use cases wrote {created.Length} files, not 9 and the response contract.");
        string[] outside =
        [
            _controller,
            //#if (api)
            _apiPage,
            //#endif
        ];

        expect(
            top.GetProperty("edits").EnumerateArray().Select(edit => edit.GetProperty("path").GetString()!).Distinct().Order(StringComparer.Ordinal).SequenceEqual(outside.Order(StringComparer.Ordinal)),
            $"The manifest does not record the edits made in files it does not list, the controller and the route table, and those alone: {string.Join(", ", top.GetProperty("edits").EnumerateArray().Select(edit => edit.GetProperty("path").GetString()))}");
        expect(top.GetProperty("schemaVersion").GetInt32() == 2, "A manifest that now records edits does not say schema version 2.");
        expect(listed.Select(entry => entry.Path).SequenceEqual(listed.Select(entry => entry.Path).Order(StringComparer.Ordinal)), "The manifest's files are not in path order.");
        expect(listed.Count == created.Length + 1 && listed.Any(entry => entry.Path == generated && entry.Sha256 == new string('a', 64)), "The manifest lost the generator's own entry or gained a stranger.");
        expect(!listed.Any(entry => entry.Path == _controller), "The manifest took in the controller, a file it did not list.");

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

        string owned = NewTree(scratch);
        string ownedManifest = Path.Combine(owned, FeatureManifest.Folder, "Invoices.json");
        string controllerHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(owned, _controller))));

        Directory.CreateDirectory(Path.GetDirectoryName(ownedManifest)!);
        File.WriteAllText(ownedManifest, original.Replace($"\"path\": \"{generated}\",\n      \"sha256\": \"{new string('a', 64)}\"", $"\"path\": \"{_controller}\",\n      \"sha256\": \"{controllerHash}\"", StringComparison.Ordinal));

        expect(Invoke(owned, "Invoices", "ApproveInvoice", "--kind", "command").Code == 0, "A use case was refused in a feature whose manifest lists its controller.");

        using (var ownedDocument = JsonDocument.Parse(File.ReadAllText(ownedManifest)))
        {
            var ownedTop = ownedDocument.RootElement;
            string recorded = ownedTop.GetProperty("files").EnumerateArray().Single(file => file.GetProperty("path").GetString() == _controller).GetProperty("sha256").GetString()!;

            expect(
                recorded == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(owned, _controller)))),
                "The controller the feature owns, untouched until the action was added, is not recorded with its new hash, so remove-feature would take it for edited.");
            expect(
                !ownedTop.TryGetProperty("edits", out var ownedEdits)
                    || ownedEdits.EnumerateArray().All(edit => edit.GetProperty("path").GetString() != _controller),
                "The action added to a controller the feature owns is recorded as an edit of a file outside it.");
        }

        string broken = NewTree(scratch);
        string brokenManifest = Path.Combine(broken, FeatureManifest.Folder, "Invoices.json");

        Directory.CreateDirectory(Path.GetDirectoryName(brokenManifest)!);
        File.WriteAllText(brokenManifest, "{ not json");

        ExpectRefusal(broken, ["Invoices", "ApproveInvoice", "--kind", "command"], "cannot be read", expect);

        File.WriteAllText(brokenManifest, "{ \"feature\": \"Orders\", \"files\": [] }");

        ExpectRefusal(broken, ["Invoices", "ApproveInvoice", "--kind", "command"], "does not read as the manifest of Invoices", expect);
    }

    private static void WithoutTheApiCases(string scratch, Action<bool, string> expect)
    {
        string[][] cases =
        [
            ["ApproveInvoice", "--kind", "command"],
            ["GetInvoiceTotals", "--kind", "query"],
            ["FindInvoicesByCustomer", "--kind", "query", "--input", "id, Customer:string, Event:int"],
        ];

        foreach (string[] args in cases)
        {
            string root = NewTree(scratch);
            string label = string.Join(' ', args);

            Directory.Delete(Path.Combine(root, "Src/Presentation/A.Api"), recursive: true);
            Directory.CreateDirectory(Path.Combine(root, "Src/Presentation/A.Portal"));
            File.WriteAllText(Path.Combine(root, "Src/Presentation/A.Portal/Program.cs"), "builder.Services.AddInvoices();\n");

            var result = Invoke(root, ["Invoices", .. args]);

            expect(result.Code == 0, $"[{label}] without the API was refused: {result.Error}");
            expect(
                !Regex.IsMatch(result.Output, @"\bApi\b|api\.md|OpenAPI|openapi|HTTP|Controller", RegexOptions.None, TimeSpan.FromSeconds(2)),
                $"[{label}] without the API printed an instruction about it: {result.Output}");
            expect(
                result.Output.Contains("Src/Presentation/A.Portal/", StringComparison.Ordinal),
                $"[{label}] without the API did not name the host that serves the feature.");
            expect(!Directory.Exists(Path.Combine(root, "Src/Presentation/A.Api")), $"[{label}] without the API wrote into it.");

            string sent = args[0] switch
            {
                "ApproveInvoice" => "send new ApproveInvoiceCommand(invoiceId) through ISender",
                "GetInvoiceTotals" => "send new GetInvoiceTotalsQuery(invoiceId) through ISender",
                _ => "send new FindInvoicesByCustomerQuery(invoiceId, customer, @event) through ISender",
            };

            expect(result.Output.Contains(sent, StringComparison.Ordinal), $"[{label}] without the API did not print the message to send with its inputs: '{sent}'.");
        }
    }

    private static void RollbackCases(string scratch, Action<bool, string> expect)
    {
        string root = NewTree(scratch);

        File.WriteAllText(Path.Combine(root, "Tests/Application/A.Application.UnitTests/Features"), "not a folder\n");

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

        if (_tokenPattern.IsMatch(text))
        {
            yield return $"{relative}: a token was left unsubstituted: {_tokenPattern.Match(text).Value}";
        }

        string code = relative.Contains("/Contracts/", StringComparison.Ordinal)
            ? string.Join('\n', text.Split('\n').Where(line => !line.StartsWith("/// <summary>", StringComparison.Ordinal)))
            : text;

        if (code.Contains("//", StringComparison.Ordinal) || code.Contains("/*", StringComparison.Ordinal))
        {
            yield return $"{relative}: a comment, where generated code carries none outside an API contract's summary.";
        }

        string[] segments = relative.Split('/');
        int project = Array.FindIndex(segments, segment => segment.Contains('.', StringComparison.Ordinal));
        string prefix = ProductPrefix(root);
        segments[project] = prefix + "." + segments[project][FolderOf(prefix, string.Empty).Length..];

        if (!text.Contains($"namespace {string.Join('.', segments[project..^1])};", StringComparison.Ordinal))
        {
            yield return $"{relative}: its namespace is not its project plus its folders.";
        }

        string stem = Path.GetFileNameWithoutExtension(relative);
        string[] declared = segments[^2] == stem
            ? [$"{stem}{(segments[^3] == "Commands" ? "Command" : "Query")}", $"{stem}{(segments[^3] == "Commands" ? "Command" : "Query")}Handler"]
            : [stem];

        foreach (string type in declared.Where(type => !Regex.IsMatch(text, $@"^public (?:sealed )?(?:class|interface|record) {type}\b", RegexOptions.Multiline, TimeSpan.FromSeconds(2))))
        {
            yield return $"{relative}: does not declare the public '{type}' it is named for.";
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
    internal const string Validator = """
        using FluentValidation;

        namespace %P%.Application.Features.%F%.UseCases.%K%.%U%;

        public sealed class %In%Validator : AbstractValidator<%In%>
        {
            public %In%Validator()
            {
        %Rules%
            }
        }
        """;

    internal const string UseCase = """
        using %P%.Application.Core.Common.Results;
        using %Concurrency%;
        using %Dtos%;
        using Mediator;

        namespace %P%.Application.Features.%F%.UseCases.%K%.%U%;

        %Message%

        public sealed class %In%Handler : I%Kind%Handler<%In%, %Out%>
        {
            public ValueTask<%Out%> Handle(%In% %in%, CancellationToken cancellationToken) =>
                throw new NotImplementedException(
                    %Unwritten%);
        }
        """;

    internal const string Dto = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %D%;
        """;

    internal const string DtoWithId = """
        namespace %P%.Application.Features.%F%.Model.Dtos;

        public sealed record %D%(Guid Id);
        """;

    internal const string ValidatorTests = """
        using %P%.Application.Features.%F%.UseCases.%K%.%U%;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.UseCases.%K%.%U%;

        public sealed class %In%ValidatorTests
        {
            private readonly %In%Validator _validator = new();

            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

            [Fact]
            public async Task AWellFormed%Kind%_PassesValidation() =>
                (await _validator.ValidateAsync(AWellFormed%Kind%(), TestToken))
                    .IsValid.ShouldBeTrue();
        %Refusals%
            internal static %In% AWellFormed%Kind%() =>
                new(%Values%);
        }
        """;

    internal const string HandlerTests = """
        using %P%.Application.Core.Common.Results;
        using %P%.Application.Features.%F%.UseCases.%K%.%U%;
        using %P%.Application.UnitTests.TestDoubles;
        using %Dtos%;
        using Shouldly;
        using Xunit;

        namespace %P%.Application.UnitTests.Features.%F%.UseCases.%K%.%U%;

        public sealed class %U%Tests
        {
            private static CancellationToken TestToken => TestContext.Current.CancellationToken;

            [Fact]
            public async Task AMalformed%Kind%_NeverReachesTheHandler()
            {
                var malformed = %In%ValidatorTests.AWellFormed%Kind%() with { %Malformed% };

                var result = await UseCase().Handle(malformed, TestToken);

                result.IsFailure.ShouldBeTrue();
                result.Error!.Type.ShouldBe(ErrorType.Validation);
            }

            private static ValidatedHandler<%In%, %Out%> UseCase() =>
                new(new %In%Handler().Handle, new %In%Validator());
        }
        """;
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

internal static class TextEdits
{
    private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal sealed record Edit(string Path, string Anchor, string Position, string[] Lines, string[] Replaced);

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

        private readonly string[] _source;

        private readonly List<int> _origin;

        private readonly bool _sourceFinalNewline;

        internal Sheet(string path, byte[] original)
        {
            Path = path;
            Original = original;
            _bom = original.AsSpan().StartsWith(Encoding.UTF8.Preamble);

            string text = _strictUtf8.GetString(original.AsSpan(_bom ? 3 : 0));

            _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            FinalNewline = text.EndsWith('\n');
            _lines = [.. (FinalNewline ? text[..^_newline.Length] : text).Split(_newline)];
            _source = [.. _lines];
            _origin = [.. Enumerable.Range(0, _lines.Count)];
            _sourceFinalNewline = FinalNewline;
        }

        internal string Path { get; }

        internal byte[] Original { get; }

        internal IReadOnlyList<string> Lines => _lines;

        internal bool FinalNewline { get; private set; }

        internal bool EndingChanged => FinalNewline != _sourceFinalNewline;

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

            _lines.RemoveRange(head, removed);
            _origin.RemoveRange(head, removed);
            Insert(head, next[head..^tail]);
            FinalNewline = finalNewline;
        }

        internal void Insert(int index, string[] lines)
        {
            _lines.InsertRange(index, lines);
            _origin.InsertRange(index, lines.Select(_ => -1));
        }

        internal IEnumerable<Edit> Edits()
        {
            int previous = -1;
            int previousSource = -1;

            for (int index = 0; index <= _lines.Count; index++)
            {
                if (index < _lines.Count && _origin[index] < 0)
                {
                    continue;
                }

                int nextSource = index < _lines.Count ? _origin[index] : _source.Length;
                string[] added = [.. _lines.Skip(previous + 1).Take(index - previous - 1)];
                string[] replaced = _source[(previousSource + 1)..nextSource];

                if (added.Length > 0 || replaced.Length > 0)
                {
                    int next = index < _lines.Count ? index : -1;
                    bool before = next >= 0 && (_lines[next].Trim().Length > 0 || previous < 0 || _lines[previous].Trim().Length == 0);

                    yield return new Edit(Path, _lines[before ? next : previous], before ? "before" : "after", added, replaced);
                }

                previous = index;
                previousSource = nextSource;
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
            _sheets.Values.OfType<Sheet>().Any(sheet => sheet.EndingChanged)
                ? throw new InvalidOperationException("An edit that adds or removes the newline that ends a file cannot be recorded to be undone.")
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

    internal static void Commit(string root, IReadOnlyList<(string Path, byte[] Bytes)> created, IReadOnlyList<(string Path, byte[] Original, byte[] Bytes)> replaced, IReadOnlyList<string>? deleted = null)
    {
        var written = new List<string>();
        var directories = new List<string>();
        var overwritten = new List<(string Full, byte[] Original)>();
        var setAside = new List<(string Full, string Aside)>();
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

            foreach (string path in deleted ?? [])
            {
                string full = System.IO.Path.Combine(root, path);
                string aside = $"{full}.{Guid.NewGuid():N}.tmp";

                File.Move(full, aside);
                setAside.Add((full, aside));
            }
        }
        catch
        {
            foreach (var (full, aside) in Enumerable.Reverse(setAside))
            {
                File.Move(aside, full);
            }

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

        foreach (var (_, aside) in setAside)
        {
            File.Delete(aside);
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

internal sealed class FeatureManifest
{
    internal const string Folder = ".feature-manifest";

    internal const int EditsVersion = 2;

    private readonly JsonObject _top;

    private readonly SortedDictionary<string, string> _files = new(StringComparer.Ordinal);

    private readonly List<TextEdits.Edit> _edits;

    private readonly int _editsRead;

    private FeatureManifest(string path, JsonObject top, byte[]? original, List<TextEdits.Edit> edits)
    {
        Path = path;
        Original = original;
        _top = top;
        _edits = edits;
        _editsRead = edits.Count;

        foreach (var file in ((JsonArray)top["files"]!).OfType<JsonObject>())
        {
            _files[file["path"]!.GetValue<string>()] = file["sha256"]!.GetValue<string>();
        }
    }

    internal string Path { get; }

    internal byte[]? Original { get; }

    internal int? SchemaVersion =>
        _top["schemaVersion"] is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out int version) ? version : null;

    internal string? Generator => Text(_top["generator"]);

    internal string? Feature => Text(_top["feature"]);

    internal string? Type => Text(_top["type"]);

    internal string? Page => Text(_top["page"]);

    internal JsonObject? Options => _top["options"] as JsonObject;

    internal JsonArray? Fields => _top["fields"] as JsonArray;

    internal IReadOnlyDictionary<string, string> Files => _files;

    internal IReadOnlyList<TextEdits.Edit> Edits => _edits;

    internal IReadOnlyList<(string Declaration, string Migration, string[] Paths)> AddedFields =>
        [
            .. (_top["addedFields"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(addition => Text(addition["declaration"]) is not null && Text(addition["migration"]) is not null)
                .Select(addition => (
                    Text(addition["declaration"])!,
                    Text(addition["migration"])!,
                    (string[])[.. (addition["files"] as JsonArray ?? []).OfType<JsonObject>().Select(file => Text(file["path"])).OfType<string>()])),
        ];

    internal (string Path, byte[] Original, byte[] Bytes) Replacement => (Path, Original!, Bytes());

    internal static string PathOf(string features) => $"{Folder}/{features}.json";

    internal static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static FeatureManifest Create(string generator, string features, string type, JsonObject options, JsonArray? fields, string? page)
    {
        var top = new JsonObject
        {
            ["schemaVersion"] = EditsVersion,
            ["generator"] = generator,
            ["feature"] = features,
            ["type"] = type,
            ["options"] = options,
        };

        if (fields is not null)
        {
            top["fields"] = fields;
        }

        top["files"] = new JsonArray();

        if (page is not null)
        {
            top["page"] = page;
        }

        top["edits"] = new JsonArray();

        return new FeatureManifest(PathOf(features), top, null, []);
    }

    internal static FeatureManifest? Read(string root, string features, out string? problem)
    {
        string path = PathOf(features);
        string full = System.IO.Path.Combine(root, path);

        problem = null;

        if (!File.Exists(full))
        {
            return null;
        }

        byte[] original;

        try
        {
            original = File.ReadAllBytes(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = $"cannot be read ({exception.Message})";

            return null;
        }

        try
        {
            if (JsonNode.Parse(original) is not JsonObject top
                || Text(top["feature"]) != features
                || top["files"] is not JsonArray files
                || files.Any(file => file is not JsonObject entry || Text(entry["path"]) is null || Text(entry["sha256"]) is null)
                || (top["edits"] is { } edits && edits is not JsonArray))
            {
                problem = $"does not read as the manifest of {features}";

                return null;
            }

            var read = new List<TextEdits.Edit>();

            foreach (var node in top["edits"] as JsonArray ?? [])
            {
                if (node is not JsonObject edit
                    || Text(edit["path"]) is not { } editPath
                    || Text(edit["anchor"]) is not { } anchor
                    || Text(edit["position"]) is not ("before" or "after") || edit["lines"] is not JsonArray lines
                    || lines.Any(line => Text(line) is null)
                    || (edit["replaced"] is { } replacedNode && (replacedNode is not JsonArray replacedLines || replacedLines.Any(line => Text(line) is null))))
                {
                    problem = $"does not read as the manifest of {features}: an edit names no file, anchor, position or lines";

                    return null;
                }

                read.Add(new TextEdits.Edit(
                    editPath,
                    anchor,
                    Text(edit["position"])!,
                    [.. lines.Select(line => Text(line)!)],
                    [.. (edit["replaced"] as JsonArray ?? []).Select(line => Text(line)!)]));
            }

            return new FeatureManifest(path, top, original, read);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            problem = $"cannot be read ({exception.Message})";

            return null;
        }
    }

    internal void Record(string path, byte[] bytes) => _files[path] = Sha256(bytes);

    internal void RecordAddition(string declaration, string migration, IEnumerable<(string Path, byte[]? Before, byte[] After, IReadOnlyList<string> Changes)> files)
    {
        var additions = _top["addedFields"] as JsonArray ?? [];

        additions.Add((JsonNode)new JsonObject
        {
            ["declaration"] = declaration,
            ["migration"] = migration,
            ["files"] = new JsonArray([.. files.Select(file => (JsonNode)new JsonObject
            {
                ["path"] = file.Path,
                ["before"] = file.Before is null ? null : Sha256(file.Before),
                ["after"] = Sha256(file.After),
                ["changes"] = new JsonArray([.. file.Changes.Select(change => (JsonNode)JsonValue.Create(change)!)]),
            })]),
        });
        _top["addedFields"] = additions;
    }

    internal void Record(TextEdits.Editor editor)
    {
        foreach (var sheet in editor.Changed)
        {
            if (_files.TryGetValue(sheet.Path, out string? recorded) && recorded == Sha256(sheet.Original))
            {
                _files[sheet.Path] = Sha256(sheet.Bytes());
            }
        }

        _edits.AddRange(editor.Applied.Where(edit => !_files.ContainsKey(edit.Path)));
    }

    internal byte[] Bytes()
    {
        var files = new JsonArray();

        foreach (var (path, sha256) in _files)
        {
            files.Add((JsonNode)new JsonObject { ["path"] = path, ["sha256"] = sha256 });
        }

        _top["files"] = files;

        var edits = new JsonArray();

        foreach (var edit in _edits)
        {
            var written = new JsonObject
            {
                ["path"] = edit.Path,
                ["anchor"] = edit.Anchor,
                ["position"] = edit.Position,
                ["lines"] = new JsonArray([.. edit.Lines.Select(line => (JsonNode)JsonValue.Create(line)!)]),
            };

            if (edit.Replaced.Length > 0)
            {
                written["replaced"] = new JsonArray([.. edit.Replaced.Select(line => (JsonNode)JsonValue.Create(line)!)]);
            }

            edits.Add((JsonNode)written);
        }

        if (_edits.Count > _editsRead)
        {
            _top["schemaVersion"] = Math.Max(SchemaVersion ?? 1, EditsVersion);
            _top.Remove("edits");
            _top["edits"] = edits;
        }
        else if (_top.ContainsKey("edits"))
        {
            _top["edits"] = edits;
        }

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            _top.WriteTo(writer);
        }

        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n");
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}
