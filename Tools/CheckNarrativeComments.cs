#:property UseAppHost=false

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Checks that no comment narrates this repository's own history.
/// </summary>
/// <remarks>
/// <para>
/// <c>CONTRIBUTING.md</c> bans comments that say what the code <i>was</i> rather than what it
/// <i>is</i>: a reader who did not live that past cannot use the information, and the sentence rots
/// on its own the next time anything moves. Git holds the history. A rule that is written down,
/// understood and quoted is still broken in scores of places for as long as nothing executes it.
/// </para>
/// <para>
/// Two design decisions are worth knowing before editing the pattern list.
/// </para>
/// <para>
/// <b>The list is deliberately narrow.</b> The same words are legitimate or banned depending on the
/// tense they carry, and no regular expression can tell those apart: "a v2 added later would show
/// up inside the v1" is a hypothetical future and good design rationale, while "a test project
/// added later was simply not in the list" is this repository's past. So <c>added later</c>,
/// <c>no longer</c>, <c>previously</c>, <c>originally</c>, <c>in the past</c> and <c>formerly</c>
/// are all absent from the list on purpose. A gate that flags a legitimate sentence gets switched
/// off within the week; one that only ever fires on a real violation survives. Every pattern below
/// is a construction that can only be about a repository.
/// </para>
/// <para>
/// <b>The gate refuses to pass on nothing.</b> A filter that silently reads no files reports
/// success, and that is the exact failure this repository calls vacuity -- it is why every
/// architecture rule establishes its candidate set first. So this script prints what it examined
/// and fails when that count is zero, rather than congratulating an empty tree.
/// </para>
/// <para>
/// An unavoidable sentence -- the paragraph in <c>CONTRIBUTING.md</c> that states this rule has to
/// quote the phrases it bans -- carries a marker with a reason. A marker exempts the rest of its
/// paragraph, and the markers are counted in the output so they cannot spread unnoticed.
/// </para>
/// <example>
/// <code>
/// dotnet run Tools/CheckNarrativeComments.cs .
/// dotnet run Tools/CheckNarrativeComments.cs --self-test
/// </code>
/// </example>
/// </remarks>
internal static class CheckNarrativeComments
{
    // Constructions that can only be about this repository's own past. Each one has been checked
    // against every legitimate sentence in the tree, and the ambiguous words are left out above.
    private static readonly string[] NarrativePatterns =
    [
        @"\bthis used to\b",
        @"\bthere used to be\b",
        @"\bit used to be\b",
        @"\bused to be (?:written|forced|named|called|reachable)\b",
        @"\bused to (?:assert|read|name|say|live|sit|hold|declare|stand|match|carry|notice|point|exist|cover)\b",
        @"\bthe (?:old|previous|earlier|former) "
        + @"(?:implementation|version|code|guard|seeder|wording|shape|list|test|behaviour|behavior)\b",
        @"\bthe previous \w+ (?:was|were|had|held|kept|ran|lived|sat|hard-coded|overclaimed|named|threw)\b",
        @"\bfixed the bug\b",
        @"\bwhich is (?:precisely )?what happened to\b",
        @"\bwhat happened to the\b",
        @"\bhad already fallen behind\b",
        @"\bnothing (?:until now|had ever)\b",
        @"\buntil now would have\b",
        @"\bbefore this, ",
        @"\bbefore that, ",
        @"\bit now lives\b",
        @"\bnow lives (?:with|at|in) the\b",
        @"\bthe swap was measured\b",
        @"\bthis replaces\b",
        @"\bthe version this replaces\b",
        @"\bwas silently losing\b",
        @"\bthe previous wording\b",
        @"\boverclaimed\b",
        @"\bdrifted for a month\b",
        @"\bfor a month while the rules\b",
        @"\bthe one test here that\b",
        @"\bthe test that used to\b",
        @"\bthe old test\b",

        // Found by reading rather than by pattern, once the narrow list above is applied. Each of
        // these names a repository's own past in a construction the list above does not carry.
        @"\bbefore this \w+ (?:existed|there was)\b",
        @"\brescued from\b",
        @"\bhad no test at (?:all|this level)\b",
        @"\bby the time this (?:rule|test|gate|guard) was written\b",
        @"\bfor a while nothing\b",
        @"\bnothing noticed\b",
        @"\bhas ever exercised\b",
        @"\bhad ever been written\b",
        @"\bthe first version of this\b",
        @"\bspent a while\b",
        @"\bstopped seeing it\b",
    ];

