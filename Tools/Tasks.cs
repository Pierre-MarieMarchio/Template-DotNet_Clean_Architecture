#:property UseAppHost=false

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

return Tasks.Run(args);

/// <summary>
/// Thin wrappers over the real dotnet and docker commands used by this repository.
/// </summary>
/// <remarks>
/// <para>
/// The .NET SDK pinned by <c>global.json</c> is the only tool this file needs. Every task shells
/// out to <c>dotnet</c>, or to <c>docker</c> for the two compose tasks, and the hygiene gates are
/// file-based C# apps under <c>Tools/</c> that <c>dotnet run</c> compiles and caches on the spot.
/// Windows, Linux and macOS each run every task with nothing installed beyond that SDK, which is
/// the one tool any machine able to build the solution already has.
/// </para>
/// <para>
/// Every task prints the command it is about to run, so this file doubles as documentation: copy
/// the printed line and you get the same result without the script. Nothing here hides a flag that
/// changes the meaning of a build — in particular no task passes anything that would relax
/// TreatWarningsAsErrors.
/// </para>
/// <para>
/// The repository root is derived rather than assumed: the search starts at this file and climbs
/// to the directory holding <c>AppTemplate.sln</c>, so a task behaves the same from any working
/// directory. The compose tasks and <c>dotnet list package</c> are the deliberate exceptions —
/// they read the working directory, exactly as they do when typed by hand.
/// </para>
/// <para>
/// <c>--configuration</c> is a name <c>dotnet run</c> claims for itself, so it reaches this file
/// only after a <c>--</c> separator. <c>--no-integration</c> and the task name need no separator.
/// </para>
/// <example>
/// <code>
/// dotnet run Tools/Tasks.cs test
/// dotnet run Tools/Tasks.cs test --no-integration
/// dotnet run Tools/Tasks.cs migration-add AddTodoItemPriority
/// dotnet run Tools/Tasks.cs -- build --configuration Release
/// </code>
/// </example>
/// </remarks>
internal static class Tasks
{
    /// <summary>The task names. An argument outside this set is refused before anything runs.</summary>
    private static readonly string[] KnownTasks =
    [
        "restore",
        "build",
        "test",
        "coverage",
        "format",
        "format-fix",
        "new-feature",
        "migration-add",
        "database-update",
        "migration-bundle",
        "run",
        "compose-up",
        "compose-down",
        "sonar",
        "bootstrap",
        "hygiene",
        "verify",
    ];

    internal static int Run(string[] arguments)
    {
        try
        {
            return Dispatch(arguments);
        }
        catch (InvalidOperationException error)
        {
            Console.Out.Flush();
            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(error.Message);
            Console.ForegroundColor = previous;
            return 1;
        }
    }

