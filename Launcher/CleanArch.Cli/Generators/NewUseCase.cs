using System.Text.Json;
using System.Text.RegularExpressions;

namespace CleanArch.Cli.Generators;

internal static class NewUseCase
{
    private const string _usage =
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

    internal static readonly Regex _tokenPattern = new("%([A-Za-z]+)%", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static readonly Regex _controllerRoute = new(@"^\[Route\(""(?<template>[^""]*)""\)\]", RegexOptions.Multiline, TimeSpan.FromSeconds(2));

    private static readonly Regex _actionRoute = new(@"\[Http(?:Get|Post|Put|Patch|Delete|Head)\(""(?<template>[^""]*)""", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static readonly Regex _rootRoute = new(@"v\{version(?::\w+)?\}/?$", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static readonly Regex _versionSegment = new(@"v\{version(?::\w+)?\}", RegexOptions.None, TimeSpan.FromSeconds(2));

    private sealed record Emission(string Path, string Content);

    private sealed record Request(string? Features, string? Name, string? Kind, string? Input, string Root, bool DryRun);

    private sealed record KnownType(string? Valid, string? Empty);

    private sealed record Routing(string Resource, string Documented, string? Failure);

    internal sealed record Parameter(string Name, string Type, string Required)
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
        string? Manifest,
        Routing? Routing = null)
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

    internal static int Run(string[] args, TextWriter output, TextWriter error, Func<TextEdits.Command, int> run)
    {
        var problems = new List<string>();
        var request = Parse(args, problems);

        string root = Path.GetFullPath(request.Root);
        var spec = problems.Count == 0 ? Check(request, root, problems) : null;

        if (spec is null)
        {
            return Refuse(error, problems);
        }

        spec = spec with { Routing = ReadRouting(root, spec) };

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
            || TextEdits.RunAll(TextEdits.ContractSteps(root, FolderOf(spec.Prefix, "Presentation.Client"), built: false), run, output, error, "the use case's files, its action, its contracts and its route's row")
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

    internal static (bool HasId, List<Parameter> Inputs) ReadInputs(string input, List<string> problems)
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

    internal static string ProductPrefix(string root)
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

    internal static string FolderOf(string prefix, string project) =>
        string.Concat(prefix.Split('.').Select(SegmentInitials)) + "." + project;

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));

    private static bool IsVowel(char letter) => "aeiou".Contains(char.ToLowerInvariant(letter), StringComparison.Ordinal);

    internal static string Kebab(string pascal) => Separated(pascal, '-');

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

        Add($"{folder}/{spec.Name}.cs", UseCaseTemplates.UseCase);

        if (spec.HasValidator)
        {
            Add($"{folder}/{spec.Input}Validator.cs", UseCaseTemplates.Validator);
        }

        if (spec.Dto is not null && !spec.DtoExists)
        {
            Add($"{application}/Model/Dtos/{spec.Dto}.cs", spec.HasId ? UseCaseTemplates.DtoWithId : UseCaseTemplates.Dto);
        }

        skippedTests = null;

        if (spec.HasValidator)
        {
            string tests = $"Tests/Application/{FolderOf(spec.Prefix, "Application.UnitTests")}";
            string useCaseTests = $"{tests}/Features/{spec.Features}/UseCases/{spec.Folder}/{spec.Name}";

            if (Directory.Exists(Path.Combine(root, tests)))
            {
                Add($"{useCaseTests}/{spec.Input}ValidatorTests.cs", UseCaseTemplates.ValidatorTests);

                if (File.Exists(Path.Combine(root, tests, "TestDoubles", "ValidatedHandler.cs")))
                {
                    Add($"{useCaseTests}/{spec.Name}Tests.cs", UseCaseTemplates.HandlerTests);
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

        if (hasApi)
        {
            output.WriteLine("  - an error code of its own: its row in docs/reference/api.md, which the error-code tests read");
        }

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

            foreach (var step in TextEdits.ContractSteps(root, FolderOf(p, "Presentation.Client"), built: false))
            {
                output.WriteLine($"  {step.Display}");
            }
        }

        string written = spec.Routing?.Documented.StartsWith("/api/", StringComparison.Ordinal) == true
            ? "written in full, from /api"
            : "relative to that table's 'Under' prefix";

        output.WriteLine(mode == Mode.Applied
            ? $"and its row, added to the table of {spec.Features} under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads, {written}:"
            : $"and its row in the table of {spec.Features} under ## Routes in docs/reference/api.md, which ApiRouteTableTests reads, {written}:");
        output.WriteLine("  " + RouteRow(spec));

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

        if (spec.Routing?.Failure is { } failure)
        {
            editor.Fail(failure);
        }
        else if (editor.Open(controller) is { } sheet)
        {
            Action(editor, sheet, spec);
        }

        editor.Step = "the route's row in docs/reference/api.md";
        RouteRow(editor, spec);

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

        return spec.HasId ? $"{spec.Routing?.Resource ?? IdSegment(spec)}/{segment}" : segment;
    }

    private static string IdSegment(Spec spec) => $"{{{IdName(spec)}:guid}}";

    private static Routing? ReadRouting(string root, Spec spec)
    {
        string controller = Path.Combine(root, ApiFolder(spec), "Features", spec.Features, "Controllers", spec.Features + "Controller.cs");

        if (!File.Exists(controller))
        {
            return null;
        }

        string text = File.ReadAllText(controller);
        string route = _controllerRoute.Match(text) is { Success: true } declared ? declared.Groups["template"].Value : string.Empty;
        bool atTheRoot = _rootRoute.IsMatch(route);
        string documented = "/" + (atTheRoot ? _versionSegment.Replace(route, "v1").Trim('/') + "/" : string.Empty);

        if (!spec.HasId)
        {
            return atTheRoot
                ? new Routing(string.Empty, documented, "the controller is routed at the API root, where each action carries its resource path, and a use case without the aggregate's id gives no resource to take it from. Write the action by hand with its full route.")
                : null;
        }

        string id = IdSegment(spec);
        string? carrying = _actionRoute.Matches(text)
            .Select(action => action.Groups["template"].Value)
            .Where(template => template.Contains(id, StringComparison.Ordinal))
            .OrderBy(template => template.Length)
            .FirstOrDefault();

        if (carrying is null)
        {
            return atTheRoot
                ? new Routing(id, documented, $"the controller is routed at the API root, where each action carries its resource path, and none of its actions carries {id} to take the path from. Write the action by hand with its full route.")
                : null;
        }

        return new Routing(carrying[..(carrying.IndexOf(id, StringComparison.Ordinal) + id.Length)], documented, null);
    }

    private static string RouteRow(Spec spec)
    {
        string documented = (spec.Routing?.Documented ?? "/") + RouteTemplate(spec).Replace(":guid", string.Empty, StringComparison.Ordinal);

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
}
