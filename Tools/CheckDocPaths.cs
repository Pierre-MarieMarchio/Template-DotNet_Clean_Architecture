#:property UseAppHost=false

using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

internal static class CheckDocPaths
{
    private static readonly Regex CodeSpan = new(@"`([^`\n]+)`");

    private static readonly Regex Candidate = new(
        @"^(?:\./)?"
        + @"(?:Src|Tests|Tools|docs|\.github|\.config)/[\w\-./]+"
        + @"|^(?:[\w\-.]+\.(?:sln|slnx|props|json|yml|md|ps1|http|runsettings|minimum|csproj|py))$");

    private static readonly Regex BareFilename = new(
        @"^[\w\-.]+\."
        + @"(?:sln|slnx|props|json|ya?ml|md|ps1|sh|http|runsettings|minimum|csproj|py|cs)$");

    private static readonly Regex Trailing = new(@"(?:'s|[.,;:)\]]+)$");

    private const string DocDir = "docs";

    private static readonly Regex BareDocPath = new($@"(?<![\w`/]){DocDir}/[\w\-./]+");

    private static readonly string[] CommentingSuffixes =
        [".yaml", ".yml", ".http", ".ps1", ".py", ".props", ".cs", ".editorconfig"];

    private static readonly SearchValues<char> NotPathCharacters =
        SearchValues.Create("*$<>|(){}\"'");

    private static readonly HashSet<string> SkippedDirectoryNames =
        new(["bin", "obj", ".git", ".vs"], StringComparer.Ordinal);

    private sealed record Scan(
        IReadOnlyList<string> Problems,
        int MarkdownFiles,
        int Checked,
        int CommentingFiles,
        int BareChecked);

    private const string Help =
        """
        Usage: dotnet run Tools/CheckDocPaths.cs [<root>]
               dotnet run Tools/CheckDocPaths.cs --self-test
               dotnet run Tools/CheckDocPaths.cs -- --help

        Checks that every repository path cited inside backticks in the tracked Markdown resolves
        on disk, CHANGELOG.md and the plans under the documentation directory excepted; a bare
        filename only has to exist somewhere in the tree. A second pass checks every path under
        the documentation directory cited without backticks in the files that explain themselves
        in comments: workflows, scripts, .http files.

        Exit code 0 when every citation resolves; 1 on a citation that does not, and when no
        Markdown or no citation was examined, which proves nothing.
        """;

