#:property UseAppHost=false

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

return Tasks.Run(args);

internal static class Tasks
{
    private const string Usage =
        """
        Usage: dotnet run Tools/Tasks.cs <task> [<name> [<second>]] [options]
               dotnet run Tools/Tasks.cs help

        Thin wrappers over the dotnet and docker commands of this repository. Each task prints the
        command it runs before running it, so the printed line gives the same result without this
        file. The repository root is the nearest directory at or above this file holding
        AppTemplate.sln; the compose tasks and the vulnerability listing read the working directory.

        Tasks:
        <tasks>

        Options:
          --no-integration                 test: skip the projects that reference Testcontainers,
                                           which need a running Docker daemon
          --configuration <Debug|Release>  build, test, coverage, migration-bundle, verify; only after
                                           a -- separator, because dotnet run claims the name
          --access, --deletion, --operations, --model, --concurrency
                                           new-feature: forwarded to Tools/NewFeature.cs, whose
                                           --help says what each one means
          --apply                          remove-feature: delete an untouched generated skeleton
                                           instead of reporting what removing it involves

        Examples:
          dotnet run Tools/Tasks.cs test
          dotnet run Tools/Tasks.cs test --no-integration
          dotnet run Tools/Tasks.cs new-feature Widgets Widget --access shared
          dotnet run Tools/Tasks.cs remove-feature Widgets
          dotnet run Tools/Tasks.cs remove-feature Widgets --apply
          dotnet run Tools/Tasks.cs migration-add AddTodoItemPriority
          dotnet run Tools/Tasks.cs -- build --configuration Release
        """;

