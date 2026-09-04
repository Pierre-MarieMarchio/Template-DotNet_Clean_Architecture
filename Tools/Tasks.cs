#:property UseAppHost=false

using System.ComponentModel;
using System.Diagnostics;
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
        "migration-add",
        "database-update",
        "migration-bundle",
        "run",
        "compose-up",
        "compose-down",
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
                Coverage(repoRoot, configuration, runsettings);
                break;

            case "format":
                Step("dotnet", "format", solution, "--verify-no-changes", "--verbosity", "normal");
                break;

            case "format-fix":
                // Also rewrites *.cs without the UTF-8 BOM that .editorconfig requires. Run this
                // after creating a file by hand, or the next 'format' task fails on encoding alone.
                Step("dotnet", "format", solution);
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
                Step("docker", "compose", "up", "-d", "--wait");
                break;

            case "compose-down":
                // Volumes are kept: use `docker compose down -v` by hand to discard the database.
                Step("docker", "compose", "down");
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
                Step(
                    "dotnet", "ef", "migrations", "has-pending-model-changes",
                    "--project", persistence,
                    "--startup-project", persistence,
                    "--no-build");
                VulnerablePackages(solution);
                break;

            default:
                throw new InvalidOperationException($"'{task}' is listed as a task and has no branch.");
        }

        Write($"'{task}' completed.", ConsoleColor.Green);
        return 0;
    }

    /// <summary>The four hygiene gates, each preceded by the fixtures that prove it can go red.</summary>
    private static void Gates(string repoRoot)
    {
        Step("dotnet", "run", Gate(repoRoot, "CheckDocPaths.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "CheckDocPaths.cs"), repoRoot);
        Step("dotnet", "run", Gate(repoRoot, "CheckWorkflows.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "CheckWorkflows.cs"), repoRoot);
        Step("dotnet", "run", Gate(repoRoot, "CoverageGate.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "CheckNarrativeComments.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "CheckNarrativeComments.cs"), repoRoot);
    }

    private static void Coverage(string repoRoot, string configuration, string runsettings)
    {
        string results = Path.Combine(repoRoot, "TestResults");
        if (Directory.Exists(results))
        {
            Directory.Delete(results, recursive: true);
        }

        // AppTemplate.Architecture.Tests is excluded on purpose: NetArchTest resolves types through
        // Type.GetType(name, throwOnError: true), which fails against a Coverlet-instrumented
        // assembly, so 7 of its rules throw under the collector and all pass without it.
        // Run 'dotnet run Tools/Tasks.cs test' for those.
        foreach (string project in TestProjects(repoRoot, excludeContaining: "AppTemplate.Architecture.Tests"))
        {
            Step(
                "dotnet",
                "test", project,
                "--configuration", configuration,
                // Two elements, not '--collect:XPlat Code Coverage': the value contains a space,
                // and each element reaches the child process whole.
                "--collect", "XPlat Code Coverage",
                "--settings", runsettings,
                "--results-directory", results);
        }

        // Same floor CI enforces, read from the same file.
        string minimum = CoverageMinimum(Path.Combine(repoRoot, "coverage.minimum"));

        Step(
            "dotnet", "run", Gate(repoRoot, "CoverageGate.cs"),
            "--root", results,
            "--minimum", minimum);
    }

    private static string Gate(string repoRoot, string fileName) => Path.Combine(repoRoot, "Tools", fileName);

    private static string TaskList() => "  " + string.Join(", ", KnownTasks);

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
    /// The floor is the first line of <c>coverage.minimum</c> that is neither blank nor a comment.
    /// CI reads the same file, so the gate a developer runs locally is the gate CI runs.
    /// </summary>
    private static string CoverageMinimum(string path)
    {
        foreach (string line in File.ReadLines(path))
        {
            if (!line.TrimStart().StartsWith('#') && line.Trim().Length > 0)
            {
                return line.Trim();
            }
        }

        throw new InvalidOperationException($"'{path}' states no coverage floor: every line is blank or a comment.");
    }

    /// <summary>
    /// Test projects are discovered from disk rather than listed, so a project added under
    /// <c>Tests/</c> runs without an edit here. The Docker-free set is selected by the property
    /// that actually makes a project need Docker — a Testcontainers package reference in its
    /// csproj — so a project that adopts Testcontainers leaves the <c>--no-integration</c> set on
    /// its own.
    /// </summary>
    private static string[] TestProjects(string repoRoot, bool dockerFreeOnly = false, string? excludeContaining = null)
    {
        string tests = Path.Combine(repoRoot, "Tests");
        IEnumerable<string> projects = Directory.Exists(tests)
            ? Directory.EnumerateFiles(tests, "*.csproj", SearchOption.AllDirectories)
            : [];

        if (!string.IsNullOrEmpty(excludeContaining))
        {
            projects = projects.Where(path => !path.Contains(excludeContaining, StringComparison.OrdinalIgnoreCase));
        }

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
                + "listing above names it. Pin a patched version in Directory.Packages.props, in the "
                + "Security pins group, with a comment saying when the pin can be removed.");
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
