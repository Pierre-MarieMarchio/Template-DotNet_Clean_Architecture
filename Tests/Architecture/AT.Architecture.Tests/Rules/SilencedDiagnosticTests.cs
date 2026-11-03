using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class SilencedDiagnosticTests
{
    private const string _anyDiagnostic = "*";

    private const string _tableHeader = "| Diagnostic | Where | Why |";

    private static readonly string _root = ProjectReferenceGraph.RepositoryRoot;

    private static readonly string _page = Path.Combine("docs", "guides", "build-and-ci.md");

    private static readonly HashSet<string> _skippedDirectories =
        new(["bin", "obj", "node_modules", ".git", ".nuget", ".claude", ".vs", ".idea", "artifacts", "TestResults"], StringComparer.Ordinal);

    private static readonly string[] _projectExtensions = [".csproj", ".props", ".targets"];

    private static readonly string[] _scriptExtensions = [".yml", ".yaml", ".sh", ".ps1"];

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

    private static readonly Regex _pragma = new(
        @"^[ \t]*#pragma[ \t]+warning[ \t]+disable\b(?<ids>[^\r\n]*)",
        RegexOptions.Multiline,
        _timeout);

    private static readonly Regex _nullableDisable = new(
        @"^[ \t]*#nullable[ \t]+disable\b",
        RegexOptions.Multiline,
        _timeout);

    private static readonly Regex _suppressMessage = new(
        @"^[ \t]*\[(?:assembly:[ \t]*|module:[ \t]*)?(?:[\w.]+\.)?(?:Unconditional)?SuppressMessage(?:Attribute)?[ \t]*\([ \t]*""[^""]*""[ \t]*,[ \t]*""(?<id>[^"":]+)",
        RegexOptions.Multiline,
        _timeout);

    private static readonly Regex _skippedTest = new(
        @"^[ \t]*\[(?:Fact|Theory|InlineData)\b[^\r\n]*\bSkip[ \t]*=",
        RegexOptions.Multiline,
        _timeout);

    private static readonly Regex _fileBasedProperty = new(
        @"^#:property[ \t]+(?:(?<name>NoWarn|WarningsNotAsErrors)=(?<ids>[^\r\n]*)|(?<name>TreatWarningsAsErrors)=false\b)",
        RegexOptions.Multiline | RegexOptions.IgnoreCase,
        _timeout);

    private static readonly Regex _projectList = new(
        @"<(?<name>NoWarn|WarningsNotAsErrors)>(?<ids>[^<]*)</|\bNoWarn=""(?<ids>[^""]*)""",
        RegexOptions.None,
        _timeout);

    private static readonly Regex _projectSwitch = new(
        @"<(?<name>TreatWarningsAsErrors|CodeAnalysisTreatWarningsAsErrors|EnforceCodeStyleInBuild|EnableNETAnalyzers|RunAnalyzers|RunAnalyzersDuringBuild)>[ \t]*false[ \t]*<"
        + @"|<(?<name>AnalysisLevel|AnalysisMode[A-Za-z]*)>[ \t]*(?:[\w.]+-)?none[ \t]*<",
        RegexOptions.IgnoreCase,
        _timeout);

    private static readonly Regex _commandLine = new(
        @"[-/]p(?:roperty)?:(?<name>NoWarn|WarningsNotAsErrors)="
        + @"|[-/]p(?:roperty)?:(?<name>TreatWarningsAsErrors)=false\b"
        + @"|(?<![\w-])(?<name>-nowarn|-warnaserror-)(?![\w-])",
        RegexOptions.IgnoreCase,
        _timeout);

    private static readonly Regex _section = new(@"^[ \t]*\[(?<glob>.+)\][ \t]*$", RegexOptions.None, _timeout);

    private static readonly Regex _loweredSeverity = new(
        @"^[ \t]*(?:dotnet_diagnostic\.(?<id>[\w-]+)|dotnet_analyzer_diagnostic(?:\.(?<id>category-[\w-]+))?)\.severity[ \t]*=[ \t]*(?:none|silent|suggestion)\b",
        RegexOptions.IgnoreCase,
        _timeout);

    private static readonly Regex _generatedCode = new(
        @"^[ \t]*generated_code[ \t]*=[ \t]*true\b",
        RegexOptions.IgnoreCase,
        _timeout);

    private static readonly Regex _codeSpan = new(@"`(?<span>[^`\n]+)`", RegexOptions.None, _timeout);

    private static readonly Regex _diagnosticList = new(@"[;,\s]+", RegexOptions.None, _timeout);

    private sealed record Silence(string Diagnostic, string Where);

    private sealed record EditorConfigSection(string Holder, string Glob, string Directory, List<string> Lines);

    [Fact]
    public void EverySilencedDiagnostic_HasItsRowInTheGatesList()
    {
        var listed = ListedSilences();
        var found = FoundSilences();

        found.Count.ShouldBeGreaterThanOrEqualTo(
            5,
            "Far fewer silenced diagnostics were found than this project holds, so this rule is reading the wrong tree.");

        string[] unlisted = [.. found.Except(listed).Select(Describe).Order(StringComparer.Ordinal)];

        unlisted.ShouldBeEmpty(
            $"A diagnostic is silenced only by a row of the table under 'The gate' in {_page}, with its reason. "
            + "Fix the code each line names, or give the silence its row.");
    }

    [Fact]
    public void EveryRowOfTheGatesList_StillSilencesSomething()
    {
        var found = FoundSilences();

        string[] dead = [.. ListedSilences().Except(found).Select(Describe).Order(StringComparer.Ordinal)];

        dead.ShouldBeEmpty(
            $"These rows of the table under 'The gate' in {_page} silence nothing in the tree any more: remove each one.");
    }

    [Fact]
    public void EveryEditorConfigSectionThatSilences_NamesAFileTheTreeHolds()
    {
        string[] missing =
        [
            .. EditorConfigSections()
                .Where(section => section.Lines.Any(line => _loweredSeverity.IsMatch(line) || _generatedCode.IsMatch(line)))
                .Where(section => section.Glob.IndexOfAny(['*', '?', '{', '[']) < 0)
                .Where(section => !File.Exists(Path.Combine(section.Directory, section.Glob.TrimStart('/'))))
                .Select(section => $"{section.Holder} [{section.Glob}]")
                .Order(StringComparer.Ordinal),
        ];

        missing.ShouldBeEmpty(
            "These sections silence a diagnostic in a file this tree does not hold: remove each one, or put it under the "
            + "template condition that removes its file.");
    }

    [Fact]
    public void EveryRowOfTheGatesList_GivesItsReason()
    {
        string[] bare = [.. TableRows().Where(cells => cells[2].Trim().Length == 0).Select(cells => cells[1].Trim())];

        TableRows().Count.ShouldBeGreaterThanOrEqualTo(5, $"{_page} holds no table headed '{_tableHeader}' to read.");
        bare.ShouldBeEmpty("A row of the gate's list without its reason is a silence nobody can weigh.");
    }

    [Fact]
    public void TheSourceForms_AreSeen()
    {
        DiagnosticsIn(_pragma, "#pragma warning disable CA1000, S4136 // the reason").ShouldBe(["CA1000", "S4136"]);
        DiagnosticsIn(_pragma, "    #pragma warning disable").ShouldBe([_anyDiagnostic]);
        _pragma.IsMatch("#pragma warning restore CA1000").ShouldBeFalse();
        _pragma.IsMatch("var text = \"#pragma warning disable CA1000\";").ShouldBeFalse();

        _nullableDisable.IsMatch("#nullable disable").ShouldBeTrue();
        _nullableDisable.IsMatch("#nullable disable warnings").ShouldBeTrue();
        _nullableDisable.IsMatch("#nullable enable").ShouldBeFalse();

        DiagnosticsIn(_suppressMessage, "[SuppressMessage(\"Design\", \"CA1000:Do not declare static members\")]").ShouldBe(["CA1000"]);
        DiagnosticsIn(_suppressMessage, "[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(\"Sonar\", \"S107\")]").ShouldBe(["S107"]);
        DiagnosticsIn(_suppressMessage, "[UnconditionalSuppressMessageAttribute(\"Trimming\", \"IL2026\")]").ShouldBe(["IL2026"]);

        _skippedTest.IsMatch("    [Fact(Skip = \"later\")]").ShouldBeTrue();
        _skippedTest.IsMatch("    [InlineData(1, Skip = \"later\")]").ShouldBeTrue();
        _skippedTest.IsMatch("    [Fact]").ShouldBeFalse();

        DiagnosticsIn(_fileBasedProperty, "#:property NoWarn=CS1591;CA1848").ShouldBe(["CS1591", "CA1848"]);
        DiagnosticsIn(_fileBasedProperty, "#:property TreatWarningsAsErrors=false").ShouldBe(["TreatWarningsAsErrors"]);
        _fileBasedProperty.IsMatch("#:property UseAppHost=false").ShouldBeFalse();
    }

    [Fact]
    public void TheProjectAndCommandLineForms_AreSeen()
    {
        DiagnosticsIn(_projectList, "<NoWarn>$(NoWarn);CS1591;CS1573</NoWarn>").ShouldBe(["CS1591", "CS1573"]);
        DiagnosticsIn(_projectList, "<WarningsNotAsErrors>NU1901</WarningsNotAsErrors>").ShouldBe(["NU1901"]);
        DiagnosticsIn(_projectList, "<PackageReference Include=\"A\" NoWarn=\"NU1701\" />").ShouldBe(["NU1701"]);

        DiagnosticsIn(_projectSwitch, "<TreatWarningsAsErrors>false</TreatWarningsAsErrors>").ShouldBe(["TreatWarningsAsErrors"]);
        DiagnosticsIn(_projectSwitch, "<RunAnalyzers>false</RunAnalyzers>").ShouldBe(["RunAnalyzers"]);
        DiagnosticsIn(_projectSwitch, "<AnalysisLevel>latest-none</AnalysisLevel>").ShouldBe(["AnalysisLevel"]);
        _projectSwitch.IsMatch("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>").ShouldBeFalse();
        _projectSwitch.IsMatch("<AnalysisLevel>latest-recommended</AnalysisLevel>").ShouldBeFalse();

        DiagnosticsIn(_commandLine, "dotnet build -p:NoWarn=CS1591").ShouldBe(["NoWarn"]);
        DiagnosticsIn(_commandLine, "dotnet build /p:TreatWarningsAsErrors=false").ShouldBe(["TreatWarningsAsErrors"]);
        DiagnosticsIn(_commandLine, "csc -nowarn:CS0168").ShouldBe(["-nowarn"]);
        _commandLine.IsMatch("dotnet build -p:TreatWarningsAsErrors=true").ShouldBeFalse();
    }

    [Fact]
    public void TheEditorConfigForms_AreSeen_UnderTheirSection()
    {
        var silences = EditorConfigSilences(new EditorConfigSection(
            ".editorconfig",
            "Src/File.cs",
            _root,
            [
                "dotnet_diagnostic.S107.severity = none",
                "dotnet_diagnostic.CA1000.severity = silent",
                "dotnet_diagnostic.CA1001.severity = suggestion",
                "dotnet_analyzer_diagnostic.category-Style.severity = none",
                "dotnet_analyzer_diagnostic.severity = none",
                "generated_code = true",
                "dotnet_diagnostic.IDE0005.severity = warning",
                "csharp_style_var_for_built_in_types = false:suggestion",
            ]));

        silences.Select(silence => silence.Diagnostic).ShouldBe(
            ["S107", "CA1000", "CA1001", "category-Style", _anyDiagnostic, "generated_code"]);
        silences.ShouldAllBe(silence => silence.Where == ".editorconfig Src/File.cs");
    }

    [Theory]
    [InlineData("**/Generated/**.cs", "Src/Clients/X.Client/Generated/Models/A.cs", true)]
    [InlineData("**/Generated/**.cs", "Src/Clients/X.Client/A.cs", false)]
    [InlineData("**/Migrations/*.cs", "Src/X/Migrations/20260101_Initial.cs", true)]
    [InlineData("**/Migrations/*.cs", "Src/X/Migrations/Deeper/A.cs", false)]
    [InlineData("*.cs", "Src/X/A.cs", true)]
    [InlineData("Tests/**/*.cs", "Tests/X/A.cs", true)]
    [InlineData("Tests/**/*.cs", "Src/Tests.cs", false)]
    [InlineData("*.{cs,vb}", "A.vb", true)]
    public void AnEditorConfigGlob_MatchesAsEditorConfigReadsIt(string glob, string path, bool matches) =>
        GlobPattern(glob).IsMatch(path).ShouldBe(matches);

    private static string Describe(Silence silence) => $"{silence.Diagnostic} in {silence.Where}";

    private static string[] DiagnosticsIn(Regex form, string text) =>
        [.. form.Matches(text).SelectMany(DiagnosticsOf)];

    private static IEnumerable<string> DiagnosticsOf(Match match)
    {
        if (match.Groups["id"].Success)
        {
            return [match.Groups["id"].Value];
        }

        if (match.Groups["ids"].Success)
        {
            string[] ids =
            [
                .. _diagnosticList.Split(match.Groups["ids"].Value.Split("//", 2)[0])
                    .Where(id => id.Length > 0 && !id.StartsWith("$(", StringComparison.Ordinal)),
            ];

            return ids.Length > 0 ? ids : [_anyDiagnostic];
        }

        return match.Groups["name"].Success ? [match.Groups["name"].Value] : [_anyDiagnostic];
    }

    private static HashSet<Silence> FoundSilences()
    {
        var files = RepositoryFiles();
        var sections = EditorConfigSections();
        Regex[] generated =
        [
            .. sections
                .Where(section => section.Lines.Any(_generatedCode.IsMatch))
                .Select(section => GlobPattern(section.Glob, Relative(section.Directory))),
        ];

        files.Count.ShouldBeGreaterThanOrEqualTo(300, "Far fewer files were found than this project holds, so this rule is reading the wrong tree.");

        var silences = new HashSet<Silence>(sections.SelectMany(EditorConfigSilences));

        foreach (string file in files)
        {
            string relative = Relative(file);
            string name = Path.GetFileName(file);
            string extension = Path.GetExtension(file);

            if (extension == ".cs" && !generated.Any(pattern => pattern.IsMatch(relative)))
            {
                string text = File.ReadAllText(file);

                Add(silences, relative, text, _pragma, _suppressMessage, _skippedTest, _fileBasedProperty);
                silences.UnionWith(_nullableDisable.Matches(text).Select(_ => new Silence("#nullable", relative)));
            }

            if (_projectExtensions.Contains(extension))
            {
                Add(silences, relative, File.ReadAllText(file), _projectList, _projectSwitch);
            }

            if (_scriptExtensions.Contains(extension)
                || name.StartsWith("Dockerfile", StringComparison.Ordinal)
                || (extension == ".cs" && relative.StartsWith("Tools/", StringComparison.Ordinal)))
            {
                Add(silences, relative, File.ReadAllText(file), _commandLine);
            }
        }

        return silences;
    }

    private static void Add(HashSet<Silence> silences, string where, string text, params Regex[] forms) =>
        silences.UnionWith(forms
            .SelectMany(form => form.Matches(text))
            .SelectMany(DiagnosticsOf)
            .Select(diagnostic => new Silence(diagnostic, where)));

    private static IEnumerable<Silence> EditorConfigSilences(EditorConfigSection section) =>
        section.Lines.SelectMany(line =>
            _generatedCode.IsMatch(line)
                ? ["generated_code"]
                : _loweredSeverity.Match(line) is { Success: true } lowered
                    ? [lowered.Groups["id"].Success ? lowered.Groups["id"].Value : _anyDiagnostic]
                    : Array.Empty<string>())
            .Select(diagnostic => new Silence(diagnostic, $"{section.Holder} {section.Glob}"));

    private static List<EditorConfigSection> EditorConfigSections()
    {
        var sections = new List<EditorConfigSection>();

        foreach (string file in RepositoryFiles().Where(file => Path.GetFileName(file) == ".editorconfig" || Path.GetExtension(file) == ".globalconfig"))
        {
            string holder = Relative(file);
            string directory = Path.GetDirectoryName(file)!;
            var current = new EditorConfigSection(holder, string.Empty, directory, []);

            sections.Add(current);

            foreach (string line in File.ReadLines(file))
            {
                if (_section.Match(line) is { Success: true } header)
                {
                    current = new EditorConfigSection(holder, header.Groups["glob"].Value, directory, []);
                    sections.Add(current);
                }
                else
                {
                    current.Lines.Add(line);
                }
            }
        }

        return sections;
    }

    private static HashSet<Silence> ListedSilences() =>
        [
            .. TableRows().SelectMany(cells =>
            {
                string[] where = [.. _codeSpan.Matches(cells[1]).Select(span => span.Groups["span"].Value)];

                return _codeSpan.Matches(cells[0]).Select(span => new Silence(span.Groups["span"].Value, string.Join(' ', where)));
            }),
        ];

    private static List<string[]> TableRows()
    {
        string[] lines = File.ReadAllLines(Path.Combine(_root, _page));
        int header = Array.FindIndex(lines, line => line.Trim() == _tableHeader);

        header.ShouldBeGreaterThanOrEqualTo(0, $"{_page} holds no table headed '{_tableHeader}' to read.");

        return
        [
            .. lines
                .Skip(header + 2)
                .TakeWhile(line => line.StartsWith('|') || line.StartsWith("<!--#", StringComparison.Ordinal))
                .Where(line => line.StartsWith('|'))
                .Select(line => line.Trim().Trim('|').Split('|'))
                .Where(cells => cells.Length == 3),
        ];
    }

    private static Regex GlobPattern(string glob, string directory = ".")
    {
        var pattern = new System.Text.StringBuilder("^");

        if (directory != ".")
        {
            pattern.Append(Regex.Escape(directory)).Append('/');
        }

        pattern.Append(glob.Contains('/', StringComparison.Ordinal) ? string.Empty : "(?:.*/)?");
        glob = glob.TrimStart('/');

        for (int index = 0; index < glob.Length; index++)
        {
            char character = glob[index];

            if (character == '*' && index + 1 < glob.Length && glob[index + 1] == '*')
            {
                bool directories = index + 2 < glob.Length && glob[index + 2] == '/';
                pattern.Append(directories ? "(?:.*/)?" : ".*");
                index += directories ? 2 : 1;
            }
            else
            {
                pattern.Append(character switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    '{' => "(?:",
                    '}' => ")",
                    ',' => "|",
                    _ => Regex.Escape(new string(character, 1)),
                });
            }
        }

        return new Regex(pattern.Append('$').ToString(), RegexOptions.None, _timeout);
    }

    private static string Relative(string path) => Path.GetRelativePath(_root, path).Replace('\\', '/');

    private static List<string> RepositoryFiles()
    {
        var files = new List<string>();
        var pending = new Stack<string>([_root]);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();

            files.AddRange(Directory.EnumerateFiles(directory));

            foreach (string child in Directory.EnumerateDirectories(directory).Where(child => !_skippedDirectories.Contains(Path.GetFileName(child))))
            {
                pending.Push(child);
            }
        }

        return files;
    }
}