    private static int Main(string[] args)
    {
        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Help);
            return 0;
        }

        if (Array.IndexOf(args, "--self-test") >= 0)
        {
            return SelfTest();
        }

        var repo = Resolve(args.Length > 0 ? args[0] : ".");

        return Run(repo);
    }

    private static string Suffix(string name)
    {
        var index = name.LastIndexOf('.');

        return index > 0 && index < name.Length - 1 ? name[index..] : string.Empty;
    }

    private static bool UnderExemptDirectory(string fullPath) =>
        Segments(fullPath).Any(ExemptDirectoryNames.Contains);

    private static string[] Segments(string fullPath) =>
        fullPath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

    private static bool UnderSkippedDirectory(string fullPath)
    {
        foreach (var part in fullPath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (SkippedDirectoryNames.Contains(part))
            {
                return true;
            }
        }

        return false;
    }

    private static void Walk(DirectoryInfo directory, List<FileSystemInfo> collected)
    {
        FileSystemInfo[] entries;

        try
        {
            entries = directory.GetFileSystemInfos();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            collected.Add(entry);

            if (entry is DirectoryInfo subdirectory && entry.LinkTarget is null)
            {
                Walk(subdirectory, collected);
            }
        }
    }

    private static List<FileSystemInfo> Entries(string repo)
    {
        var collected = new List<FileSystemInfo>();

        Walk(new DirectoryInfo(repo), collected);
        collected.Sort((left, right) => string.CompareOrdinal(left.FullName, right.FullName));

        return collected;
    }

    private static HashSet<string> GitIgnored(string repo, List<string> paths)
    {
        var ignored = new HashSet<string>(StringComparer.Ordinal);

        if (paths.Count == 0)
        {
            return ignored;
        }

        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repo,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardInputEncoding = new UTF8Encoding(false),
            };

            startInfo.ArgumentList.Add("check-ignore");
            startInfo.ArgumentList.Add("--stdin");

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return ignored;
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            process.StandardInput.Write(string.Join("\n", paths.Select(Normalised)));
            process.StandardInput.Close();
            process.WaitForExit();

            standardError.GetAwaiter().GetResult();

            foreach (var line in standardOutput.GetAwaiter().GetResult()
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                ignored.Add(Normalised(line.TrimEnd('\r')));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return [];
        }

        return ignored;
    }

    private static List<string> Tracked(string repo, List<string> candidates)
    {
        var ignored = GitIgnored(repo, candidates);

        return candidates.Where(path => !ignored.Contains(Normalised(path))).ToList();
    }

    private static readonly HashSet<string> ExemptFileNames =
        new(StringComparer.OrdinalIgnoreCase) { "CHANGELOG.md" };

    private static readonly HashSet<string> ExemptDirectoryNames =
        new(StringComparer.Ordinal) { "plans" };

    private static List<string> CollectMarkdown(string repo, List<FileSystemInfo> entries)
    {
        var candidates = entries
            .Where(entry => entry is FileInfo
                            && entry.Exists
                            && Suffix(entry.Name) == ".md"
                            && !ExemptFileNames.Contains(entry.Name)
                            && !UnderExemptDirectory(entry.FullName)
                            && !UnderSkippedDirectory(entry.FullName))
            .Select(entry => entry.FullName)
            .ToList();

        return Tracked(repo, candidates);
    }

    private static List<string> CollectCommenting(string repo, List<FileSystemInfo> entries)
    {
        var candidates = entries
            .Where(entry => entry is FileInfo
                            && entry.Exists
                            && CommentingSuffixes.Contains(Suffix(entry.Name))
                            && !UnderSkippedDirectory(entry.FullName))
            .Select(entry => entry.FullName)
            .ToList();

        return Tracked(repo, candidates);
    }

    private static Scan ScanTree(string repo)
    {
        var missing = new List<string>();
        var checkedPaths = 0;

        var entries = Entries(repo);

        var namesInTree = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            if (!UnderSkippedDirectory(entry.FullName))
            {
                namesInTree.Add(entry.Name);
            }
        }

        var markdownFiles = CollectMarkdown(repo, entries);

        foreach (var markdown in markdownFiles)
        {
            var text = ReadText(markdown);
            var relativeMarkdown = Relative(repo, markdown);

            foreach (Match span in CodeSpan.Matches(text))
            {
                var raw = span.Groups[1].Value.Trim();

                if (raw.AsSpan().IndexOfAny(NotPathCharacters) >= 0 || raw.Contains(' '))
                {
                    continue;
                }

                if (raw.StartsWith("http://", StringComparison.Ordinal)
                    || raw.StartsWith("https://", StringComparison.Ordinal)
                    || raw.StartsWith('-')
                    || raw.StartsWith('/'))
                {
                    continue;
                }

                var cleaned = raw.StartsWith("./", StringComparison.Ordinal) ? raw[2..] : raw;

                if (!cleaned.Contains('/'))
                {
                    if (!BareFilename.IsMatch(cleaned))
                    {
                        continue;
                    }

                    checkedPaths++;

                    if (!namesInTree.Contains(cleaned))
                    {
                        missing.Add($"{relativeMarkdown}: `{raw}` (not found anywhere in the tree)");
                    }

                    continue;
                }

                if (!Candidate.IsMatch(cleaned))
                {
                    continue;
                }

                checkedPaths++;

                if (!Exists(Path.Combine(repo, cleaned)))
                {
                    missing.Add($"{relativeMarkdown}: `{raw}`");
                }
            }
        }

        var commentingFiles = CollectCommenting(repo, entries);
        var bareChecked = 0;

        foreach (var source in commentingFiles)
        {
            var text = ReadText(source);
            var relativeSource = Relative(repo, source);

            foreach (Match match in BareDocPath.Matches(text))
            {
                var cited = Trailing.Replace(match.Value, string.Empty);

                bareChecked++;

                if (!Exists(Path.Combine(repo, cited)))
                {
                    missing.Add($"{relativeSource}: {cited}");
                }
            }
        }

        var problems = missing.Distinct(StringComparer.Ordinal).ToList();
        problems.Sort(StringComparer.Ordinal);

        return new Scan(problems, markdownFiles.Count, checkedPaths, commentingFiles.Count, bareChecked);
    }

    private static int Run(string repo)
    {
        var result = ScanTree(repo);

        Console.WriteLine(
            $"Scanned {result.MarkdownFiles} Markdown file(s); "
            + $"checked {result.Checked} path reference(s).");
        Console.WriteLine(
            $"Scanned {result.CommentingFiles} commented file(s); "
            + $"checked {result.BareChecked} bare {DocDir}/ reference(s).");
        Console.WriteLine();

        if (result.MarkdownFiles == 0)
        {
            Console.WriteLine("PROBLEM: no Markdown file was examined, so this check proves nothing.");
            return 1;
        }

        if (result.Checked == 0)
        {
            Console.WriteLine("PROBLEM: no cited path was checked, so this check proves nothing.");
            return 1;
        }

        if (result.Problems.Count > 0)
        {
            Console.WriteLine($"PROBLEMS ({result.Problems.Count}):");

            foreach (var entry in result.Problems)
            {
                Console.WriteLine($"  ! {entry}");
            }

            return 1;
        }

        Console.WriteLine("Every cited path exists.");
        return 0;
    }

    private static readonly (string Label, (string Path, string Content)[] Files)[] MustFire =
    [
        (
            "a backticked path that resolves to nothing",
            [
                ("README.md", "The handler lives in `Src/Application/Missing.cs`.\n"),
                ("Src/Application/Present.cs", ""),
            ]
        ),
        (
            "a bare filename that exists nowhere in the tree",
            [
                ("README.md", "Run `Absent.ps1` first.\n"),
                ("Src/Application/Present.cs", ""),
            ]
        ),
        (
            "a bare documentation path cited from a file that comments rather than documents",
            [
                ("README.md", "See `Src/Application/Present.cs`.\n"),
                ("Src/Application/Present.cs", ""),
                ("deploy.yaml", $"# rationale: {DocDir}/nowhere.md\n"),
            ]
        ),
        (
            "an empty tree, where a path check establishes nothing",
            []
        ),
        (
            "a tree whose Markdown cites no checkable path at all",
            [("README.md", "Prose with no code spans, so nothing is asserted about the tree.\n")]
        ),
    ];

    private static readonly (string Label, (string Path, string Content)[] Files)[] MustStaySilent =
    [
        (
            "a backticked path that resolves",
            [
                ("README.md", "The handler lives in `Src/Application/Present.cs`.\n"),
                ("Src/Application/Present.cs", ""),
            ]
        ),
        (
            "a bare filename that exists somewhere in the tree",
            [
                ("README.md", "Run `Present.ps1` first.\n"),
                ("Src/Application/Present.ps1", ""),
            ]
        ),
        (
            "a bare documentation path that resolves",
            [
                ("README.md", "See `Src/Application/Present.cs`.\n"),
                ("Src/Application/Present.cs", ""),
                ($"{DocDir}/architecture/README.md", "`Src/Application/Present.cs`\n"),
                ("deploy.yaml", $"# rationale: {DocDir}/architecture/README.md\n"),
            ]
        ),
        (
            "prose that mentions a directory without backticks",
            [
                (
                    "README.md",
                    "Everything under Src/Application/Nowhere.cs is prose, not a claim.\n"
                    + "The real one is `Src/Application/Present.cs`.\n"
                ),
                ("Src/Application/Present.cs", ""),
            ]
        ),
        (
            "an identifier that merely looks like a filename",
            [
                (
                    "README.md",
                    "`TodoItem.MaxTags` and `net10.0` and `127.0.0.1` name no file.\n"
                    + "This one does: `Src/Application/Present.cs`.\n"
                ),
                ("Src/Application/Present.cs", ""),
            ]
        ),
        (
            "a changelog citing a path that has since been removed, which is a record and not a claim",
            [
                ("CHANGELOG.md", "- Added `Src/Application/Gone.cs`, later replaced.\n"),
                ("README.md", "See `Src/Application/Present.cs`.\n"),
                ("Src/Application/Present.cs", ""),
            ]
        ),
    ];

    private static readonly (string Path, string Content)[] IgnoredNote =
    [
        (".gitignore", "ignored/\n"),
        ("README.md", "The handler lives in `Src/Application/Present.cs`.\n"),
        ("Src/Application/Present.cs", ""),
        ("ignored/WORKING-NOTE.md", "A scratch note citing `Src/Application/Missing.cs`.\n"),
    ];

    private static readonly (string Path, string Content)[] PlannedTree =
    [
        ("README.md", "The handler lives in `Src/Application/Present.cs`.\n"),
        ("Src/Application/Present.cs", ""),
        ($"{DocDir}/plans/WAVE-ONE.md", "The handler will live in `Src/Application/Planned.cs`.\n"),
        ("NOTES.md", "The handler lives in `Src/Application/Absent.cs`.\n"),
    ];

    private static readonly string[] PlannedTreeProblems = ["NOTES.md: `Src/Application/Absent.cs`"];

    private static void Materialise(string root, (string Path, string Content)[] files)
    {
        foreach (var (relative, content) in files)
        {
            var path = Path.Combine(root, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }

    private static int SilentRun(string repo)
    {
        var original = Console.Out;

        try
        {
            Console.SetOut(TextWriter.Null);

            return Run(repo);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static bool InitialiseRepository(string root)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            startInfo.ArgumentList.Add("init");
            startInfo.ArgumentList.Add("-q");

            using var process = Process.Start(startInfo);

            process?.WaitForExit();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }

        return Directory.Exists(Path.Combine(root, ".git"));
    }

    private static int SelfTest()
    {
        var failures = new List<string>();

        foreach (var (label, files) in MustFire)
        {
            RunInTemporaryTree(files, root =>
            {
                if (SilentRun(root) == 0)
                {
                    failures.Add($"passed on a tree it should have rejected: {label}");
                }
            });
        }

        foreach (var (label, files) in MustStaySilent)
        {
            RunInTemporaryTree(files, root =>
            {
                var status = SilentRun(root);

                if (status != 0)
                {
                    failures.Add($"failed on a sound tree (exit {status}): {label}");
                }
            });
        }

        var ignoredNoteChecked = false;

        RunInTemporaryTree(IgnoredNote, root =>
        {
            if (!InitialiseRepository(root))
            {
                return;
            }

            ignoredNoteChecked = true;

            var result = ScanTree(root);

            if (result.MarkdownFiles != 1)
            {
                failures.Add(
                    "a note git ignores must not be scanned; expected 1 Markdown file, saw "
                    + $"{result.MarkdownFiles}");
            }

            if (result.Problems.Count > 0)
            {
                failures.Add(
                    "a note git ignores must raise nothing; got "
                    + Repr(result.Problems));
            }
        });

        RunInTemporaryTree(PlannedTree, root =>
        {
            var result = ScanTree(root);

            if (result.MarkdownFiles != 2)
            {
                failures.Add(
                    "a plan the gate exempts must not be scanned; expected 2 Markdown files, saw "
                    + $"{result.MarkdownFiles}");
            }

            if (!result.Problems.SequenceEqual(PlannedTreeProblems, StringComparer.Ordinal))
            {
                failures.Add(
                    "a plan the gate exempts must raise nothing while the note beside it still does; "
                    + $"expected {Repr(PlannedTreeProblems)}, got {Repr(result.Problems)}");
            }
        });

        var total = MustFire.Length + MustStaySilent.Length + (ignoredNoteChecked ? 1 : 0) + 1;

        Console.WriteLine(
            $"Self-test: {MustFire.Length} faulted tree(s), {MustStaySilent.Length} sound tree(s), "
            + $"{(ignoredNoteChecked ? "1 ignored-note tree" : "no ignored-note tree (git absent)")}, "
            + "1 exempt-plan tree.");
        Console.WriteLine();

        if (failures.Count > 0)
        {
            Console.WriteLine($"SELF-TEST FAILURES ({failures.Count} of {total}):");

            foreach (var failure in failures)
            {
                Console.WriteLine($"  ! {failure}");
            }

            return 1;
        }

        Console.WriteLine("The gate fires on every faulted tree and on none of the sound ones.");
        return 0;
    }

    private static void RunInTemporaryTree((string Path, string Content)[] files, Action<string> body)
    {
        var directory = Directory.CreateTempSubdirectory("check-doc-paths-");

        try
        {
            var root = Resolve(directory.FullName);

            Materialise(root, files);
            body(root);
        }
        finally
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string Resolve(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string Normalised(string path) => path.Replace('\\', '/');

    private static string Relative(string repo, string path) =>
        Path.GetRelativePath(repo, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string ReadText(string path) => File.ReadAllText(path, new UTF8Encoding(false));

    private static string Repr(IReadOnlyList<string> values) =>
        "[" + string.Join(", ", values.Select(value => $"'{value}'")) + "]";
}