    private static int Dispatch(string[] arguments)
    {
        string? task = null;
        string? name = null;

        // Only 'new-feature' takes two: the folder in the plural and the type in the singular.
        string? second = null;
        bool noIntegration = false;
        string configuration = "Debug";

        for (int i = 0; i < arguments.Length; i++)
        {
            string argument = arguments[i];

            if (argument is "--no-integration")
            {
                // Skip the Testcontainers suite, which needs a running Docker daemon.
                noIntegration = true;
            }
            else if (argument is "--configuration")
            {
                if (i + 1 >= arguments.Length)
                {
                    throw new InvalidOperationException("'--configuration' needs a value: Debug or Release.");
                }

                configuration = arguments[++i];
            }
            else if (argument.StartsWith("--configuration=", StringComparison.Ordinal))
            {
                configuration = argument["--configuration=".Length..];
            }
            else if (argument.StartsWith('-'))
            {
                throw new InvalidOperationException(
                    $"'{argument}' is not an option of this file. The options are --no-integration and "
                    + "--configuration <Debug|Release>, the second after a -- separator.");
            }
            else if (task is null)
            {
                task = argument;
            }
            else if (name is null)
            {
                name = argument;
            }
            else if (second is null)
            {
                second = argument;
            }
            else
            {
                throw new InvalidOperationException($"'{argument}' is one argument too many for the '{task}' task.");
            }
        }

        if (configuration is not ("Debug" or "Release"))
        {
            throw new InvalidOperationException(
                $"'{configuration}' does not belong to the set of configurations: Debug, Release. "
                + "Supply one of those and then try the command again.");
        }

        if (task is null)
        {
            throw new InvalidOperationException(
                "A task is required. It has to be one of these:" + Environment.NewLine + TaskList());
        }

        if (!KnownTasks.Contains(task, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{task}' does not belong to the set of tasks. Supply one of these and then try the command again:"
                + Environment.NewLine + TaskList());
        }

        string repoRoot = ResolveRepoRoot();
        string solution = Path.Combine(repoRoot, "AppTemplate.sln");
        string persistence = Path.Combine(repoRoot, "Src", "Infrastructure", "AppTemplate.Infrastructure.Persistence");
        string auth = Path.Combine(repoRoot, "Src", "Infrastructure", "AppTemplate.Infrastructure.Auth");
        string api = Path.Combine(repoRoot, "Src", "Presentation", "AppTemplate.Api");
        string runsettings = Path.Combine(repoRoot, "coverage.runsettings");

        switch (task)
        {
            case "restore":
                Step("dotnet", "restore", solution);
                break;

            case "build":
                Step("dotnet", "build", solution, "--configuration", configuration);
                break;

            case "test":
                if (noIntegration)
                {
                    foreach (string project in TestProjects(repoRoot, dockerFreeOnly: true))
                    {
                        Step("dotnet", "test", project, "--configuration", configuration);
                    }
                }
                else
                {
                    Step("dotnet", "test", solution, "--configuration", configuration);
                }

                break;

            case "coverage":
                Coverage(repoRoot, solution, configuration, runsettings);
                break;

            case "format":
                Step("dotnet", "format", solution, "--verify-no-changes", "--verbosity", "normal");
                break;

            case "format-fix":
                // Also rewrites *.cs without the UTF-8 BOM that .editorconfig requires. Run this
                // after creating a file by hand, or the next 'format' task fails on encoding alone.
                Step("dotnet", "format", solution);
                break;

            case "new-feature":
                // Two names, not one: the folder is the plural and the type is the singular.
                Step(
                    "dotnet", "run", Path.Combine(repoRoot, "Tools", "NewFeature.cs"),
                    RequiredName(task, name),
                    RequiredSecond(task, second),
                    repoRoot);
                break;

            case "migration-add":
                Step(
                    "dotnet", "ef", "migrations", "add", RequiredName(task, name),
                    "--project", persistence,
                    "--startup-project", persistence);
                break;

            case "database-update":
                Step(
                    "dotnet", "ef", "database", "update",
                    "--project", persistence,
                    "--startup-project", persistence);
                break;

            case "migration-bundle":
                // Self-contained executable that applies pending migrations. This is how a
                // deployment migrates: the API applies migrations at startup in Development only.
                Step(
                    "dotnet", "ef", "migrations", "bundle",
                    "--project", persistence,
                    "--startup-project", persistence,
                    "--configuration", configuration,
                    "--self-contained",
                    "--force",
                    "--output", Path.Combine(repoRoot, "artifacts", "migrate"));
                break;

            case "run":
                Step("dotnet", "run", "--project", api);
                break;

            case "compose-up":
                // `--wait` reports a failure when any service has exited, and minio-bucket is a
                // run-once container that exits 0 by design. It is not counted here because api and
                // worker name it under `condition: service_completed_successfully`, which tells
                // Compose it is a step rather than a service that should still be up. A one-shot
                // service nothing depends on would fail this task again, and should: nothing would
                // be sequencing it either.
                Step("docker", "compose", "up", "-d", "--wait");
                break;

            case "compose-down":
                // Volumes are kept: use `docker compose down -v` by hand to discard the database.
                Step("docker", "compose", "down");
                break;

            case "sonar":
                Sonar(repoRoot);
                break;

            case "bootstrap":
                // The local tools first. `verify` runs `dotnet ef`, and the manifest under .config/
                // pins it as a local tool, so without this the task documented as the first thing to
                // run leaves the gate one command short of working.
                Step("dotnet", "tool", "restore");

                // Then the formatting. Using directives are sorted alphabetically, so the correct
                // order depends on where the generated project's own namespace falls among
                // FluentValidation, Microsoft.* and the rest — which the template cannot know in
                // advance. Until this runs, `format --verify-no-changes` fails, and that is CI's
                // first step.
                Step("dotnet", "format", solution);
                Step("dotnet", "format", solution, "--verify-no-changes", "--no-restore");
                Write("Tools restored and formatting stable. Commit this before anything else.", ConsoleColor.Green);
                break;

            case "hygiene":
                // No solution build needed. Catches a doc path that resolves to nothing, a workflow
                // that would fail the first time it ran, and a comment that says what the code was
                // rather than what it is. Each gate runs its fixtures first: a green from a gate
                // that cannot go red proves nothing. The coverage gate's own run needs reports, so
                // only its fixtures belong here.
                Gates(repoRoot);
                break;

            case "verify":
                // The full gate, in the order CI runs it: formatting first because it is the
                // fastest to fail.
                Gates(repoRoot);
                Step("dotnet", "restore", solution);
                Step("dotnet", "format", solution, "--verify-no-changes", "--no-restore");
                Step("dotnet", "build", solution, "--configuration", configuration, "--no-restore");
                Step("dotnet", "test", solution, "--configuration", configuration, "--no-build");
                // Both contexts, because each has a history of its own: a model whose snapshot
                // nobody compares is a model nobody migrates, and the other half's migrations
                // would go on applying cleanly while this half's tables were simply absent.
                Step(
                    "dotnet", "ef", "migrations", "has-pending-model-changes",
                    "--project", persistence,
                    "--startup-project", persistence,
                    "--no-build");
                Step(
                    "dotnet", "ef", "migrations", "has-pending-model-changes",
                    "--project", auth,
                    "--startup-project", auth,
                    "--no-build");
                VulnerablePackages(solution);
                break;

            default:
                throw new InvalidOperationException($"'{task}' is listed as a task and has no branch.");
        }

        Write($"'{task}' completed.", ConsoleColor.Green);
        return 0;
    }

