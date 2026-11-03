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
        _template.Value.Files.Keys.ShouldContain(
            path => path.StartsWith(".template.config/starters/", StringComparison.Ordinal),
            "No C# file of a presentation starter was read, so the audit judges none of the files they write.");
        _template.Value.Files.Keys.ShouldContain(
            path => path.StartsWith(".template.config/variants/", StringComparison.Ordinal),
            "No C# file of a variant was read, so the audit judges none of the files they write.");
        _template.Value.Declarations.ShouldNotBeEmpty("No file of the generated client was read, so no using of its namespaces is judged.");
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

    private sealed class SourceTree(IReadOnlyDictionary<string, string> files, IReadOnlyDictionary<string, string> declarations, IReadOnlySet<string> scaffoldedProjects)
    {
        private const string _generated = "Generated";

        private static readonly string[] _skipped = ["bin", "obj", "node_modules", ".git", ".claude", "Migrations", "Launcher", "Tools", "docs"];

        private static readonly Regex _dotnetScaffold = new(@"""kind""\s*:\s*""dotnet""", RegexOptions.None, TimeSpan.FromSeconds(1));

        internal IReadOnlyDictionary<string, string> Files { get; } = files;

        internal IReadOnlyDictionary<string, string> Declarations { get; } = declarations;

        internal IReadOnlySet<string> ScaffoldedProjects { get; } = scaffoldedProjects;

        internal static SourceTree FromText(IReadOnlyDictionary<string, string> files) =>
            new(files, new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

        internal static SourceTree FromDisk(string root)
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var declarations = new Dictionary<string, string>(StringComparer.Ordinal);
            var scaffolded = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>([root]);

            while (pending.TryPop(out string? directory))
            {
                string relative = Relative(root, directory);

                if (!Read(relative))
                {
                    continue;
                }

                string scaffold = Path.Combine(directory, "scaffold.json");

                if (File.Exists(scaffold) && _dotnetScaffold.IsMatch(File.ReadAllText(scaffold)))
                {
                    scaffolded.UnionWith(Directory.EnumerateFiles(directory, "*.csproj").Select(Path.GetFileNameWithoutExtension).OfType<string>());
                }

                var read = relative.Split('/').Contains(_generated, StringComparer.Ordinal) ? declarations : files;

                foreach (string file in Directory.EnumerateFiles(directory, "*.cs"))
                {
                    read[Relative(root, file)] = File.ReadAllText(file);
                }

                foreach (string child in Directory.EnumerateDirectories(directory).Where(child => !_skipped.Contains(Path.GetFileName(child), StringComparer.Ordinal)))
                {
                    pending.Push(child);
                }
            }

            return new SourceTree(files, declarations, scaffolded);
        }

        private static bool Read(string relative) => (relative + "/") switch
        {
            ".template.config/" => true,
            var folder when folder.StartsWith(".template.config/variants/refused/", StringComparison.Ordinal) => false,
            var folder when folder.StartsWith(".template.config/", StringComparison.Ordinal) =>
                folder.StartsWith(".template.config/variants/", StringComparison.Ordinal) || folder.StartsWith(".template.config/starters/", StringComparison.Ordinal),
            _ => true,
        };

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

        private static readonly Regex _using = new(@"^\s*using\s+(?<namespace>[A-Za-z_][\w.]*)\s*;", RegexOptions.None, _budget);

        private static readonly Regex _namespace = new(@"^\s*namespace\s+(?<namespace>[A-Za-z_][\w.]*)\s*[;{]?\s*$", RegexOptions.Multiline, _budget);

        private static readonly Regex _type = new(@"\b(?:class|record|struct|interface|enum)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.None, _budget);

        private static readonly Regex _delegate = new(@"\bdelegate\s+[^;(){}=]*?\b(?<name>[A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(", RegexOptions.None, _budget);

        private static readonly Regex _extension = new(@"\bstatic\s[^=;{}()]*?\b(?<name>[A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(\s*this\s", RegexOptions.None, _budget);

        private static readonly Regex _identifier = new(@"[A-Za-z_]\w*", RegexOptions.None, _budget);

        internal static HashSet<string> Symbols(SourceTree tree) =>
            tree.Files.Values.SelectMany(text => TemplateMarkers.Conditions(TemplateMarkers.Lines(text)).Values).SelectMany(condition => condition.Names).ToHashSet(StringComparer.Ordinal);

        internal static AuditResult Run(TemplateForms forms, SourceTree tree)
        {
            var provided = ProvidedNames(tree);
            var offenders = new SortedDictionary<string, string>(StringComparer.Ordinal);
            int markers = 0;
            int judged = 0;

            foreach (var (path, text) in tree.Files)
            {
                string[] lines = TemplateMarkers.Lines(text);
                var conditions = TemplateMarkers.Conditions(lines);

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

        private static bool IsJudged(string imported, Dictionary<string, HashSet<string>> provided, IReadOnlySet<string> scaffoldedProjects) =>
            IsWithin(imported, _projectNamespace)
            && provided.ContainsKey(imported)
            && !scaffoldedProjects.Any(project => IsWithin(imported, project));

        private static bool IsWithin(string space, string parent) =>
            space == parent || space.StartsWith(parent + ".", StringComparison.Ordinal);

        private static (List<string> Usings, HashSet<string> Read) Emit(string[] lines, Dictionary<int, Condition> conditions, Func<string, object> symbol)
        {
            var usings = new List<string>();
            var read = new HashSet<string>(StringComparer.Ordinal);

            foreach (string line in TemplateMarkers.Kept(lines, conditions, symbol))
            {
                Read(line, usings, read);
            }

            return (usings, read);
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

            foreach (string text in tree.Files.Values.Concat(tree.Declarations.Values))
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
}
