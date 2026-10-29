#:property UseAppHost=false
#:property ManagePackageVersionsCentrally=false
#:package YamlDotNet@16.*

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
}

if (args is ["--help"] or ["-h"])
{
    Console.WriteLine(WorkflowGate.Help);
    return 0;
}

if (args.Contains("--self-test"))
{
    return WorkflowGate.SelfTest();
}

return WorkflowGate.Run(Path.GetFullPath(args.Length > 0 ? args[0] : "."));

internal static class WorkflowGate
{
    internal const string Help =
        """
        Usage: dotnet run Tools/CheckWorkflows.cs [<root>]
               dotnet run Tools/CheckWorkflows.cs --self-test
               dotnet run Tools/CheckWorkflows.cs -- --help

        Checks every workflow under <root>/.github/workflows, .yml and .yaml alike, for what a YAML
        parse does not catch:
          - a needs: naming a job that does not exist
          - an action referenced without a SHA pin
          - a container image without a digest: a job's container: or services:, and the images
            the Docker actions pull on a step's behalf (binfmt, the BuildKit daemon, the SBOM
            scanner), whose defaults float when the step leaves them unnamed
          - permissions: missing at both workflow and job level
          - a run: block reading an env: key that is defined nowhere
          - a local file a step names (script, Dockerfile, settings) that is absent from disk
          - a working-directory, or a path an action takes as input (context, file,
            global-json-file, node-version-file, package_json_file, cache_dependency_path),
            that is absent from disk; a path built from an expression is not read
          - a trigger filtered by paths: or paths-ignore:, which leaves a required check it
            skips pending
          - a job-level if: on a matrix job of a workflow pull requests trigger: skipped whole,
            it reports under its unexpanded name and none of its legs does; allowed only on a
            matrix read from an earlier job's output (fromJSON(needs....)) when another job of
            the workflow carries the same name, to report the legs that one leaves out

        Exit code 0 when every workflow is sound; 1 on a problem, and on a directory holding no
        workflow, which proves nothing.
        """;

    private static readonly string[] WorkflowSuffixes = [".yml", ".yaml"];

    private static readonly Regex ShaPin = new(@"^[\w\-./]+@[0-9a-f]{40}$", RegexOptions.Compiled);

    private static readonly Regex DigestPin = new(
        @"^[a-z0-9][a-z0-9._/:-]*:[A-Za-z0-9._-]+@sha256:[0-9a-f]{64}$",
        RegexOptions.Compiled);

    private static readonly Regex DriverImage = new(@"(?:^|[\s,])image=(?<ref>[^\s,]+)", RegexOptions.Compiled);

    private static readonly Regex EnvRef = new(
        @"\$\{?\{?\s*env\.([A-Za-z_][A-Za-z0-9_]*)|\$([A-Z_][A-Z0-9_]*)|\$\{([A-Z_][A-Z0-9_]*)\}",
        RegexOptions.Compiled);

    private static readonly Regex FileRef = new(
        @"(?:\./)?("
        + @"(?:\.github/scripts/|Src/|Tests/|Tools/)[\w\-./]+\.(?:py|csproj|cs|xml)"
        + @"|coverage\.(?:runsettings|minimum)"
        + @"|[\w\-./]+/Dockerfile"
        + @")",
        RegexOptions.Compiled);

    private static readonly string[] PathInputs =
    [
        "context",
        "file",
        "global-json-file",
        "node-version-file",
        "package_json_file",
        "cache_dependency_path",
        "cache-dependency-path",
    ];

    private static readonly string[] PathFilters = ["paths", "paths-ignore"];

    private static readonly string[] PullRequestTriggers = ["pull_request", "pull_request_target"];

    private static readonly Regex GoTemplate = new(@"\{\{.*?\}\}", RegexOptions.Singleline | RegexOptions.Compiled);

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

