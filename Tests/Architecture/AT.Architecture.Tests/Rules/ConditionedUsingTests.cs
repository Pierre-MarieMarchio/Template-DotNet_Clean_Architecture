using System.Text.Json;
using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ConditionedUsingTests
{
    private const string _fixture = "Tests/Application/AT.Application.UnitTests/FixtureModuleTests.cs";

    private const string _filesOptions = "AppTemplate.Application.Features.Files.Configuration.Options";

    private const string _registerFile = "AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile";

    private const string _issue250 =
        """
        //#if (files)
        using AppTemplate.Application.Features.Files.Configuration.Options;
        using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
        //#endif

        namespace AppTemplate.Application.UnitTests;

        public sealed class FixtureModuleTests
        {
            //#if (todoLists)
            //#if (files)
            private static readonly Type _registered = typeof(RegisterFileCommand);
            //#endif
            //#endif
            //#if (files)
            private static readonly Type _options = typeof(FilesOptions);
            //#endif
        }
        """;

    private const string _issue250Fixed =
        """
        //#if (files)
        using AppTemplate.Application.Features.Files.Configuration.Options;
        //#endif
        //#if (files && todoLists)
        using AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;
        //#endif

        namespace AppTemplate.Application.UnitTests;

        public sealed class FixtureModuleTests
        {
            //#if (todoLists)
            //#if (files)
            private static readonly Type _registered = typeof(RegisterFileCommand);
            //#endif
            //#endif
            //#if (files)
            private static readonly Type _options = typeof(FilesOptions);
            //#endif
        }
        """;

    private const string _readOnlyByAnElseifBranch =
        """
        using AppTemplate.Application.Features.Files.Configuration.Options;

        namespace AppTemplate.Application.UnitTests;

        public sealed class FixtureModuleTests
        {
            /*#if (todoLists)*/
            private const int _lists = 1;
            /*#elseif (files)
            private static readonly Type _options = typeof(FilesOptions);
            #else
            private const int _nothing = 0;
            #endif*/
        }
        """;

    private const string _keptOnlyWhereItsReaderIs =
        """
        //#if (reminders)
        using AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;
        //#endif

        namespace AppTemplate.Application.UnitTests;

        public sealed class FixtureModuleTests
        {
            //#if (todoLists)
            /*#if (reminders)
            private static readonly Type _scheduled = typeof(ScheduleReminderCommand);
            #endif*/
            //#endif
        }
        """;

    private const string _readThroughADelegate =
        """
        //#if (files)
        using AppTemplate.Application.Features.Files.Configuration.Options;
        //#endif

        namespace AppTemplate.Application.UnitTests;

        public sealed class FixtureModuleTests
        {
            //#if (files)
            private static readonly Type _policy = typeof(FilesPolicy);
            //#endif
        }
        """;

    private static readonly Dictionary<string, string> _fixtureDeclarations = new(StringComparer.Ordinal)
    {
        ["Src/Application/AT.Application/FilesOptions.cs"] = $"namespace {_filesOptions};\n\npublic sealed class FilesOptions;\n\npublic delegate string? FilesPolicy(string name);\n",
        ["Src/Application/AT.Application/RegisterFileCommand.cs"] = $"namespace {_registerFile};\n\npublic sealed record RegisterFileCommand;\n",
        ["Src/Application/AT.Application/ScheduleReminderCommand.cs"] =
            "namespace AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;\n\npublic sealed record ScheduleReminderCommand;\n",
    };

    private static readonly Lazy<SourceTree> _template = new(() => SourceTree.FromDisk(ProjectReferenceGraph.RepositoryRoot));

    private static readonly Lazy<TemplateForms> _forms = new(() => TemplateForms.Load(ProjectReferenceGraph.RepositoryRoot, UsingAudit.Symbols(_template.Value)));

    [Fact]
    public void EveryUsingOfAProjectNamespace_IsReadInEachFormThatKeepsIt()
    {
        var audit = UsingAudit.Run(_forms.Value, _template.Value);

        audit.FilesWithMarkers.ShouldBeGreaterThan(0, "No C# file of the template carries a marker, so this rule reads the wrong tree.");
        audit.Judged.ShouldBeGreaterThan(0, "No using of a project namespace was judged, so the audit reads no declaration.");
        audit.Offenders.ShouldBeEmpty(
            "A using of the project's own namespaces stays in a generated form whose lines read nothing it brings in, so IDE0005 "
            + "fails that form's build, which no acceptance leg may generate. Put the using under the condition of the lines that "
            + "read it, as docs/template/maintaining-the-template.md says under 'Usings under a marker'.");
    }

    [Fact]
    public void TheAudit_FiresOnTheImportAFilesOnlyProjectKeptUnread_AndStaysSilentOnItsFix()
    {
        Offenders(_issue250).ShouldBe(
            [$"{_fixture}: {_registerFile}, read by no line under --examples notes"],
            "The audit does not fire on the import of RegisterFile kept under files alone and read under files && todoLists only.");
        Offenders(_issue250Fixed).ShouldBeEmpty("The audit fires on an import that follows the condition of the lines that read it.");
    }

    [Fact]
    public void TheAudit_FiresOnAUsingOutsideEveryBlock_ThatOnlyAnElseifBranchReads()
    {
        Offenders(_readOnlyByAnElseifBranch).ShouldBe(
            [$"{_fixture}: {_filesOptions}, read by no line under the defaults"],
            "The audit does not take an #elseif branch only when the branches before it are not taken.");
    }

    [Fact]
    public void TheAudit_ReadsADelegateAsATypeTheUsingBringsIn()
    {
        Offenders(_readThroughADelegate).ShouldBeEmpty("The audit does not read a delegate declaration as a type its namespace provides.");
    }

    [Fact]
    public void TheAudit_JudgesOnlyTheFormsTemplateJsonCanGenerate()
    {
        Offenders(_keptOnlyWhereItsReaderIs).ShouldBeEmpty(
            "reminders holds only where todoLists does, so no generated form keeps this using without the line that reads it; "
            + "the audit judged a form template.json cannot generate.");
    }

    [Fact]
    public void TheConditionReader_ReadsTheTemplateSyntax()
    {
        var symbols = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["files"] = true,
            ["auth"] = "local",
            ["presentation"] = new[] { "api", "worker" },
        };
        bool Holds(string text) => (bool)Condition.Parse(text).Evaluate(name => symbols[name]);

        Holds("(files && auth == \"local\")").ShouldBeTrue();
        Holds("(!files || auth != \"local\")").ShouldBeFalse();
        Holds("(presentation == \"worker\")").ShouldBeTrue("a multi-valued option equals each of its values");
        Holds("(presentation != \"mcp\" && !(auth == \"none\"))").ShouldBeTrue();
        Condition.Parse("(a || b && !c)").Names.Order(StringComparer.Ordinal).ShouldBe(["a", "b", "c"]);
        Should.Throw<FormatException>(() => Condition.Parse("(files &&)"));
        Should.Throw<FormatException>(() => Condition.Parse("(files"));
    }

    private static List<string> Offenders(string fixture) =>
        UsingAudit.Run(_forms.Value, SourceTree.FromText(new Dictionary<string, string>(_fixtureDeclarations, StringComparer.Ordinal) { [_fixture] = fixture })).Offenders;

    private sealed record AuditResult(int FilesWithMarkers, int Judged, List<string> Offenders);

    private sealed class SourceTree(IReadOnlyDictionary<string, string> files, IReadOnlySet<string> scaffoldedProjects)
    {
        private static readonly string[] _skipped = ["bin", "obj", "node_modules", ".git", ".claude", "Generated", "Migrations", "Launcher", "Tools", "docs"];

        private static readonly Regex _dotnetScaffold = new(@"""kind""\s*:\s*""dotnet""", RegexOptions.None, TimeSpan.FromSeconds(1));

        internal IReadOnlyDictionary<string, string> Files { get; } = files;

        internal IReadOnlySet<string> ScaffoldedProjects { get; } = scaffoldedProjects;

        internal static SourceTree FromText(IReadOnlyDictionary<string, string> files) =>
            new(files, new HashSet<string>(StringComparer.Ordinal));

        internal static SourceTree FromDisk(string root)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var scaffolded = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>([root]);

            while (pending.TryPop(out string? directory))
            {
                if (!Read(Relative(root, directory)))
                {
                    continue;
                }

                string scaffold = Path.Combine(directory, "scaffold.json");

                if (File.Exists(scaffold) && _dotnetScaffold.IsMatch(File.ReadAllText(scaffold)))
                {
                    scaffolded.UnionWith(Directory.EnumerateFiles(directory, "*.csproj").Select(Path.GetFileNameWithoutExtension).OfType<string>());
                }

                foreach (string file in Directory.EnumerateFiles(directory, "*.cs"))
                {
                    files[Relative(root, file)] = File.ReadAllText(file);
                }

                foreach (string child in Directory.EnumerateDirectories(directory).Where(child => !_skipped.Contains(Path.GetFileName(child), StringComparer.Ordinal)))
                {
                    pending.Push(child);
                }
            }

            return new SourceTree(files, scaffolded);
        }

        private static bool Read(string relative) =>
            !relative.StartsWith(".template.config/", StringComparison.Ordinal)
            || ((relative.StartsWith(".template.config/variants/", StringComparison.Ordinal) || relative.StartsWith(".template.config/starters/", StringComparison.Ordinal))
                && !relative.StartsWith(".template.config/variants/refused", StringComparison.Ordinal));

        private static string Relative(string root, string path)
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            return relative == "." ? "" : relative;
        }
    }

    private static class UsingAudit
    {
        private const string _projectNamespace = "AppTemplate";

        private const string _attribute = "Attribute";

        private static readonly TimeSpan _budget = TimeSpan.FromSeconds(5);

        private static readonly Regex _directive = new(@"^\s*(?://|/\*)?#(?<kind>if|elseif|elif|else|endif)\b(?<condition>.*)$", RegexOptions.None, _budget);

        private static readonly Regex _using = new(@"^\s*using\s+(?<namespace>[A-Za-z_][\w.]*)\s*;", RegexOptions.None, _budget);

        private static readonly Regex _namespace = new(@"^\s*namespace\s+(?<namespace>[A-Za-z_][\w.]*)\s*[;{]?\s*$", RegexOptions.Multiline, _budget);

        private static readonly Regex _type = new(@"\b(?:class|record|struct|interface|enum)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.None, _budget);

        private static readonly Regex _delegate = new(@"\bdelegate\s+[^;(){}=]*?\b(?<name>[A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.None, _budget);

        private static readonly Regex _extension = new(@"\bstatic\s[^=;{}()]*?\b(?<name>[A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(\s*this\s", RegexOptions.None, _budget);

        private static readonly Regex _identifier = new(@"[A-Za-z_]\w*", RegexOptions.None, _budget);

        internal static HashSet<string> Symbols(SourceTree tree) =>
            tree.Files.Values.SelectMany(text => Conditions(Lines(text)).Values).SelectMany(condition => condition.Names).ToHashSet(StringComparer.Ordinal);

        internal static AuditResult Run(TemplateForms forms, SourceTree tree)
        {
            var provided = ProvidedNames(tree);
            var offenders = new SortedDictionary<string, string>(StringComparer.Ordinal);
            int markers = 0;
            int judged = 0;

            foreach (var (path, text) in tree.Files)
            {
                string[] lines = Lines(text);
                var conditions = Conditions(lines);

                if (conditions.Count == 0 && !path.StartsWith(".template.config/", StringComparison.Ordinal))
                {
                    continue;
                }

                markers += conditions.Count > 0 ? 1 : 0;

                foreach (var form in forms.FormsOf(path, conditions.Values.SelectMany(condition => condition.Names)))
                {
                    var (usings, read) = Emit(lines, conditions, form.Value);

                    foreach (string imported in usings.Where(imported => IsJudged(imported, provided, tree.ScaffoldedProjects)))
                    {
                        judged++;

                        if (!provided[imported].Overlaps(read))
                        {
                            offenders.TryAdd($"{path}: {imported}", form.Options);
                        }
                    }
                }
            }

            return new AuditResult(markers, judged, [.. offenders.Select(offender => $"{offender.Key}, read by no line under {offender.Value}")]);
        }

        private static string[] Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        private static bool IsJudged(string imported, Dictionary<string, HashSet<string>> provided, IReadOnlySet<string> scaffoldedProjects) =>
            IsWithin(imported, _projectNamespace)
            && provided.ContainsKey(imported)
            && !scaffoldedProjects.Any(project => IsWithin(imported, project));

        private static bool IsWithin(string space, string parent) =>
            space == parent || space.StartsWith(parent + ".", StringComparison.Ordinal);

        private static Dictionary<int, Condition> Conditions(string[] lines)
        {
            var conditions = new Dictionary<int, Condition>();

            for (int index = 0; index < lines.Length; index++)
            {
                if (_directive.Match(lines[index]) is { Success: true } directive && directive.Groups["kind"].Value is "if" or "elseif" or "elif")
                {
                    string condition = directive.Groups["condition"].Value.Trim();
                    conditions[index] = Condition.Parse(condition.EndsWith("*/", StringComparison.Ordinal) ? condition[..^2] : condition);
                }
            }

            return conditions;
        }

        private static (List<string> Usings, HashSet<string> Read) Emit(string[] lines, Dictionary<int, Condition> conditions, Func<string, object> symbol)
        {
            var branches = new Stack<(bool Active, bool Taken)>();
            var usings = new List<string>();
            var read = new HashSet<string>(StringComparer.Ordinal);

            for (int index = 0; index < lines.Length; index++)
            {
                if (_directive.Match(lines[index]) is { Success: true } directive)
                {
                    Branch(branches, directive.Groups["kind"].Value, conditions.TryGetValue(index, out var condition) ? () => (bool)condition.Evaluate(symbol) : () => false);
                }
                else if (branches.All(branch => branch.Active))
                {
                    Read(lines[index], usings, read);
                }
            }

            return (usings, read);
        }

        private static void Branch(Stack<(bool Active, bool Taken)> branches, string kind, Func<bool> holds)
        {
            switch (kind)
            {
                case "if":
                    bool active = holds();
                    branches.Push((active, active));
                    break;
                case "elseif" or "elif":
                    var (_, taken) = branches.Pop();
                    bool chosen = !taken && holds();
                    branches.Push((chosen, taken || chosen));
                    break;
                case "else":
                    var (_, before) = branches.Pop();
                    branches.Push((!before, true));
                    break;
                default:
                    branches.Pop();
                    break;
            }
        }

        private static void Read(string line, List<string> usings, HashSet<string> read)
        {
            string trimmed = line.TrimStart();

            if (_using.Match(line) is { Success: true } imported)
            {
                usings.Add(imported.Groups["namespace"].Value);
            }
            else if (!trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                read.UnionWith(_identifier.Matches(line).Select(match => match.Value));
            }
        }

        private static Dictionary<string, HashSet<string>> ProvidedNames(SourceTree tree)
        {
            var provided = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            foreach (string text in tree.Files.Values)
            {
                if (_namespace.Match(text) is not { Success: true } declared)
                {
                    continue;
                }

                string space = declared.Groups["namespace"].Value;
                var names = Names(provided, space);
                string[] declaredNames =
                [
                    .. _type.Matches(text).Select(match => match.Groups["name"].Value),
                    .. _delegate.Matches(text).Select(match => match.Groups["name"].Value),
                    .. _extension.Matches(text).Select(match => match.Groups["name"].Value),
                ];
                names.UnionWith(declaredNames);
                names.UnionWith(declaredNames.Where(name => name.Length > _attribute.Length && name.EndsWith(_attribute, StringComparison.Ordinal)).Select(name => name[..^_attribute.Length]));

                string[] segments = space.Split('.');

                for (int length = 1; length < segments.Length; length++)
                {
                    Names(provided, string.Join('.', segments[..length])).Add(segments[length]);
                }
            }

            return provided;
        }

        private static HashSet<string> Names(Dictionary<string, HashSet<string>> provided, string space)
        {
            if (!provided.TryGetValue(space, out var names))
            {
                names = new HashSet<string>(StringComparer.Ordinal);
                provided[space] = names;
            }

            return names;
        }
    }

    private sealed record Form(Func<string, object> Value, string Options);

    private sealed class TemplateForms
    {
        private const string _refused = "refusedOptions";

        private readonly Dictionary<string, Condition> _computed;

        private readonly List<(string Name, object[] Domain)> _parameters;

        private readonly List<Source> _sources;

        private readonly Lazy<(string[] Symbols, List<(ulong Vector, long Combination)> Vectors)> _forms;

        private readonly Dictionary<ulong, List<(ulong Vector, long Combination)>> _byMask = new();

        private TemplateForms(Dictionary<string, Condition> computed, List<(string Name, object[] Domain)> parameters, List<Source> sources, IReadOnlyCollection<string> fileSymbols)
        {
            _computed = computed;
            _parameters = parameters;
            _sources = sources;
            _forms = new(() => Enumerate(fileSymbols));
        }

        internal static TemplateForms Load(string root, IReadOnlyCollection<string> fileSymbols)
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".template.config", "template.json")));
            var computed = new Dictionary<string, Condition>(StringComparer.Ordinal);
            var parameters = new List<(string Name, object[] Domain)>();

            foreach (var symbol in manifest.RootElement.GetProperty("symbols").EnumerateObject())
            {
                string type = symbol.Value.GetProperty("type").GetString() ?? "";

                if (type == "computed")
                {
                    computed[symbol.Name] = Condition.Parse(symbol.Value.GetProperty("value").GetString() ?? "");
                }
                else if (type == "parameter" && Domain(symbol.Value) is { Length: > 1 } domain)
                {
                    parameters.Add((symbol.Name, domain));
                }
            }

            parameters.ShouldNotBeEmpty("template.json declares no option, so the audit enumerates no form.");

            return new TemplateForms(computed, parameters, [.. manifest.RootElement.GetProperty("sources").EnumerateArray().Select(Source.Read)], fileSymbols);
        }

        internal IEnumerable<Form> FormsOf(string path, IEnumerable<string> fileSymbols)
        {
            var (symbols, _) = _forms.Value;
            var generating = _sources.Where(source => source.Holds(path)).ToList();
            var read = fileSymbols.Concat(generating.SelectMany(source => source.NamesFor(path))).Where(name => name != _refused).ToHashSet(StringComparer.Ordinal);

            read.Except(symbols, StringComparer.Ordinal).ShouldBeEmpty($"{path} reads symbols the audit does not enumerate.");

            ulong mask = symbols.Select((name, bit) => read.Contains(name) ? 1UL << bit : 0UL).Aggregate(0UL, (all, bit) => all | bit);

            foreach (var (vector, combination) in Representatives(mask))
            {
                object Value(string name) => name != _refused && ((vector >> Array.IndexOf(symbols, name)) & 1UL) == 1UL;

                if (generating.Any(source => source.Generates(path, Value)))
                {
                    yield return new Form(Value, Options(combination));
                }
            }
        }

        private List<(ulong Vector, long Combination)> Representatives(ulong mask)
        {
            if (!_byMask.TryGetValue(mask, out var representatives))
            {
                var seen = new HashSet<ulong>();
                representatives = [.. _forms.Value.Vectors.Where(form => seen.Add(form.Vector & mask))];
                _byMask[mask] = representatives;
            }

            return representatives;
        }

        private (string[] Symbols, List<(ulong Vector, long Combination)> Vectors) Enumerate(IReadOnlyCollection<string> fileSymbols)
        {
            string[] symbols = [.. fileSymbols.Concat(_sources.SelectMany(source => source.AllNames)).Where(name => name != _refused).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

            symbols.Length.ShouldBeLessThanOrEqualTo(64, "The audit packs a form's symbols into 64 bits.");

            var seen = new HashSet<ulong>();
            var vectors = new List<(ulong Vector, long Combination)>();
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            long count = _parameters.Aggregate(1L, (product, parameter) => product * parameter.Domain.Length);

            object Symbol(string name)
            {
                if (!values.TryGetValue(name, out object? value))
                {
                    value = _computed.TryGetValue(name, out var condition) ? condition.Evaluate(Symbol) : false;
                    values[name] = value;
                }

                return value;
            }

            for (long combination = 0; combination < count; combination++)
            {
                Assign(values, combination);

                if ((bool)Symbol(_refused))
                {
                    continue;
                }

                ulong vector = 0;

                for (int bit = 0; bit < symbols.Length; bit++)
                {
                    vector |= Symbol(symbols[bit]) is true ? 1UL << bit : 0UL;
                }

                if (seen.Add(vector))
                {
                    vectors.Add((vector, combination));
                }
            }

            return (symbols, vectors);
        }

        private void Assign(Dictionary<string, object> values, long combination)
        {
            values.Clear();

            foreach (var (name, domain) in _parameters)
            {
                values[name] = domain[combination % domain.Length];
                combination /= domain.Length;
            }
        }

        private string Options(long combination)
        {
            var options = new List<string>();

            foreach (var (name, domain) in _parameters)
            {
                long index = combination % domain.Length;
                combination /= domain.Length;

                if (index > 0)
                {
                    options.Add(domain[index] switch
                    {
                        string[] values => string.Join(' ', values.Select(value => $"--{Kebab(name)} {value}")),
                        bool flag => $"--{Kebab(name)} {(flag ? "true" : "false")}",
                        var value => $"--{Kebab(name)} {value}",
                    });
                }
            }

            return options.Count == 0 ? "the defaults" : string.Join(' ', options);
        }

        private static string Kebab(string name) =>
            Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", "-", RegexOptions.None, TimeSpan.FromSeconds(1)).ToLowerInvariant();

        private static object[] Domain(JsonElement parameter)
        {
            string datatype = parameter.GetProperty("datatype").GetString() ?? "";
            string fallback = parameter.TryGetProperty("defaultValue", out var given) ? given.GetString() ?? "" : "";

            if (datatype == "bool")
            {
                bool byDefault = fallback == "true";
                return [byDefault, !byDefault];
            }

            if (datatype != "choice")
            {
                return [];
            }

            string[] choices = [.. parameter.GetProperty("choices").EnumerateArray().Select(choice => choice.GetProperty("choice").GetString() ?? "")];
            string[] ordered = [.. choices.Where(choice => choice == fallback), .. choices.Where(choice => choice != fallback)];

            return parameter.TryGetProperty("allowMultipleValues", out var multiple) && multiple.GetBoolean()
                ? [.. Subsets(ordered)]
                : ordered;
        }

        private static IEnumerable<object> Subsets(string[] choices) =>
            Enumerable.Range(1, (1 << choices.Length) - 1)
                .Select(bits => choices.Where((_, index) => ((bits >> index) & 1) == 1).ToArray());
    }

    private sealed record Source(string Prefix, Condition? When, Regex[] Include, Regex[] Exclude, (Condition? When, Regex[] Exclude)[] Modifiers)
    {
        internal IEnumerable<string> AllNames =>
            (When?.Names ?? Enumerable.Empty<string>()).Concat(Modifiers.SelectMany(modifier => modifier.When?.Names ?? Enumerable.Empty<string>()));

        internal static Source Read(JsonElement source)
        {
            string folder = source.TryGetProperty("source", out var given) ? given.GetString() ?? "./" : "./";

            return new Source(
                folder.StartsWith("./", StringComparison.Ordinal) ? folder[2..] : folder,
                ConditionOf(source),
                Globs(source, "include"),
                Globs(source, "exclude"),
                [.. Elements(source, "modifiers").Select(modifier => (ConditionOf(modifier), Globs(modifier, "exclude")))]);
        }

        internal bool Holds(string path) =>
            path.StartsWith(Prefix, StringComparison.Ordinal)
            && (Include.Length == 0 || Include.Any(glob => glob.IsMatch(path[Prefix.Length..])))
            && !Exclude.Any(glob => glob.IsMatch(path[Prefix.Length..]));

        internal IEnumerable<string> NamesFor(string path) =>
            (When?.Names ?? Enumerable.Empty<string>()).Concat(Modifiers
                .Where(modifier => modifier.Exclude.Any(glob => glob.IsMatch(path[Prefix.Length..])))
                .SelectMany(modifier => modifier.When?.Names ?? Enumerable.Empty<string>()));

        internal bool Generates(string path, Func<string, object> symbol) =>
            (When is null || (bool)When.Evaluate(symbol))
            && !Modifiers.Any(modifier => modifier.Exclude.Any(glob => glob.IsMatch(path[Prefix.Length..]))
                && (modifier.When is null || (bool)modifier.When.Evaluate(symbol)));

        private static IEnumerable<JsonElement> Elements(JsonElement element, string key) =>
            element.TryGetProperty(key, out var array) ? [.. array.EnumerateArray()] : [];

        private static Condition? ConditionOf(JsonElement element) =>
            element.TryGetProperty("condition", out var condition) ? Condition.Parse(condition.GetString() ?? "") : null;

        private static Regex[] Globs(JsonElement element, string key) =>
            [.. Elements(element, key).Select(glob => Glob(glob.GetString() ?? ""))];

        private static Regex Glob(string pattern)
        {
            string body = Regex.Escape(pattern.StartsWith("./", StringComparison.Ordinal) ? pattern[2..] : pattern)
                .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
                .Replace(@"\*\*", ".*", StringComparison.Ordinal)
                .Replace(@"\*", "[^/]*", StringComparison.Ordinal);

            return new Regex($"^{body}$", RegexOptions.None, TimeSpan.FromSeconds(1));
        }
    }

    private sealed class Condition
    {
        private static readonly Regex _token = new(@"\G\s*(?:""(?<text>[^""]*)""|(?<operator>&&|\|\||==|!=|!|\(|\))|(?<name>[A-Za-z_]\w*))\s*", RegexOptions.None, TimeSpan.FromSeconds(1));

        private readonly Func<Func<string, object>, object> _evaluate;

        private Condition(Func<Func<string, object>, object> evaluate, IReadOnlySet<string> names)
        {
            _evaluate = evaluate;
            Names = names;
        }

        internal IReadOnlySet<string> Names { get; }

        internal object Evaluate(Func<string, object> symbol) => _evaluate(symbol);

        internal static Condition Parse(string text)
        {
            var tokens = new List<(string Kind, string Value)>();
            string condition = text.Trim();

            for (int position = 0; position < condition.Length;)
            {
                var token = _token.Match(condition, position);

                if (!token.Success || token.Length == 0)
                {
                    throw new FormatException($"'{text}' is not a template condition.");
                }

                tokens.Add(token.Groups["text"].Success ? ("text", token.Groups["text"].Value)
                    : token.Groups["operator"].Success ? ("operator", token.Groups["operator"].Value)
                    : ("name", token.Groups["name"].Value));
                position += token.Length;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var parser = new Parser(tokens, names);
            var evaluate = parser.Or();

            return parser.AtEnd ? new Condition(evaluate, names) : throw new FormatException($"'{text}' is not a template condition.");
        }

        private sealed class Parser(List<(string Kind, string Value)> tokens, HashSet<string> names)
        {
            private int _next;

            internal bool AtEnd => _next == tokens.Count;

            internal Func<Func<string, object>, object> Or()
            {
                var left = And();

                while (Takes("||"))
                {
                    var (first, second) = (left, And());
                    left = symbol => (bool)first(symbol) || (bool)second(symbol);
                }

                return left;
            }

            private Func<Func<string, object>, object> And()
            {
                var left = Comparison();

                while (Takes("&&"))
                {
                    var (first, second) = (left, Comparison());
                    left = symbol => (bool)first(symbol) && (bool)second(symbol);
                }

                return left;
            }

            private Func<Func<string, object>, object> Comparison()
            {
                var left = Unary();
                bool equal = Takes("==");

                if (equal || Takes("!="))
                {
                    var right = Unary();
                    return symbol => Same(left(symbol), right(symbol)) == equal;
                }

                return left;
            }

            private Func<Func<string, object>, object> Unary()
            {
                if (Takes("!"))
                {
                    var operand = Unary();
                    return symbol => !(bool)operand(symbol);
                }

                if (Takes("("))
                {
                    var inner = Or();
                    return Takes(")") ? inner : throw new FormatException("A parenthesis of a template condition is not closed.");
                }

                if (AtEnd || tokens[_next].Kind == "operator")
                {
                    throw new FormatException("A template condition lacks an operand.");
                }

                var (kind, value) = tokens[_next++];

                if (kind == "text" || value is "true" or "false")
                {
                    object constant = kind == "text" ? value : value == "true";
                    return _ => constant;
                }

                names.Add(value);
                return symbol => symbol(value);
            }

            private bool Takes(string symbol)
            {
                if (!AtEnd && tokens[_next] is ("operator", var value) && value == symbol)
                {
                    _next++;
                    return true;
                }

                return false;
            }

            private static bool Same(object left, object right) => (left, right) switch
            {
                (string[] values, string value) => values.Contains(value, StringComparer.Ordinal),
                (string value, string[] values) => values.Contains(value, StringComparer.Ordinal),
                (bool flag, string value) => string.Equals(flag ? "true" : "false", value, StringComparison.Ordinal),
                _ => Equals(left, right),
            };
        }
    }
}