    private static readonly string[] KnownTasks =
    [
        "restore",
        "build",
        "test",
        "coverage",
        "format",
        "format-fix",
        "new-feature",
        "remove-feature",
        "migration-add",
        "database-update",
        "migration-bundle",
        "run",
        "compose-up",
        "compose-down",
        //#if (sonar)
        "sonar",
        //#endif
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
        if (arguments is ["--help"] or ["-h"] or ["help"])
        {
            Console.WriteLine(Usage.Replace("<tasks>", TaskList(), StringComparison.Ordinal));
            return 0;
        }

        string? task = null;
        string? name = null;
        string? second = null;

        List<string> featureOptions = [];
        bool noIntegration = false;
        bool apply = false;
        string configuration = "Debug";

        for (int i = 0; i < arguments.Length; i++)
        {
            string argument = arguments[i];

            if (argument is "--no-integration")
            {
                noIntegration = true;
            }
            else if (argument is "--apply")
            {
                apply = true;
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
            else if (FeatureOptionNames.Any(option => argument == option || argument.StartsWith(option + "=", StringComparison.Ordinal)))
            {
                featureOptions.Add(argument);

                if (!argument.Contains('=', StringComparison.Ordinal))
                {
                    if (i + 1 >= arguments.Length)
                    {
                        throw new InvalidOperationException($"'{argument}' needs a value.");
                    }

                    featureOptions.Add(arguments[++i]);
                }
            }
            else if (argument.StartsWith('-'))
            {
                throw new InvalidOperationException(
                    $"'{argument}' is not an option of this file. The options are --no-integration and "
                    + "--configuration <Debug|Release>, the second after a -- separator, and for new-feature "
                    + $"{string.Join(", ", FeatureOptionNames)}.");
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

        if (featureOptions.Count > 0 && task != "new-feature")
        {
            throw new InvalidOperationException(
                $"'{featureOptions[0]}' is an option of the new-feature task, not of '{task ?? "(no task)"}'.");
        }

        if (apply && task != "remove-feature")
        {
            throw new InvalidOperationException($"'--apply' is an option of the remove-feature task, not of '{task ?? "(no task)"}'.");
        }

        if (task is null)
        {
            throw new InvalidOperationException(
                "A task is required (help lists them with their options). It has to be one of these:" + Environment.NewLine + TaskList());
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
        //#if (localAuth)
        string auth = Path.Combine(repoRoot, "Src", "Infrastructure", "AppTemplate.Infrastructure.Auth");
        //#endif
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
                    var failed = new List<string>();

                    foreach (string project in TestProjects(repoRoot, dockerFreeOnly: true))
                    {
                        try
                        {
                            Step("dotnet", "test", project, "--configuration", configuration);
                        }
                        catch (InvalidOperationException error)
                        {
                            failed.Add(Path.GetFileNameWithoutExtension(project));
                            Write(error.Message, ConsoleColor.Red);
                        }
                    }

                    if (failed.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"{failed.Count} test project(s) failed:" + Environment.NewLine + string.Join(Environment.NewLine, failed.Select(name => "  " + name)));
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
                Step("dotnet", "format", solution);
                break;

            case "new-feature":
                Step(
                    "dotnet",
                    [
                        "run", Path.Combine(repoRoot, "Tools", "NewFeature.cs"),
                        RequiredSecond(task, name),
                        RequiredSecond(task, second),
                        "--root", repoRoot,
                        .. featureOptions,
                    ]);
                break;

            case "remove-feature":
                List<string> applyOption = apply ? ["--apply"] : [];
                Step(
                    "dotnet",
                    [
                        "run", Path.Combine(repoRoot, "Tools", "RemoveFeature.cs"),
                        RequiredName(task, name),
                        "--root", repoRoot,
                        .. applyOption,
                    ]);
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
                foreach ((string project, string output) in new[]
                {
                    //#if (localAuth)
                    (auth, "efbundle-identity"),
                    //#endif
                    (persistence, "efbundle"),
                })
                {
                    Step(
                        "dotnet", "ef", "migrations", "bundle",
                        "--project", project,
                        "--startup-project", project,
                        "--configuration", configuration,
                        "--self-contained",
                        "--force",
                        "--output", Path.Combine(repoRoot, "artifacts", "migrate", output));
                }

                break;

            case "run":
                Step("dotnet", "run", "--project", api);
                break;

            case "compose-up":
                Step("docker", "compose", "up", "-d", "--wait");
                break;

            case "compose-down":
                Step("docker", "compose", "down");
                break;

            //#if (sonar)
            case "sonar":
                Sonar(repoRoot);
                break;

            //#endif
            case "bootstrap":
                Step("dotnet", "format", solution);
                Step("dotnet", "format", solution, "--verify-no-changes", "--no-restore");

                try
                {
                    Step("dotnet", "tool", "restore");
                }
                catch (InvalidOperationException error)
                {
                    throw new InvalidOperationException(
                        error.Message + " The formatting is done and stable, so CI's first step will pass. The local tools are not restored, "
                        + "so the tasks that run dotnet ef cannot work yet: migration-add, database-update, migration-bundle and the last step of verify. "
                        + "Fix the restore and run `dotnet tool restore` again.");
                }

                //#if (anyWeb)
                WebWorkspace(repoRoot, "install");
                WebWorkspace(repoRoot, "api");
                //#endif
                Write("Formatting stable and tools restored. Commit this before anything else.", ConsoleColor.Green);
                break;

            case "hygiene":
                Gates(repoRoot);
                break;

            case "verify":
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
                //#if (localAuth)
                Step(
                    "dotnet", "ef", "migrations", "has-pending-model-changes",
                    "--project", auth,
                    "--startup-project", auth,
                    "--no-build");
                //#endif
                VulnerablePackages(solution);
                break;

            default:
                throw new InvalidOperationException($"'{task}' is listed as a task and has no branch.");
        }

        Write($"'{task}' completed.", ConsoleColor.Green);
        return 0;
    }

    private static void Gates(string repoRoot)
    {
        Step("dotnet", "run", Gate(repoRoot, "NewFeature.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "RemoveFeature.cs"), "--self-test");
        OptionalGate(repoRoot, "CheckDocPaths.cs", "--self-test");
        OptionalGate(repoRoot, "CheckDocPaths.cs", repoRoot);
        OptionalGate(repoRoot, "CheckWorkflows.cs", "--self-test");
        OptionalGate(repoRoot, "CheckWorkflows.cs", repoRoot);
        Step("dotnet", "run", Gate(repoRoot, "CoverageGate.cs"), "--self-test");
        OptionalGate(repoRoot, "CheckComments.cs", "--self-test");
        OptionalGate(repoRoot, "CheckComments.cs", repoRoot);
    }

    private static void OptionalGate(string repoRoot, string fileName, params string[] arguments)
    {
        string path = Path.Combine(repoRoot, "Tools", fileName);

        if (File.Exists(path))
        {
            Step("dotnet", ["run", path, .. arguments]);
        }
    }

    private static void Coverage(string repoRoot, string solution, string configuration, string runsettings)
    {
        string results = Path.Combine(repoRoot, "TestResults");
        if (Directory.Exists(results))
        {
            Directory.Delete(results, recursive: true);
        }

        Step(
            "dotnet",
            "test", solution,
            "--configuration", configuration,
            "--results-directory", results,
            "--coverage",
            "--coverage-settings", runsettings,
            "--coverage-output-format", "cobertura");

        string minimum = CoverageMinimum(Path.Combine(repoRoot, "coverage.minimum"));

        Step(
            "dotnet", "run", Gate(repoRoot, "CoverageGate.cs"),
            "--root", results,
            "--minimum", minimum);
    }

    //#if (sonar)
    private static void Sonar(string repoRoot)
    {
        Step("docker", "compose", "--profile", "sonar", "up", "--detach", "--wait", "sonarqube");

        Step(
            "docker", "compose", "--profile", "sonar",
            "run", "--rm", "--build", "sonar-scanner",
            "/bin/sh", "-c", ScanScript(repoRoot));
    }

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
            if [ -z "$SONAR_TOKEN" ]; then
              echo "SONAR_TOKEN is empty. Open http://localhost:9111, generate a token under" >&2
              echo "My Account > Security, and set SONAR_TOKEN in .env." >&2
              exit 1
            fi
            trap 'chown -R "$(stat -c %u:%g /repo/AppTemplate.sln)" /repo/artifacts /repo/.sonarqube 2>/dev/null || true' EXIT
            dotnet tool restore
            dotnet restore AppTemplate.sln --artifacts-path artifacts/sonar
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

    //#endif
    //#if (anyWeb)
    private static void WebWorkspace(string repoRoot, string script)
    {
        try
        {
            Step(OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm", "--dir", Path.Combine(repoRoot, "Src", "Web"), script);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                error.Message + " The web workspace needs Node.js and pnpm: run `corepack enable` once, then "
                + "`pnpm install` and `pnpm api` in Src/Web.");
        }
    }

    //#endif
    private static string Gate(string repoRoot, string fileName) => Path.Combine(repoRoot, "Tools", fileName);

    private static readonly string[] FeatureOptionNames = ["--access", "--deletion", "--operations", "--model", "--concurrency"];

    private static string TaskList() => "  " + string.Join(", ", KnownTasks);

    private static string RequiredSecond(string task, string? second)
    {
        if (string.IsNullOrWhiteSpace(second))
        {
            throw new InvalidOperationException(
                $"The '{task}' task needs two names and --access, e.g. dotnet run Tools/Tasks.cs {task} Widgets Widget --access shared");
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
