#:property UseAppHost=false

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class NewUseCase
{
    private const string _manifestFolder = ".feature-manifest";

    private const string _usage =
        "Usage: dotnet run Tools/NewUseCase.cs <Features> <UseCase> --kind command|query\n"
        + "           [--input id|none|\"[id, ]Name:Type, Other:Type\"]\n"
        + "           [--root <path>]\n"
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

    private static readonly string[] _keywords =
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

    private static readonly Regex _pascalCase = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly Regex _tokenPattern = new("%([A-Za-z]+)%", RegexOptions.None, TimeSpan.FromSeconds(2));

    private sealed record Emission(string Path, string Content);

    private sealed record Request(string? Features, string? Name, string? Kind, string? Input, string Root);

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

        internal string Argument => _keywords.Contains(Lower(Name), StringComparer.Ordinal) ? "@" + Lower(Name) : Lower(Name);
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
        error.WriteLine(_usage);
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

            if (!_optionNames.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"'{name}' is not an option. The options are {string.Join(", ", _optionNames[..^1])}.");

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
                .Where(type => _pascalCase.IsMatch(type))
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

        string manifest = $"{_manifestFolder}/{features}.json";
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

        return new Spec(prefix, features, name, kind, aggregate, inputs, dto, dtoExists, File.Exists(manifestFull) ? manifest : null);
    }

    private static List<string> NameProblems(string features, string name)
    {
        var problems = new List<string>();

        if (!_pascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a feature folder: <Features> is PascalCase and plural, e.g. Invoices.");
        }

        if (!_pascalCase.IsMatch(name))
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

        if (_keywords.Contains(Lower(name), StringComparer.Ordinal))
        {
            problems.Add($"'{name}' becomes the C# keyword '{Lower(name)}' once its first letter is lowered, which is how the controller names it. Choose another name.");
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

                if (!_pascalCase.IsMatch(name))
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

            return _keywords.Contains(name, StringComparer.Ordinal)
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

            if (_keywords.Contains(first, StringComparer.Ordinal))
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

                named += "." + (_keywords.Contains(segment, StringComparer.Ordinal) ? throw new FormatException($"'{segment}' is a C# keyword, not a type") : segment) + TypeArguments();
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

    private static string Lower(string name) => char.ToLowerInvariant(name[0]) + name[1..];

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
        string kind = spec.IsCommand ? "Command" : "Query";
        string validatorTests = $"{spec.Input}ValidatorTests";
        bool testsWritten = plan.Any(emission => emission.Path.EndsWith($"/{validatorTests}.cs", StringComparison.Ordinal));
        bool handlerTested = plan.Any(emission => emission.Path.EndsWith($"/{u}Tests.cs", StringComparison.Ordinal));
        string api = $"Src/Presentation/{FolderOf(p, "Api")}";
        bool hasApi = Directory.Exists(Path.Combine(root, api));

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
        output.WriteLine($"To serve it over HTTP, the edit by hand in {where}:");
        output.WriteLine($"  the usings it lacks  {spec.Namespace};");
        output.WriteLine("                       Mediator;");

        if (spec.HasRequest)
        {
            output.WriteLine($"                       {p}.Api.Features.{f}.Contracts.Requests;");
        }

        if (!spec.IsCommand)
        {
            output.WriteLine($"                       {p}.Application.Core.Common.Results;");
            output.WriteLine($"                       {p}.Api.Features.{f}.Contracts.Responses;");
        }

        output.WriteLine("  the parameter        ISender sender, in the primary constructor, unless it has one");
        output.WriteLine("  the action");

        foreach (string line in Action(spec))
        {
            output.WriteLine(line.Length == 0 ? string.Empty : "      " + line);
        }

        if (spec.HasRequest)
        {
            string carrier = spec.IsCommand ? "body" : "query string";
            string summary = spec.HasId
                ? $"The {carrier} of {Separated(u, ' ')}; the {Separated(spec.Aggregate!, ' ')} id travels in the route."
                : $"The {carrier} of {Separated(u, ' ')}.";

            output.WriteLine($"  and {api}/Features/{f}/Contracts/Requests/{spec.Request}.cs, with what a client sends:");
            output.WriteLine($"      namespace {p}.Api.Features.{f}.Contracts.Requests;");
            output.WriteLine(string.Empty);
            output.WriteLine($"      /// <summary>{summary}</summary>");

            foreach (string line in Record(spec.Request, spec.Inputs.Select(parameter => $"{parameter.Type} {parameter.Name}"), string.Empty).Split('\n'))
            {
                output.WriteLine("      " + line);
            }
        }

        if (!spec.IsCommand)
        {
            string response = spec.Dto![..^"Dto".Length] + "Response";

            output.WriteLine($"  and {api}/Features/{f}/Contracts/Responses/{response}.cs, with the members a client reads:");
            output.WriteLine($"      namespace {p}.Api.Features.{f}.Contracts.Responses;");
            output.WriteLine(string.Empty);
            output.WriteLine($"      /// <summary>The wire shape of {Separated(response[..^"Response".Length], ' ')}.</summary>");
            output.WriteLine($"      public sealed record {response};");
        }

        output.WriteLine();
        output.WriteLine("Then rewrite the OpenAPI reference the contract test compares:  dotnet run Tools/Tasks.cs openapi");

        //#if (api)
        output.WriteLine($"and its row in the table of {spec.Features} under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads, relative to that table's 'Under' prefix:");
        output.WriteLine("  " + RouteRow(spec));
        //#endif

        if (File.Exists(Path.Combine(root, page)))
        {
            output.WriteLine($"and add the route to {page}.");
        }
    }

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

    private static string IdName(Spec spec) => spec.Aggregate is null ? string.Empty : Lower(spec.Aggregate) + "Id";

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

        File.WriteAllText(Path.Combine(root, "Src/Presentation/A.Api/Features/Invoices/Controllers/InvoicesController.cs"), "the controller a person edits\n");

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
            expect(text.Contains(fragment, StringComparison.Ordinal), $"{what} does not hold '{fragment}'.");

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

            expect(created.SequenceEqual(files.Order(StringComparer.Ordinal)), $"[{label}] wrote {string.Join(", ", created)}.");
            expect(before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value), $"[{label}] changed a file it does not own.");
            expect(!Directory.Exists(Path.Combine(root, _manifestFolder)), $"[{label}] wrote a manifest where the feature had none.");
            expect(result.Output.Contains("dotnet run Tools/Tasks.cs openapi", StringComparison.Ordinal), $"[{label}] did not print the OpenAPI step.");
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
        string manifest = Path.Combine(root, _manifestFolder, "Invoices.json");
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

        expect(created.Length == 9, $"Two use cases wrote {created.Length} files, not 9.");
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
        string brokenManifest = Path.Combine(broken, _manifestFolder, "Invoices.json");

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

        if (text.Contains("//", StringComparison.Ordinal) || text.Contains("/*", StringComparison.Ordinal))
        {
            yield return $"{relative}: a comment, where generated code carries none.";
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
