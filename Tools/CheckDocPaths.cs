#:property UseAppHost=false

using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Checks every repository path cited in the Markdown docs against the filesystem.
/// </summary>
/// <remarks>
/// <para>
/// A wrong path in a template's documentation costs the reader an hour, so it is worth a check
/// rather than a proofread. Only paths inside backticks are considered — prose mentioning a folder
/// by name is not a claim about the tree.
/// </para>
/// <para>
/// A second pass covers the files that explain themselves in comments rather than in Markdown —
/// the deployment manifests, the <c>.http</c> scratchpad, the build scripts. They cite a document
/// by bare path with no backticks around it, so the first pass cannot see them, and deleting a
/// documentation directory leaves seven such references pointing at nothing while a backtick-only
/// check stays green. Only the documentation directory's own prefix is considered there: unlike a
/// bare filename, it is unambiguous enough to check without inventing false positives out of code
/// identifiers.
/// </para>
/// <para>
/// Two further properties, without which a green result is worth nothing.
/// </para>
/// <para>
/// <b>The gate refuses to pass on nothing.</b> A scan that reads no Markdown, or that finds no path
/// worth checking, has established no candidate set and therefore proves nothing. That is the
/// vacuity <c>CONTRIBUTING.md</c> bans for the architecture rules, where
/// <c>RuleAssertions.RequireTypes</c> enforces it; here the counts are printed and a zero of either
/// one is a failure.
/// </para>
/// <para>
/// <b>The gate reads what git tracks.</b> A working note dropped into an ignored directory is not
/// documentation, and the paths it cites are not claims about the tree. The ignore list is asked of
/// git rather than parsed here, because a gate that reimplements those rules disagrees with them
/// eventually.
/// </para>
/// <example>
/// <code>
/// dotnet run Tools/CheckDocPaths.cs .
/// dotnet run Tools/CheckDocPaths.cs --self-test
/// </code>
/// </example>
/// </remarks>
internal static class CheckDocPaths
{
    // Inside backticks, anything that looks like a repo-relative path with a real extension or a
    // known top-level directory.
    private static readonly Regex CodeSpan = new(@"`([^`\n]+)`");

    private static readonly Regex Candidate = new(
        @"^(?:\./)?"
        + @"(?:Src|Tests|Tools|docs|\.github|\.config)/[\w\-./]+"
        + @"|^(?:[\w\-.]+\.(?:sln|slnx|props|json|yml|md|ps1|http|runsettings|minimum|csproj|py))$");

    // A bare filename in prose is not a claim that it sits at the repository root. It only has to
    // exist somewhere in the tree.
    //
    // The extension must be a real file type from a closed list. Without that, code identifiers
    // (`TodoItem.MaxTags`), error codes (`auth.required`), versions (`net10.0`) and addresses
    // (`127.0.0.1`) all look like filenames and the check drowns in noise it invented.
    private static readonly Regex BareFilename = new(
        @"^[\w\-.]+\."
        + @"(?:sln|slnx|props|json|ya?ml|md|ps1|sh|http|runsettings|minimum|csproj|py|cs)$");

    // Trailing punctuation belongs to the sentence, not to the path. A possessive is the one that
    // does not look like punctuation: a reference written as "<path>'s own recommendation" names a
    // document, not a directory whose last segment ends in an apostrophe-s.
    private static readonly Regex Trailing = new(@"(?:'s|[.,;:)\]]+)$");

    // The documentation directory's name, held in a constant rather than written into the pattern
    // below, so that this script's own fixtures can name a path under it without the second pass
    // reading those fixtures as citations and reporting itself.
    private const string DocDir = "docs";

    private static readonly Regex BareDocPath = new($@"(?<![\w`/]){DocDir}/[\w\-./]+");

    private static readonly string[] CommentingSuffixes =
        [".yaml", ".yml", ".http", ".ps1", ".py", ".props", ".cs", ".editorconfig"];

    // The characters that mark a code span as a command line, a glob or an expression rather
    // than a path.
    private static readonly SearchValues<char> NotPathCharacters =
        SearchValues.Create("*$<>|(){}\"'");

    private static readonly HashSet<string> SkippedDirectoryNames =
        new(["bin", "obj", ".git", ".vs"], StringComparer.Ordinal);

    /// <summary>What the pass examined, alongside what it found.</summary>
    /// <remarks>
    /// Both halves are needed: a problem list with no count behind it cannot be told apart from a
    /// pass that read nothing.
    /// </remarks>
    private sealed record Scan(
        IReadOnlyList<string> Problems,
        int MarkdownFiles,
        int Checked,
        int CommentingFiles,
        int BareChecked);

    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-test") >= 0)
        {
            return SelfTest();
        }

        var repo = Resolve(args.Length > 0 ? args[0] : ".");