    /// <summary>The five hygiene gates, each preceded by the fixtures that prove it can go red.</summary>
    private static void Gates(string repoRoot)
    {
        // The scaffolder is not a gate over the tree, but its self-test is what keeps its templates
        // from drifting away from the shapes the rules below check.
        Step("dotnet", "run", Gate(repoRoot, "NewFeature.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "CheckDocPaths.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "CheckDocPaths.cs"), repoRoot);
        Step("dotnet", "run", Gate(repoRoot, "CheckWorkflows.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "CheckWorkflows.cs"), repoRoot);
        Step("dotnet", "run", Gate(repoRoot, "CoverageGate.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "CheckNarrativeComments.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "CheckNarrativeComments.cs"), repoRoot);
    }

    private static void Coverage(string repoRoot, string solution, string configuration, string runsettings)
    {
        string results = Path.Combine(repoRoot, "TestResults");
        if (Directory.Exists(results))
        {
            Directory.Delete(results, recursive: true);
        }

        // The whole solution in one invocation, architecture suite included: the platform runs
        // every test module the solution names, and this collector does not break the type
        // resolution NetArchTest does through Type.GetType(name, throwOnError: true). Discovery
        // per project is not needed here and would be a second copy of what the solution states.
        //
        // The switches are the platform's, and the coverage ones only look like the VSTest
        // spellings: '--settings' is accepted and then ignored, so the settings file has to arrive
        // through '--coverage-settings' or every exclusion in it goes silently unapplied.
        Step(
            "dotnet",
            "test", solution,
            "--configuration", configuration,
            "--results-directory", results,
            "--coverage",
            "--coverage-settings", runsettings,
            "--coverage-output-format", "cobertura");

        // Same floor CI enforces, read from the same file.
        string minimum = CoverageMinimum(Path.Combine(repoRoot, "coverage.minimum"));

        Step(
            "dotnet", "run", Gate(repoRoot, "CoverageGate.cs"),
            "--root", results,
            "--minimum", minimum);
    }

    /// <summary>
    /// The local Sonar analysis: a server and a scanner, both in containers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No JRE on the machine, which is the whole reason this is a task rather than a paragraph
    /// telling you to install one. SonarScanner for .NET is a .NET tool that shells out to Java,
    /// and <c>Tools/sonar-scanner.Dockerfile</c> is where that Java lives, so the prerequisites
    /// stay what <c>docs/BUILD-AND-CI.md</c> says they are: the SDK from <c>global.json</c>, and Docker.
    /// </para>
    /// <para>
    /// Build output goes to <c>artifacts/sonar</c>, which is gitignored: the per-project
    /// <c>bin/</c> and <c>obj/</c> the host build owns are left alone, so an analysis does not
    /// leave the tree holding paths that exist only inside a container and the next build on the
    /// machine still works. It stays <em>inside</em> the checkout, and that part is not a detail --
    /// the architecture suite finds the repository root by climbing from its own assembly, looking
    /// for the directory holding both <c>Directory.Packages.props</c> and <c>Src</c>. Built to a
    /// path outside the checkout, all of it fails in the type initialiser before a single rule
    /// runs.
    /// </para>
    /// <para>
    /// The token is never read here: Compose takes it from <c>.env</c> and refuses to start with a
    /// message saying where to generate one, which beats an authentication error raised deep inside
    /// the scanner.
    /// </para>
    /// <para>
    /// What the container writes is handed back to the owner of the checkout before it exits. The
    /// SDK image runs as root, so without that the two directories an analysis creates are owned by
    /// root inside a developer's own tree, and removing them needs sudo.
    /// </para>
    /// </remarks>
    private static void Sonar(string repoRoot)
    {
        // Waited on, not assumed. A first boot builds the database and the search indices, and an
        // analysis sent before the API is ready fails in a way that reads like a bad token.
        Step("docker", "compose", "--profile", "sonar", "up", "--detach", "--wait", "sonarqube");

        Step(
            "docker", "compose", "--profile", "sonar",
            "run", "--rm", "--build", "sonar-scanner",
            "/bin/sh", "-c", ScanScript(repoRoot));
    }

    /// <summary>
    /// The analysis, in the one order the scanner accepts: <c>begin</c>, a build it can watch, then
    /// <c>end</c>. The scanner learns which file belongs to which project by observing MSBuild, so
    /// a build that already happened is invisible to it and nothing here may skip one.
    /// </summary>
    /// <remarks>
    /// SCM is off here and on in CI, which is not an oversight in either place. Locally it buys
    /// nothing -- blame dates, issue authorship and a new-code baseline are noise on a throwaway
    /// instance -- and it costs correctness: in a git worktree, <c>.git</c> is a file naming an
    /// absolute path on the host, so the scanner's git client finds no repository inside the
    /// container and fails the whole run at the very end, after the build and the tests. CI
    /// analyses a plain clone with full history, where blame is the thing that makes "new code"
    /// mean anything.
    /// </remarks>
    /// <remarks>
    /// The Testcontainers suite is left out, and the coverage this reports is lower than CI's
    /// because of it. There is no Docker daemon inside the scanner container, so those tests cannot
    /// run there at all; the set is the same one <c>test --no-integration</c> uses, derived from
    /// the projects themselves rather than listed. CI runs the whole suite, and
    /// <c>.github/workflows/sonarqube.yml</c> is what reports the real figure.
    /// </remarks>
    private static string ScanScript(string repoRoot)
    {
        IEnumerable<string> tests = TestProjects(repoRoot, dockerFreeOnly: true)
            .Select(project => Path.GetRelativePath(repoRoot, project).Replace('\\', '/'))
            .Select(project =>
                $"""
                dotnet test "{project}" \
                  --artifacts-path artifacts/sonar \
                  --no-build \
                  --results-directory artifacts/sonar/TestResults \
                  --coverage \
                  --coverage-settings coverage.runsettings \
                  --coverage-output-format cobertura
                """);

        return $"""
            set -eu
            # The name carries no braces on purpose. This script is built from a C# interpolated
            # raw string, so a brace opens an interpolation hole rather than a shell expansion, and
            # the braced spelling does not compile. Compose always defines SONAR_TOKEN, defaulting
            # it to empty, so the plain form is safe under `set -u`.
            if [ -z "$SONAR_TOKEN" ]; then
              echo "SONAR_TOKEN is empty. Open http://localhost:9111, generate a token under" >&2
              echo "My Account > Security, and set SONAR_TOKEN in .env." >&2
              exit 1
            fi
            # The image is the .NET SDK's, whose user is root, so everything written through the
            # mount lands owned by root -- inside the developer's own checkout. Left that way,
            # artifacts/sonar and .sonarqube cannot be deleted, `git clean` fails on them, and the
            # tree needs sudo to tidy. The owner of the checkout is read off a file that is
            # certainly in it, and the outputs are handed back on the way out.
            #
            # artifacts and not artifacts/sonar: the parent is created by the same root process and
            # stays root-owned otherwise, which blocks removing the directory just as effectively.
            # Anything else already under artifacts belongs to that owner, so it is chowned to what
            # it already is.
            #
            # A trap and not a last line: the build or the tests failing is exactly when this is
            # skipped otherwise, and `set -e` would leave the mess behind on the runs that already
            # went badly.
            trap 'chown -R "$(stat -c %u:%g /repo/AppTemplate.sln)" /repo/artifacts /repo/.sonarqube 2>/dev/null || true' EXIT
            dotnet tool restore
            dotnet restore AppTemplate.sln --artifacts-path artifacts/sonar
            # The flags match .github/workflows/sonarqube.yml, duplication exclusion included, for
            # the reasons stated there. A local run reaching a different verdict from CI's would be
            # a second analysis rather than a preview of the one that decides.
            dotnet sonarscanner begin \
              /k:"$SONAR_PROJECT_KEY" \
              /d:sonar.token="$SONAR_TOKEN" \
              /d:sonar.host.url="$SONAR_HOST_URL" \
              /d:sonar.cs.cobertura.reportsPaths="artifacts/sonar/TestResults/**/*.cobertura.xml" \
              /d:sonar.cpd.exclusions="**/*.html" \
              /d:sonar.scanner.scanAll=false \
              /d:sonar.scm.disabled=true
            dotnet build AppTemplate.sln --artifacts-path artifacts/sonar --no-restore
            {string.Join(Environment.NewLine, tests)}
            dotnet sonarscanner end /d:sonar.token="$SONAR_TOKEN"
            """;
    }

    private static string Gate(string repoRoot, string fileName) => Path.Combine(repoRoot, "Tools", fileName);

    private static string TaskList() => "  " + string.Join(", ", KnownTasks);

    private static string RequiredSecond(string task, string? second)
    {
        if (string.IsNullOrWhiteSpace(second))
        {
            throw new InvalidOperationException(
                $"The '{task}' task needs two names, e.g. dotnet run Tools/Tasks.cs {task} Widgets Widget");
        }

        return second;
    }

    private static string RequiredName(string task, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                $"The '{task}' task needs a name, e.g. dotnet run Tools/Tasks.cs {task} AddTodoItemPriority");
        }

        return name;
    }