    public static (List<string> Problems, List<string> Notes) Validate(string repo, string name, string text)
    {
        var problems = new List<string>();
        var notes = new List<string>();

        YamlNode? document;

        try
        {
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

        var triggers = Get(root, "on");

        foreach (var trigger in Keys(triggers))
        {
            foreach (var filter in PathFilters)
            {
                if (!IsNull(Get(Get(triggers, trigger), filter)))
                {
                    problems.Add(
                        $"{name}: the {trigger} trigger filters by {filter}:, so a required check it skips stays pending; choose the work inside a job instead");
                }
            }
        }

        var pullRequestTriggered = TriggerNames(triggers).Any(PullRequestTriggers.Contains);

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

            if (pullRequestTriggered && !IsNull(Get(job, "if")) && !IsNull(Get(Get(job, "strategy"), "matrix"))
                && !ReportsTheLegsItSkips(jobs, entry.Key, job))
            {
                problems.Add(
                    $"{name}: job '{jobName}' is a matrix job with a job-level if:, so skipped whole it reports none of its legs; "
                    + "read its matrix from an earlier job's output and report the other legs from a job of the same name");
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

            foreach (var problem in MissingPath(repo, "a default working-directory", Get(Get(Get(job, "defaults"), "run"), "working-directory")))
            {
                problems.Add($"{name}: job '{jobName}' {problem}");
            }

            foreach (var (where, image) in JobImages(job))
            {
                if (!DigestPin.IsMatch(image))
                {
                    problems.Add($"{name}: job '{jobName}' runs {where} '{image}' without a digest pin");
                }
            }

            foreach (var step in Steps(job))
            {
                var uses = Get(step, "uses") as YamlScalarNode;
                if (!string.IsNullOrEmpty(uses?.Value) && !ShaPin.IsMatch(uses.Value.Trim()))
                {
                    problems.Add($"{name}: job '{jobName}' uses '{uses.Value}' without a 40-char SHA pin");
                }

                foreach (var problem in ActionImageProblems(uses?.Value, Get(step, "with")))
                {
                    problems.Add($"{name}: job '{jobName}' step '{StepName(step)}' {problem}");
                }

                var pathProblems = MissingPath(repo, "a working-directory", Get(step, "working-directory"))
                    .Concat(PathInputs.SelectMany(input => MissingPath(repo, $"the input '{input}'", Get(Get(step, "with"), input))));

                foreach (var problem in pathProblems)
                {
                    problems.Add($"{name}: job '{jobName}' step '{StepName(step)}' {problem}");
                }

                var stepEnv = new HashSet<string>(Keys(Get(step, "env")), StringComparer.Ordinal);
                var runNode = Get(step, "run");
                if (IsFalsy(runNode) || runNode is not YamlScalarNode runScalar)
                {
                    continue;
                }

                var run = runScalar.Value ?? "";
                var stepName = StepName(step);

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
              - name: Set up Buildx
                uses: docker/setup-buildx-action@37fe631027851001ddb9b187196cc803df7f5f0e
                with:
                  driver-opts: image=moby/buildkit:v0.33.1@sha256:cec9f139f45e93c5c69c60f8b07cfad9f43f4ef6b6a6cd917527fea5ff2e3dea
              - name: Build
                run: |
                  echo "$SOLUTION"
                  for ITEM in a b; do echo "$ITEM"; done
                  docker images --format '{{ $P }}'
                  dotnet run Tools/Present.cs

        """;

    private const string SelectedLegsFixture = """
        name: Selected
        on:
          pull_request:
        permissions:
          contents: read
        jobs:
          changes:
            runs-on: ubuntu-latest
            timeout-minutes: 5
            outputs:
              run: ${{ steps.select.outputs.run }}
              skip: ${{ steps.select.outputs.skip }}
            steps:
              - id: select
                run: echo "run=[]" >> "$GITHUB_OUTPUT"
          legs:
            name: Leg (${{ matrix.name }})
            needs: changes
            if: needs.changes.outputs.run != '[]'
            runs-on: ubuntu-latest
            timeout-minutes: 5
            strategy:
              matrix: ${{ fromJSON(needs.changes.outputs.run) }}
            steps:
              - run: echo leg
          skipped:
            name: Leg (${{ matrix.name }})
            needs: changes
            if: needs.changes.outputs.skip != '[]'
            runs-on: ubuntu-latest
            timeout-minutes: 5
            strategy:
              matrix: ${{ fromJSON(needs.changes.outputs.skip) }}
            steps:
              - run: echo skipped

        """;

    private const string InlineMatrixWithIf =
        "    if: github.actor != 'bot'\n    strategy:\n      matrix:\n        leg: [a, b]\n    runs-on: ubuntu-latest";

    private static readonly (string Label, string Text)[] MustFire =
    [
        (
            "a pull_request trigger filtered by paths",
            CleanFixture.Replace("  push:\n", "  push:\n  pull_request:\n    paths: [Src/**]\n")
        ),
        (
            "a push trigger filtered by paths-ignore",
            CleanFixture.Replace("  push:\n", "  push:\n    paths-ignore: [docs/**]\n")
        ),
        (
            "an inline matrix skipped whole on a pull request",
            CleanFixture
                .Replace("  push:\n", "  push:\n  pull_request:\n")
                .Replace("    runs-on: ubuntu-latest", InlineMatrixWithIf)
        ),
        (
            "an inline matrix skipped whole, the triggers listed",
            CleanFixture
                .Replace("  push:\n", "  [push, pull_request]\n")
                .Replace("on:\n  [", "on: [")
                .Replace("    runs-on: ubuntu-latest", InlineMatrixWithIf)
        ),
        (
            "a computed matrix skipped whole with no job reporting the legs it leaves out",
            SelectedLegsFixture.Replace("    name: Leg (${{ matrix.name }})\n    needs: changes\n    if: needs.changes.outputs.skip", "    name: Skipped (${{ matrix.name }})\n    needs: changes\n    if: needs.changes.outputs.skip")
        ),
        (
            "needs: naming a job that does not exist",
            CleanFixture.Replace("    runs-on: ubuntu-latest", "    needs: absent\n    runs-on: ubuntu-latest")
        ),
        (
            "an action without a SHA pin",
            CleanFixture.Replace("actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1", "actions/checkout@v5")
        ),
        (
            "a BuildKit image left to the action's floating default",
            CleanFixture.Replace(
                "        with:\n          driver-opts: image=moby/buildkit:v0.33.1@sha256:cec9f139f45e93c5c69c60f8b07cfad9f43f4ef6b6a6cd917527fea5ff2e3dea\n",
                "")
        ),
        (
            "a BuildKit image named by tag alone",
            CleanFixture.Replace(
                "moby/buildkit:v0.33.1@sha256:cec9f139f45e93c5c69c60f8b07cfad9f43f4ef6b6a6cd917527fea5ff2e3dea",
                "moby/buildkit:v0.33.1")
        ),
        (
            "a job container without a digest",
            CleanFixture.Replace(
                "    runs-on: ubuntu-latest",
                "    runs-on: ubuntu-latest\n    container: node:22")
        ),
        (
            "a service image without a digest",
            CleanFixture.Replace(
                "    runs-on: ubuntu-latest",
                "    runs-on: ubuntu-latest\n    services:\n      db:\n        image: postgres:18")
        ),
        (
            "an SBOM from the scanner's floating default",
            CleanFixture.Replace(
                "      - name: Build\n",
                "      - name: Push\n        uses: docker/build-push-action@53b7df96c91f9c12dcc8a07bcb9ccacbed38856a\n"
                + "        with:\n          sbom: true\n      - name: Build\n")
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
            "a Dockerfile input absent from disk",
            CleanFixture.Replace(
                "      - name: Build\n",
                "      - name: Image\n        uses: docker/build-push-action@53b7df96c91f9c12dcc8a07bcb9ccacbed38856a\n"
                + "        with:\n          context: Web\n          file: Web/apps/Absent/Dockerfile\n      - name: Build\n")
        ),
        (
            "a lockfile input absent from disk",
            CleanFixture.Replace(
                "      - name: Build\n",
                "      - name: Package manager\n        uses: pnpm/action-setup@ea17c68df8912ef543352723c149a84f56e3d413\n"
                + "        with:\n          cache_dependency_path: Web/absent-lock.yaml\n      - name: Build\n")
        ),
        (
            "a step working-directory absent from disk",
            CleanFixture.Replace("      - name: Build\n", "      - name: Build\n        working-directory: Absent\n")
        ),
        (
            "a job default working-directory absent from disk",
            CleanFixture.Replace(
                "    timeout-minutes: 10\n",
                "    timeout-minutes: 10\n    defaults:\n      run:\n        working-directory: Absent\n")
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
        ("a computed matrix skipped whole beside the job that reports the legs it leaves out", SelectedLegsFixture),
        ("an inline matrix with a job-level if: on push alone", CleanFixture.Replace("    runs-on: ubuntu-latest", InlineMatrixWithIf)),
        (
            "a job-level if: on a pull request job without a matrix",
            CleanFixture
                .Replace("  push:\n", "  push:\n  pull_request:\n")
                .Replace("    runs-on: ubuntu-latest", "    if: github.actor != 'bot'\n    runs-on: ubuntu-latest")
        ),
        (
            "a service image pinned by digest",
            CleanFixture.Replace(
                "    runs-on: ubuntu-latest",
                "    runs-on: ubuntu-latest\n    services:\n      db:\n        image: "
                + "postgres:18.6-alpine3.23@sha256:885cf05d376c7cf27afef02073e6bdac3841252537f16e244fd1c1e6a7c99fb1")
        ),
        (
            "path inputs that exist, and one built from the matrix",
            CleanFixture
                .Replace("    timeout-minutes: 10\n", "    timeout-minutes: 10\n    defaults:\n      run:\n        working-directory: Web\n")
                .Replace(
                    "      - name: Build\n",
                    "      - name: Image\n        uses: docker/build-push-action@53b7df96c91f9c12dcc8a07bcb9ccacbed38856a\n"
                    + "        with:\n          context: Web\n          file: Web/apps/${{ matrix.app }}/Dockerfile\n"
                    + "      - name: Package manager\n        uses: pnpm/action-setup@ea17c68df8912ef543352723c149a84f56e3d413\n"
                    + "        with:\n          cache: true\n          cache_dependency_path: Web/pnpm-lock.yaml\n      - name: Build\n")
        ),
        (
            "permissions declared on the job rather than the workflow",
            CleanFixture
                .Replace("permissions:\n  contents: read\n", "")
                .Replace(
                    "    runs-on: ubuntu-latest",
                    "    permissions:\n      contents: read\n    runs-on: ubuntu-latest")
        ),
    ];

    private static readonly (string Name, string Text) UnreadSpelling =
        ("release.yaml", CleanFixture.Replace("    runs-on:", "    needs: absent\n    runs-on:"));

    private static void FixtureRepo(string root)
    {
        var tools = Path.Combine(root, "Tools");
        Directory.CreateDirectory(tools);
        File.WriteAllText(Path.Combine(tools, "Present.cs"), "", Utf8NoBom);
        Directory.CreateDirectory(Path.Combine(root, "Web"));
        File.WriteAllText(Path.Combine(root, "Web", "pnpm-lock.yaml"), "", Utf8NoBom);
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

    private static IEnumerable<string> TriggerNames(YamlNode? triggers) => triggers switch
    {
        YamlScalarNode scalar when !IsNull(scalar) => [scalar.Value!],
        YamlSequenceNode sequence => sequence.Children.OfType<YamlScalarNode>().Select(item => item.Value ?? ""),
        _ => Keys(triggers),
    };

    private static bool ReportsTheLegsItSkips(YamlMappingNode jobs, YamlNode key, YamlMappingNode job)
    {
        var matrix = Get(Get(job, "strategy"), "matrix");
        var name = Get(job, "name");

        if (matrix is not YamlScalarNode computed
            || !(computed.Value ?? "").Contains("fromJSON(needs.", StringComparison.Ordinal)
            || IsNull(name))
        {
            return false;
        }

        return jobs.Children.Any(other =>
            !ReferenceEquals(other.Key, key)
            && Get(other.Value, "name") is { } otherName
            && string.Equals(ScalarText(otherName), ScalarText(name!), StringComparison.Ordinal));
    }

    private static IEnumerable<string> MissingPath(string repo, string what, YamlNode? node)
    {
        if (IsNull(node) || node is not YamlScalarNode scalar)
        {
            yield break;
        }

        foreach (var line in (scalar.Value ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Contains("${{", StringComparison.Ordinal) || line.Contains('*') || line is "true" or "false")
            {
                continue;
            }

            var candidate = Path.Combine(repo, line);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                yield return $"names {what} '{line}', which does not exist on disk";
            }
        }
    }

    private static IEnumerable<(string Where, string Image)> JobImages(YamlMappingNode job)
    {
        var container = Get(job, "container");
        var containerImage = container is YamlScalarNode ? container : Get(container, "image");
        if (!IsNull(containerImage))
        {
            yield return ("in container", ScalarText(containerImage!).Trim());
        }

        if (Get(job, "services") is YamlMappingNode services)
        {
            foreach (var service in services.Children)
            {
                var image = service.Value is YamlScalarNode ? service.Value : Get(service.Value, "image");
                if (!IsNull(image))
                {
                    yield return ($"service '{ScalarText(service.Key)}'", ScalarText(image!).Trim());
                }
            }
        }
    }

    private static IEnumerable<string> ActionImageProblems(string? uses, YamlNode? with)
    {
        string action = uses?.Split('@')[0].Trim() ?? "";
        string Input(string key) => Get(with, key) is { } node && !IsNull(node) ? ScalarText(node).Trim() : "";

        switch (action)
        {
            case "docker/setup-qemu-action":
                if (!DigestPin.IsMatch(Input("image")))
                {
                    yield return "leaves the binfmt `image` input unpinned (the default is tonistiigi/binfmt:latest)";
                }

                break;

            case "docker/setup-buildx-action":
                var driver = DriverImage.Match(Input("driver-opts"));
                if (Input("driver") is "" or "docker-container" && (!driver.Success || !DigestPin.IsMatch(driver.Groups["ref"].Value)))
                {
                    yield return "leaves the BuildKit image unpinned; set `driver-opts: image=name:tag@sha256:...`";
                }

                break;

            case "docker/build-push-action":
                string sbom = Input("sbom");
                if (sbom.Length > 0 && sbom != "false")
                {
                    var generator = Regex.Match(sbom, @"generator=(?<ref>[^\s,]+)");
                    if (!generator.Success || !DigestPin.IsMatch(generator.Groups["ref"].Value))
                    {
                        yield return "asks for an SBOM from an unpinned scanner; set `sbom: generator=name:tag@sha256:...`";
                    }
                }

                break;
        }
    }

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

    private static string ReadText(string path)
        => File.ReadAllText(path).Replace("\r\n", "\n").Replace("\r", "\n");

    private static List<string> Unique(List<string> values)
        => values.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList();
}