    private static readonly Regex[] Rules =
        [.. NarrativePatterns.Select(pattern =>
            new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))];

    // A marker exempts its own line and the rest of its paragraph, up to the next blank line -- the
    // sentence it excuses is usually a line or two below it, in prose or in an XML doc block. The
    // marker must give a reason, so that an exemption is an argument rather than a silencer.
    private static readonly Regex Marker = new(@"narrative-ok:\s*\S");

    // Narrating history is what a changelog is for.
    private static readonly HashSet<string> ExemptFilenames =
        new(["CHANGELOG.md"], StringComparer.Ordinal);

    private static readonly HashSet<string> SkippedDirectoryNames =
        new(["bin", "obj", ".git", ".vs", "node_modules", "TestResults"], StringComparer.Ordinal);

    private static readonly string[] CommentPrefixes = ["//", "///", "*", "/*"];

    private static readonly string[] ExaminedSuffixes = [".cs", ".md"];

    private sealed record Finding(int Number, string Pattern, string Line);

    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-test") >= 0)
        {
            return SelfTest();
        }

        var repo = Resolve(args.Length > 0 ? args[0] : ".");

        return Run(repo);
    }

    /// <summary>In C# only comments are prose; in Markdown the whole file is.</summary>
    private static bool IsExaminedLine(string line, string suffix)
    {
        if (suffix == ".md")
        {
            return true;
        }

        var trimmed = line.TrimStart();

        foreach (var prefix in CommentPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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

    private static List<string> CollectFiles(string repo)
    {
        var collected = new List<FileSystemInfo>();

        Walk(new DirectoryInfo(repo), collected);

        var candidates = collected
            .Where(entry => entry is FileInfo
                            && entry.Exists
                            && ExaminedSuffixes.Contains(Suffix(entry.Name))
                            && !ExemptFilenames.Contains(entry.Name)
                            && !UnderSkippedDirectory(entry.FullName))
            .Select(entry => entry.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        var ignored = GitIgnored(repo, candidates);

        return candidates.Where(path => !ignored.Contains(path)).ToList();
    }

    /// <summary>The offending lines, alongside the exemptions honoured.</summary>
    private static (List<Finding> Findings, int Exemptions) ScanText(string text, string suffix)
    {
        var findings = new List<Finding>();
        var exemptions = 0;
        var exempt = false;
        var number = 0;

        foreach (var line in SplitLines(text))
        {
            number++;

            if (line.Trim().Length == 0)
            {
                exempt = false;
            }
            else if (Marker.IsMatch(line))
            {
                exempt = true;
            }

            if (!IsExaminedLine(line, suffix))
            {
                continue;
            }

            foreach (var rule in Rules)
            {
                if (!rule.IsMatch(line))
                {
                    continue;
                }

                if (exempt)
                {
                    exemptions++;
                }
                else
                {
                    findings.Add(new Finding(number, rule.ToString(), line.Trim()));
                }

                break;
            }
        }

        return (findings, exemptions);
    }

    private static int Run(string repo)
    {
        var files = CollectFiles(repo);

        var problems = new List<string>();
        var exemptions = 0;

        foreach (var path in files)
        {
            var (found, exempted) = ScanText(ReadText(path), Suffix(Path.GetFileName(path)));

            exemptions += exempted;

            foreach (var finding in found)
            {
                var relative = Relative(repo, path);

                problems.Add(
                    $"{relative}:{finding.Number}\n      matched /{finding.Pattern}/\n"
                    + $"      {Truncate(finding.Line, 160)}");
            }
        }

        Console.WriteLine($"Scanned {files.Count} file(s); honoured {exemptions} narrative-ok marker(s).");
        Console.WriteLine();

        // A filter that read nothing reports success, which is the failure this gate exists to prevent.
        if (files.Count == 0)
        {
            Console.WriteLine("PROBLEM: no .cs or .md file was examined, so this check proves nothing.");
            return 1;
        }

        if (problems.Count > 0)
        {
            Console.WriteLine($"PROBLEMS ({problems.Count}):");
            Console.WriteLine("  A comment must say what the code is, not what it was. Git holds the history.");
            Console.WriteLine("  Rewrite the sentence in the present, or add a `narrative-ok: <reason>` marker.");
            Console.WriteLine();

            foreach (var entry in problems)
            {
                Console.WriteLine($"  ! {entry}");
            }

            return 1;
        }

        Console.WriteLine("No comment narrates the repository's own history.");
        return 0;
    }

    // --- Self-test -----------------------------------------------------------------------------
    //
    // The two halves a gate needs in order to be worth its place in CI: proof that it fires on a
    // real violation, and proof that it stays silent on the sentences that merely resemble one. Both
    // sets are taken verbatim from this repository -- the offenders from what the audit finds, the
    // innocents from the sentences a broader pattern list flags wrongly.

    private static readonly (string Suffix, string Line)[] MustFire =
    [
        (".cs", "/// The value, not the algorithm. This used to be the algorithm name."),
        (".cs", "/// <b>Every header, and no others.</b> There used to be a parameter here for headers."),
        (".cs", "/// This exists because the same list used to be written out by hand in two places."),
        (".cs", "// is measured and thrown away — which is what happened to the Files loop."),
        (".cs", "/// It had already fallen behind: the list named three modules and the disk held five."),
        (".cs", "/// nothing until now would have noticed a <c>PUT</c> becoming replayable."),
        (".cs", "// Before this, a task that purged 0 rows every hour looked healthy."),
        (".cs", "/// The previous guard was `if (!await roleManager.Roles.AnyAsync())`, which skipped."),
        (".cs", "/// <b>This used to assert <c>x-amz-sdk-checksum-algorithm</c></b>, and that was wrong."),
        (".cs", "/// the ingress is the ordinary shape, and it used to be forced to declare a port."),
        (".cs", "/// This is the opposite of the test that used to stand here."),
        (".cs", "/// feature could not complete against a real store. The old test characterised that."),
        (".md", "is therefore the model's composition root. There used to be a second: `X`."),
        (".md", "is precisely why the test could not see it. It now lives with the feature it counts."),
        (".cs", "/// The version this replaces ran on every start in every environment."),
    ];

    private static readonly (string Suffix, string Line)[] MustStaySilent =
    [
        // A hypothetical future is design rationale, not history.
        (".cs", "// captures every action regardless of its group, so a v2 added later would show up."),
        (".cs", "/// route added later, and an unknown provider becomes a 404 where a configured one is."),
        (".md", "provider — which is to say email confirmation's, and any purpose added later."),
        (".md", "derivative pipeline added later must bound output size before it reads an archive."),

        // Runtime state, not repository history.
        (".cs", "/// <returns><c>null</c> when the account no longer exists.</returns>"),
        (".cs", "/// caller named a version the aggregate no longer holds."),
        (".cs", "/// address already held by an account that never confirmed it, and may no longer exist."),

        // Domain vocabulary that happens to be tense-shaped.
        (".cs", "// Caught: whether the due date is in the past depends on the clock."),
        (".cs", "/// summary before this switch can name it."),
        (".cs", "/// and that refusal happens before this method reads the field."),

        // A proof technique, and a token's own semantics.
        (".cs", "/// The valid settings, optionally with keys replaced — used to prove that an invalid."),
        (".cs", "/// invalidates every token already issued under the old name."),
        (".cs", "/// an assembly that was never loaded, or a namespace that has since been renamed."),

        // Design alternatives the repository rejected are not its history.
        (".md", "Two concrete defects in the `BaseRepository<T>` this pattern would replace:"),

        // Prose that merely contains a banned word in another sense.
        (".cs", "/// Points at the local docker-compose database. Design-time only."),
        (".cs", "/// the pair cannot be used to ask whether an id exists."),

        // "used to" meaning "employed in order to", which is why the verb list above does not carry
        // make: both of these stand in this repository and neither is about its past.
        (".cs", "/// used to make this request — an administrator could otherwise lock themselves out"),
        (".cs", "/// so the access token it just used to make the call keeps working until it expires."),

        // An exemption, honoured.
        (".md", """own history ("this used to…"). <!-- narrative-ok: the rule must quote what it bans -->"""),
    ];

    // A marker excuses the rest of its paragraph, not just the line it sits on: in prose and in an
    // XML doc block alike, the sentence being excused is normally a line or two below the reason for
    // it.
    private const string MarkerSpansItsParagraph =
        """
        <!-- narrative-ok: stating this rule requires quoting the phrases it bans -->
        Specifically banned: comments that paraphrase the code, and comments that narrate the repository's
        own history ("this used to…", "the old implementation…"). Git holds that.

        This paragraph is past the blank line, so the marker no longer covers it: this used to be allowed.

        """;

    private static int SelfTest()
    {
        var failures = new List<string>();

        var (spanned, honoured) = ScanText(MarkerSpansItsParagraph, ".md");

        if (honoured != 1)
        {
            failures.Add($"a marker should excuse its whole paragraph; honoured {honoured}");
        }

        var lines = spanned.Select(finding => finding.Number).ToList();

        if (lines.Count != 1 || lines[0] != 5)
        {
            failures.Add(
                "a marker should stop at the blank line; expected the offender on line 5, got "
                + $"[{string.Join(", ", lines)}]");
        }

        foreach (var (suffix, line) in MustFire)
        {
            var (found, _) = ScanText(line, suffix);

            if (found.Count == 0)
            {
                failures.Add($"should have fired but did not:\n      {line}");
            }
        }

        foreach (var (suffix, line) in MustStaySilent)
        {
            var (found, _) = ScanText(line, suffix);

            if (found.Count > 0)
            {
                failures.Add($"fired on a legitimate sentence (/{found[0].Pattern}/):\n      {line}");
            }
        }

        var total = MustFire.Length + MustStaySilent.Length;

        Console.WriteLine($"Self-test: {MustFire.Length} offender(s), {MustStaySilent.Length} innocent(s).");
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

        Console.WriteLine("The gate fires on every offender and on none of the innocents.");
        return 0;
    }

    // --- The tree ------------------------------------------------------------------------------

    /// <summary>The name Python's <c>PurePath.suffix</c> would report.</summary>
    /// <remarks>A leading dot is part of the name, not an extension.</remarks>
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

    // --- Odds and ends -------------------------------------------------------------------------

    /// <summary>The lines Python's <c>str.splitlines</c> would produce.</summary>
    /// <remarks>
    /// It breaks on more than the three ASCII sequences a .NET reader knows, and a line number in a
    /// message has to name the line the author sees.
    /// </remarks>
    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];

            if (!IsLineBoundary(current))
            {
                continue;
            }

            lines.Add(text[start..index]);

            if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
            {
                index++;
            }

            start = index + 1;
        }

        if (start < text.Length)
        {
            lines.Add(text[start..]);
        }

        return lines;
    }

    private static bool IsLineBoundary(char character) => character is '\n'
        or '\r'
        or '\v'
        or '\f'
        or '\u001c'
        or '\u001d'
        or '\u001e'
        or '\u0085'
        or '\u2028'
        or '\u2029';

    /// <summary>The first <paramref name="limit"/> characters, counted as Python counts them.</summary>
    private static string Truncate(string value, int limit)
    {
        var count = 0;
        var index = 0;

        while (index < value.Length)
        {
            if (count == limit)
            {
                return value[..index];
            }

            index += char.IsHighSurrogate(value[index])
                     && index + 1 < value.Length
                     && char.IsLowSurrogate(value[index + 1])
                ? 2
                : 1;

            count++;
        }

        return value;
    }

    private static string Resolve(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string Relative(string repo, string path) =>
        Path.GetRelativePath(repo, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string ReadText(string path) => File.ReadAllText(path, new UTF8Encoding(false));
}