    /// <summary>
    /// The floor is the first line of <c>coverage.minimum</c> that <em>is</em> a number. CI reads the
    /// same file with a POSIX shell, so the gate a developer runs locally is the gate CI runs — and
    /// "the first line that is a number" is the one definition both can hold to.
    /// </summary>
    /// <remarks>
    /// Deliberately not "the first line that is not a comment", which the two cannot agree on: a
    /// UTF-8 BOM sits in front of a leading <c>#</c>, and <c>File.ReadLines</c> strips it while GNU
    /// grep does not — so that definition read 85 here and the whole comment line in CI.
    /// </remarks>
    private static string CoverageMinimum(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            string candidate = line.Trim();

            if (double.TryParse(candidate, CultureInfo.InvariantCulture, out _))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"'{path}' states no coverage floor: no line is a bare number.");
    }

    /// <summary>
    /// Test projects are discovered from disk rather than listed, so a project added under
    /// <c>Tests/</c> runs without an edit here. The Docker-free set is selected by the property
    /// that actually makes a project need Docker — a Testcontainers package reference in its
    /// csproj — so a project that adopts Testcontainers leaves the <c>--no-integration</c> set on
    /// its own.
    /// </summary>
    private static string[] TestProjects(string repoRoot, bool dockerFreeOnly = false)
    {
        string tests = Path.Combine(repoRoot, "Tests");
        IEnumerable<string> projects = Directory.Exists(tests)
            ? Directory.EnumerateFiles(tests, "*.csproj", SearchOption.AllDirectories)
            : [];

        if (dockerFreeOnly)
        {
            projects = projects.Where(
                path => !File.ReadAllText(path).Contains("Testcontainers", StringComparison.OrdinalIgnoreCase));
        }

        string[] paths = [.. projects.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
        if (paths.Length == 0)
        {
            throw new InvalidOperationException(
                "Discovery found no test project under Tests/. A run over nothing is a false green.");
        }

        return paths;
    }

    /// <summary>
    /// The repository root is the nearest directory at or above this file that holds
    /// <c>AppTemplate.sln</c>.
    /// </summary>
    private static string ResolveRepoRoot()
    {
        string start = Path.GetDirectoryName(SourceFile()) ?? string.Empty;
        if (!Directory.Exists(start))
        {
            start = Directory.GetCurrentDirectory();
        }

        for (DirectoryInfo? directory = new(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AppTemplate.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"AppTemplate.sln is in neither '{start}' nor any directory above it, so the repository root cannot "
            + "be derived. Run this file from the checkout it belongs to.");
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;

    private static void Write(string message, ConsoleColor color)
    {
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ForegroundColor = previous;
    }

    /// <summary>
    /// Prints the command, runs it, and stops the task on a non-zero exit code with a message that
    /// names both the command and the code. The child inherits stdout and stderr, so a build or a
    /// test run scrolls past live rather than arriving in a block at the end.
    /// </summary>
    /// <summary>
    /// The vulnerability listing, read rather than trusted.
    /// </summary>
    /// <remarks>
    /// <c>dotnet list package --vulnerable</c> exits zero whether or not it finds anything, so a
    /// step that only inspects the exit code passes for every input and asserts nothing. The
    /// sentence it prints when it has something to report is the only signal it gives, which is
    /// why CI greps for it and why this does the same rather than running the command for show.
    /// </remarks>
    private static void VulnerablePackages(string solution)
    {
        const string marker = "has the following vulnerable packages";

        string line = $"dotnet list {solution} package --vulnerable --include-transitive";
        Write("> " + line, ConsoleColor.Cyan);
        Console.Out.Flush();

        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in new[] { "list", solution, "package", "--vulnerable", "--include-transitive" })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("'dotnet' started no process.");

        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Console.Write(output);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{line}' exited with {process.ExitCode}.");
        }

        if (output.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A package with a known vulnerability is referenced, directly or transitively. The "
                + "listing above names it. Pin a patched version in Directory.Packages.props, under a "
                + "Security pins label, with a comment saying when the pin can be removed.");
        }
    }

    private static void Step(string executable, params string[] arguments)
    {
        string line = executable + " " + string.Join(' ', arguments);
        Write("> " + line, ConsoleColor.Cyan);
        Console.Out.Flush();

        ProcessStartInfo startInfo = new() { FileName = executable, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"'{executable}' started no process.");
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException($"'{executable}' is not on PATH.");
        }

        using (process)
        {
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"'{line}' exited with {process.ExitCode}.");
            }
        }
    }
}
