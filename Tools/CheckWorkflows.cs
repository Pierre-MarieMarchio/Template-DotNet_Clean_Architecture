#:property UseAppHost=false
#:property ManagePackageVersionsCentrally=false
#:package YamlDotNet@16.*

// Structural validation of the GitHub workflows, for a repository whose CI has never run.
//
// Checks the failure modes that a YAML parse alone does not catch:
//   - `needs:` naming a job that does not exist
//   - a step referencing an action without a SHA pin
//   - `permissions:` missing at both workflow and job level
//   - a `run:` block referencing an `env:` key that is defined nowhere
//   - a referenced local file (script, Dockerfile, settings) that is absent from disk
//
// Two properties the checks above depend on.
//
// **Both spellings are workflows.** GitHub Actions reads `.yml` and `.yaml` alike, so a gate that
// globs one of them declares a whole file sound without opening it.
//
// **The gate refuses to pass on nothing.** A directory with no workflow in it establishes no
// candidate set, and reporting success over it is the vacuity `CONTRIBUTING.md` bans for the
// architecture rules, where `RuleAssertions.RequireTypes` enforces the same thing. So the count is
// printed and a zero is a failure.
//
//     dotnet run Tools/CheckWorkflows.cs .
//     dotnet run Tools/CheckWorkflows.cs --self-test

using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