        return Run(repo);
    }

    // --- The tree ------------------------------------------------------------------------------

    /// <summary>The name Python's <c>PurePath.suffix</c> would report.</summary>
    /// <remarks>
    /// A leading dot is part of the name, not an extension, so a file called <c>.editorconfig</c>
    /// carries no suffix at all and the suffix list above never selects one.
    /// </remarks>
    private static string Suffix(string name)
    {
        var index = name.LastIndexOf('.');

        return index > 0 && index < name.Length - 1 ? name[index..] : string.Empty;
    }

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

    /// <summary>Every entry beneath <paramref name="directory"/>, files and directories alike.</summary>
    /// <remarks>A symlinked directory is reported but not descended into, as a recursive glob does.</remarks>
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

    /// <summary>Paths git is told to ignore.</summary>
    /// <remarks>
    /// Asked of git rather than parsed out of <c>.gitignore</c>, because a gate that reimplements
    /// the ignore rules disagrees with them eventually. A tree that is not a repository, or a
    /// machine with no git, yields nothing to skip rather than an error: the check still runs, over
    /// slightly more.
    /// </remarks>
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

            // Both streams are drained while stdin is still being written: a pipe that fills up
            // while nobody reads it stops the process and this one waits on it forever.
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();

            process.StandardInput.Write(string.Join("\n", paths));
            process.StandardInput.Close();
            process.WaitForExit();

            standardError.GetAwaiter().GetResult();

            foreach (var line in standardOutput.GetAwaiter().GetResult()
                         .Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                ignored.Add(line.TrimEnd('\r'));
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

        return candidates.Where(path => !ignored.Contains(path)).ToList();
    }

    /// <summary>
    /// The Markdown this check has an opinion about. A changelog is excluded, for the same reason
    /// the narrative-comment gate excludes it: its entries describe the tree as it stood when each
    /// one was written, so a path it cites is a record rather than a claim about today. Holding it
    /// to the present would force a choice between a red gate and a rewritten history, and the
    /// second is worse.
    /// </summary>
    private static readonly HashSet<string> ExemptFileNames =
        new(StringComparer.OrdinalIgnoreCase) { "CHANGELOG.md" };

    private static List<string> CollectMarkdown(string repo, List<FileSystemInfo> entries)
    {
        var candidates = entries
            .Where(entry => entry is FileInfo
                            && entry.Exists
                            && Suffix(entry.Name) == ".md"
                            && !ExemptFileNames.Contains(entry.Name)
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

    // --- The scan ------------------------------------------------------------------------------

    private static Scan ScanTree(string repo)
    {
        var missing = new List<string>();
        var checkedPaths = 0;

        var entries = Entries(repo);

        // Names of everything in the tree, so that a bare filename can be looked for once rather
        // than by walking the tree again for each citation.
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

                // Skip command lines, globs, code, and anything with shell/expression syntax.
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

                // A path may legitimately name a directory or a file.
                if (!Exists(Path.Combine(repo, cleaned)))
                {
                    missing.Add($"{relativeMarkdown}: `{raw}`");
                }
            }
        }

        // --- Second pass: bare references in the files that comment rather than document. --------

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

        // A pass that read nothing reports success, which is the failure this gate exists to prevent.
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

    // --- Self-test -----------------------------------------------------------------------------
    //
    // The two halves a gate needs in order to be worth its place in CI: proof that it fires on a
    // citation that resolves to nothing, and proof that it stays green on a tree where every
    // citation holds. Each fixture is a whole miniature repository, because this gate's subject is a
    // tree and not a line of text — a citation is only wrong relative to the files that do or do not
    // sit beside it.

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
                ($"{DocDir}/ARCHITECTURE.md", "`Src/Application/Present.cs`\n"),
                ("deploy.yaml", $"# rationale: {DocDir}/ARCHITECTURE.md\n"),
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

    // The fixture that only a real repository can express: a note git is told to ignore cites a path
    // that does not exist, and the gate must neither read it nor count it.
    private static readonly (string Path, string Content)[] IgnoredNote =
    [
        (".gitignore", "ignored/\n"),
        ("README.md", "The handler lives in `Src/Application/Present.cs`.\n"),
        ("Src/Application/Present.cs", ""),
        ("ignored/WORKING-NOTE.md", "A scratch note citing `Src/Application/Missing.cs`.\n"),
    ];

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

        var total = MustFire.Length + MustStaySilent.Length + (ignoredNoteChecked ? 1 : 0);

        Console.WriteLine(
            $"Self-test: {MustFire.Length} faulted tree(s), {MustStaySilent.Length} sound tree(s), "
            + $"{(ignoredNoteChecked ? "1 ignored-note tree" : "no ignored-note tree (git absent)")}.");
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

    // --- Odds and ends -------------------------------------------------------------------------

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
                // A tree that refuses to go away is the operating system's business, not the gate's.
            }
        }
    }

    private static string Resolve(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string Relative(string repo, string path) =>
        Path.GetRelativePath(repo, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string ReadText(string path) => File.ReadAllText(path, new UTF8Encoding(false));

    /// <summary>The list of strings as a Python interpreter would show it.</summary>
    private static string Repr(IReadOnlyList<string> values) =>
        "[" + string.Join(", ", values.Select(value => $"'{value}'")) + "]";
}