try
{
    Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
catch (IOException)
{
    // A console that refuses an encoding change still prints; the default is already UTF-8 here.
}

if (args.Contains("--self-test"))
{
    return WorkflowGate.SelfTest();
}

return WorkflowGate.Run(Path.GetFullPath(args.Length > 0 ? args[0] : "."));

/// <summary>The gate itself: discovery, the structural checks, and the report.</summary>
internal static class WorkflowGate
{
    /// <summary>GitHub Actions accepts either spelling, so both are workflows and both are checked.</summary>
    private static readonly string[] WorkflowSuffixes = [".yml", ".yaml"];

    private static readonly Regex ShaPin = new(@"^[\w\-./]+@[0-9a-f]{40}$", RegexOptions.Compiled);

    private static readonly Regex EnvRef = new(
        @"\$\{?\{?\s*env\.([A-Za-z_][A-Za-z0-9_]*)|\$([A-Z_][A-Z0-9_]*)|\$\{([A-Z_][A-Z0-9_]*)\}",
        RegexOptions.Compiled);

    /// <summary>
    /// Local paths the workflows hand to a tool; each must exist or the step dies at run time.
    /// "Dockerfile" must be preceded by a path separator: the bare word also appears inside
    /// `::error title=Dockerfile ...` messages, which are strings, not paths. `csproj` is offered
    /// to the alternation ahead of `cs`, so that a project file is matched whole.
    /// </summary>
    private static readonly Regex FileRef = new(
        @"(?:\./)?("
        + @"(?:\.github/scripts/|Src/|Tests/|Tools/)[\w\-./]+\.(?:py|csproj|cs|xml)"
        + @"|coverage\.(?:runsettings|minimum)"
        + @"|[\w\-./]+/Dockerfile"
        + @")",
        RegexOptions.Compiled);

    /// <summary>
    /// Go template actions inside `--format '{{...}}'` declare their own variables ($p, $_);
    /// they are not shell parameters and must not be checked against env blocks.
    /// </summary>
    private static readonly Regex GoTemplate = new(@"\{\{.*?\}\}", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Shell variables that are always present, plus anything GitHub injects.</summary>
    private static readonly HashSet<string> ShellBuiltins = new(StringComparer.Ordinal)
    {
        "GITHUB_STEP_SUMMARY", "GITHUB_OUTPUT", "GITHUB_ENV", "GITHUB_PATH",
        "GITHUB_REPOSITORY", "GITHUB_REF", "GITHUB_SHA", "GITHUB_WORKSPACE",
        "GITHUB_TOKEN", "GITHUB_ACTOR", "RUNNER_OS", "HOME", "PATH", "PWD",
        "IFS", "PY", "REGISTRY",
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static List<string> Discover(string repo)
    {
        var directory = Path.Combine(repo, ".github", "workflows");

        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory)
            .Where(path => WorkflowSuffixes.Contains(Path.GetExtension(path), StringComparer.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Returns the problems that fail the gate, and the notes that only inform.</summary>
    public static (List<string> Problems, List<string> Notes) Validate(string repo, string name, string text)
    {
        var problems = new List<string>();
        var notes = new List<string>();

        YamlNode? document;

        try
        {
            // `on:` parses as boolean True in YAML 1.1 -- that is expected, not a bug.
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            document = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;
        }
        catch (YamlException exception)
        {
            return ([$"{name}: does not parse — {exception.Message}"], notes);
        }

        if (document is not YamlMappingNode root)
        {
            return ([$"{name}: top level is not a mapping"], notes);
        }

        var jobsNode = Get(root, "jobs");
        if (IsFalsy(jobsNode) || jobsNode is not YamlMappingNode jobs)
        {
            return ([$"{name}: defines no jobs"], notes);
        }

        var jobNames = new HashSet<string>(Keys(jobs), StringComparer.Ordinal);

        // Workflow-level env plus per-job env, for the shell-variable check.
        var workflowEnv = new HashSet<string>(Keys(Get(root, "env")), StringComparer.Ordinal);

        var topPermissions = Get(root, "permissions");

        foreach (var entry in jobs.Children)
        {
            var jobName = ScalarText(entry.Key);

            if (entry.Value is not YamlMappingNode job)
            {
                problems.Add($"{name}: job '{jobName}' is not a mapping");
                continue;
            }

            // needs: must resolve
            foreach (var dependency in Needs(job))
            {
                if (!jobNames.Contains(dependency))
                {
                    problems.Add(
                        $"{name}: job '{jobName}' needs '{dependency}', which is not a job in this workflow");
                }
            }

            if (IsNull(topPermissions) && IsNull(Get(job, "permissions")))
            {
                problems.Add($"{name}: job '{jobName}' has no permissions at job or workflow level");
            }

            if (IsFalsy(Get(job, "runs-on")))
            {
                problems.Add($"{name}: job '{jobName}' has no runs-on");
            }

            if (IsNull(Get(job, "timeout-minutes")))
            {
                notes.Add($"{name}: job '{jobName}' sets no timeout-minutes");
            }

            var knownEnv = new HashSet<string>(workflowEnv, StringComparer.Ordinal);
            knownEnv.UnionWith(Keys(Get(job, "env")));
            knownEnv.UnionWith(ShellBuiltins);

            foreach (var step in Steps(job))
            {
                var uses = Get(step, "uses") as YamlScalarNode;
                if (!string.IsNullOrEmpty(uses?.Value) && !ShaPin.IsMatch(uses.Value.Trim()))
                {
                    problems.Add($"{name}: job '{jobName}' uses '{uses.Value}' without a 40-char SHA pin");
                }

                var stepEnv = new HashSet<string>(Keys(Get(step, "env")), StringComparer.Ordinal);
                var runNode = Get(step, "run");
                if (IsFalsy(runNode) || runNode is not YamlScalarNode runScalar)
                {
                    continue;
                }

                var run = runScalar.Value ?? "";
                var stepName = StepName(step);

                // GitHub expressions and Go template actions both use {{...}}; strip them so
                // their internal variables are not mistaken for shell parameters.
                var runShell = GoTemplate.Replace(run, "");

                foreach (Match match in EnvRef.Matches(runShell))
                {
                    var variable =
                        match.Groups[1].Success ? match.Groups[1].Value
                        : match.Groups[2].Success ? match.Groups[2].Value
                        : match.Groups[3].Success ? match.Groups[3].Value
                        : "";

                    if (variable.Length == 0)
                    {
                        continue;
                    }

                    if (knownEnv.Contains(variable) || stepEnv.Contains(variable))
                    {
                        continue;
                    }

                    // Loop variables and locals assigned in the same block.
                    var assignment =
                        $@"(?:^|\n)\s*(?:for\s+{variable}\b|{variable}=|read -r[^\n]*\b{variable}\b|mapfile[^\n]*\b{variable}\b)";
                    if (Regex.IsMatch(run, assignment))
                    {
                        continue;
                    }

                    if (Regex.IsMatch(run, $@"\b{variable}\b\s*\(\)"))
                    {
                        continue;
                    }

                    problems.Add(
                        $"{name}: job '{jobName}' step '{stepName}' "
                        + $"references ${variable}, defined in no env block");
                }

                foreach (Match match in FileRef.Matches(run))
                {
                    var reference = match.Groups[1].Value;
                    if (reference.Contains('*') || reference.Contains('$'))
                    {
                        continue;
                    }

                    var candidate = Path.Combine(repo, reference);
                    if (!File.Exists(candidate) && !Directory.Exists(candidate))
                    {
                        problems.Add(
                            $"{name}: job '{jobName}' step '{stepName}' "
                            + $"references '{reference}', which does not exist on disk");
                    }
                }
            }
        }

        return (problems, notes);
    }

    public static int Run(string repo)
    {
        var workflows = Discover(repo);

        var listed = workflows.Count > 0
            ? string.Join(", ", workflows.Select(Path.GetFileName))
            : "(none)";
        Console.WriteLine($"Validated {workflows.Count} workflow(s): {listed}");
        Console.WriteLine();

        // A glob that matched nothing reports success, which is the failure this gate prevents.
        if (workflows.Count == 0)
        {
            Console.WriteLine("PROBLEM: no workflow file was examined, so this check proves nothing.");
            return 1;
        }

        var problems = new List<string>();
        var notes = new List<string>();

        foreach (var path in workflows)
        {
            var (found, noted) = Validate(repo, Path.GetFileName(path), ReadText(path));
            problems.AddRange(found);
            notes.AddRange(noted);
        }

        if (notes.Count > 0)
        {
            Console.WriteLine("Notes:");
            foreach (var note in Unique(notes))
            {
                Console.WriteLine($"  - {note}");
            }

            Console.WriteLine();
        }

        if (problems.Count > 0)
        {
            var distinct = Unique(problems);
            Console.WriteLine($"PROBLEMS ({distinct.Count}):");
            foreach (var problem in distinct)
            {
                Console.WriteLine($"  ! {problem}");
            }

            return 1;
        }

        Console.WriteLine("No structural problems found.");
        return 0;
    }

    // --- Self-test -------------------------------------------------------------------------------
    //
    // The two halves a gate needs in order to be worth its place in CI: proof that each check fires on
    // a workflow carrying the fault it names, and proof that none of them fires on a workflow that is
    // merely shaped like one. The clean fixture carries, on purpose, every construction that a naive
    // reading mistakes for a fault: a loop variable, a Go template's own `$P`, and a local script that
    // does exist.

    private const string CleanFixture = """
        name: Clean
        on:
          push:
        permissions:
          contents: read
        env:
          SOLUTION: App.slnx
        jobs:
          build:
            runs-on: ubuntu-latest
            timeout-minutes: 10
            steps:
              - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1
              - name: Build
                run: |
                  echo "$SOLUTION"
                  for ITEM in a b; do echo "$ITEM"; done
                  docker images --format '{{ $P }}'
                  dotnet run Tools/Present.cs

        """;

    private static readonly (string Label, string Text)[] MustFire =
    [
        (
            "needs: naming a job that does not exist",
            CleanFixture.Replace("    runs-on: ubuntu-latest", "    needs: absent\n    runs-on: ubuntu-latest")
        ),
        (
            "an action without a SHA pin",
            CleanFixture.Replace("actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", "actions/checkout@v5")
        ),
        (
            "no permissions at either level",
            CleanFixture.Replace("permissions:\n  contents: read\n", "")
        ),
        (
            "a $VAR defined in no env block",
            CleanFixture.Replace("echo \"$SOLUTION\"", "echo \"$UNDECLARED_TARGET\"")
        ),
        (
            "a local script absent from disk",
            CleanFixture.Replace("Tools/Present.cs", "Tools/Absent.cs")
        ),
        (
            "no runs-on",
            CleanFixture.Replace("    runs-on: ubuntu-latest\n", "")
        ),
        (
            "no jobs",
            "name: Empty\non:\n  push:\npermissions:\n  contents: read\njobs: {}\n"
        ),
        (
            "a job that is not a mapping",
            "name: Odd\non:\n  push:\npermissions:\n  contents: read\njobs:\n  build: 3\n"
        ),
        (
            "a top level that is not a mapping",
            "- one\n- two\n"
        ),
        (
            "YAML that does not parse",
            "name: Broken\njobs: [\n"
        ),
    ];

    private static readonly (string Label, string Text)[] MustStaySilent =
    [
        ("a workflow with nothing wrong with it", CleanFixture),
        (
            "permissions declared on the job rather than the workflow",
            CleanFixture
                .Replace("permissions:\n  contents: read\n", "")
                .Replace(
                    "    runs-on: ubuntu-latest",
                    "    permissions:\n      contents: read\n    runs-on: ubuntu-latest")
        ),
    ];

    /// <summary>
    /// The spelling that a `*.yml` glob declares sound without ever opening it. The fault inside is a
    /// real one, so a green result here means the file was never read.
    /// </summary>
    private static readonly (string Name, string Text) UnreadSpelling =
        ("release.yaml", CleanFixture.Replace("    runs-on:", "    needs: absent\n    runs-on:"));

    /// <summary>The minimum tree the file-existence check needs in order to have an answer.</summary>
    private static void FixtureRepo(string root)
    {
        var tools = Path.Combine(root, "Tools");
        Directory.CreateDirectory(tools);
        File.WriteAllText(Path.Combine(tools, "Present.cs"), "", Utf8NoBom);
        Directory.CreateDirectory(Path.Combine(root, ".github", "workflows"));
    }

    private static int SilentRun(string repo)
    {
        var original = Console.Out;
        try
        {
            Console.SetOut(new StringWriter());
            return Run(repo);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    public static int SelfTest()
    {
        var failures = new List<string>();

        var fixtures = Directory.CreateTempSubdirectory().FullName;
        try
        {
            FixtureRepo(fixtures);

            foreach (var (label, text) in MustFire)
            {
                var (problems, _) = Validate(fixtures, "fixture.yml", text);
                if (problems.Count == 0)
                {
                    failures.Add($"should have fired but did not: {label}");
                }
            }

            foreach (var (label, text) in MustStaySilent)
            {
                var (problems, _) = Validate(fixtures, "fixture.yml", text);
                if (problems.Count > 0)
                {
                    failures.Add($"fired on a sound workflow ({problems[0]}): {label}");
                }
            }
        }
        finally
        {
            Directory.Delete(fixtures, recursive: true);
        }

        // Discovery is its own claim: a fault the validator catches is still missed when the file is
        // never handed to it, and an empty directory is a green that rests on nothing.
        var discovery = Directory.CreateTempSubdirectory().FullName;
        try
        {
            FixtureRepo(discovery);

            if (SilentRun(discovery) == 0)
            {
                failures.Add("passed over a workflow directory with no workflow in it");
            }

            var (name, text) = UnreadSpelling;
            var target = Path.Combine(discovery, ".github", "workflows", name);
            File.WriteAllText(target, text, Utf8NoBom);

            if (SilentRun(discovery) == 0)
            {
                failures.Add($"never read '{name}', so its dangling needs: went unreported");
            }

            File.WriteAllText(target, CleanFixture, Utf8NoBom);

            if (SilentRun(discovery) != 0)
            {
                failures.Add($"rejected '{name}', which carries no fault");
            }
        }
        finally
        {
            Directory.Delete(discovery, recursive: true);
        }

        var total = MustFire.Length + MustStaySilent.Length + 3;
        Console.WriteLine(
            $"Self-test: {MustFire.Length} faulted workflow(s), {MustStaySilent.Length} sound one(s), "
            + "3 discovery claim(s).");
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

        Console.WriteLine(
            "The gate fires on every faulted workflow, on none of the sound ones, and reads both spellings.");
        return 0;
    }

    // --- YAML helpers ----------------------------------------------------------------------------

    private static YamlNode? Get(YamlNode? node, string key)
    {
        if (node is not YamlMappingNode mapping)
        {
            return null;
        }

        foreach (var entry in mapping.Children)
        {
            if (entry.Key is YamlScalarNode scalar && string.Equals(scalar.Value, key, StringComparison.Ordinal))
            {
                return entry.Value;
            }
        }

        return null;
    }

    /// <summary>An absent key and an empty value are the same absence to every check here.</summary>
    private static bool IsNull(YamlNode? node)
        => node is null
            || (node is YamlScalarNode scalar
                && scalar.Style == ScalarStyle.Plain
                && scalar.Value is null or "" or "~" or "null" or "Null" or "NULL");

    private static bool IsFalsy(YamlNode? node) => node switch
    {
        null => true,
        YamlScalarNode scalar => IsNull(node)
            || (scalar.Style == ScalarStyle.Plain
                && scalar.Value is "false" or "False" or "FALSE" or "0")
            || scalar.Value?.Length == 0,
        YamlMappingNode mapping => mapping.Children.Count == 0,
        YamlSequenceNode sequence => sequence.Children.Count == 0,
        _ => false,
    };

    private static IEnumerable<string> Keys(YamlNode? node)
    {
        if (node is not YamlMappingNode mapping)
        {
            yield break;
        }

        foreach (var entry in mapping.Children)
        {
            if (entry.Key is YamlScalarNode scalar && scalar.Value is not null)
            {
                yield return scalar.Value;
            }
        }
    }

    private static IEnumerable<string> Needs(YamlMappingNode job)
    {
        var node = Get(job, "needs");
        if (IsFalsy(node))
        {
            yield break;
        }

        switch (node)
        {
            case YamlScalarNode scalar when scalar.Value is not null:
                yield return scalar.Value;
                break;
            case YamlSequenceNode sequence:
                foreach (var item in sequence.Children)
                {
                    if (item is YamlScalarNode scalar && scalar.Value is not null)
                    {
                        yield return scalar.Value;
                    }
                }

                break;
        }
    }

    private static IEnumerable<YamlMappingNode> Steps(YamlMappingNode job)
    {
        if (Get(job, "steps") is not YamlSequenceNode steps)
        {
            yield break;
        }

        foreach (var step in steps.Children)
        {
            if (step is YamlMappingNode mapping)
            {
                yield return mapping;
            }
        }
    }

    private static string ScalarText(YamlNode node)
        => node is YamlScalarNode scalar ? scalar.Value ?? "" : node.ToString();

    private static string StepName(YamlMappingNode step)
    {
        var node = Get(step, "name");
        return node switch
        {
            null => "?",
            YamlScalarNode when IsNull(node) => "None",
            _ => ScalarText(node),
        };
    }

    // --- Plumbing --------------------------------------------------------------------------------

    /// <summary>Line endings are normalised so that the shell patterns see the text a shell would.</summary>
    private static string ReadText(string path)
        => File.ReadAllText(path).Replace("\r\n", "\n").Replace("\r", "\n");

    private static List<string> Unique(List<string> values)
        => values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList();
}
