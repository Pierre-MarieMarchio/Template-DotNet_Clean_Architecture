#:property UseAppHost=false

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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
        "web-scaffold",
        "hygiene",
        "verify",
        //#if (templateMaintenance)
        "web-lockfiles",
        //#endif
    ];

    private const string WebUsage =
        """

        Web apps:
          web-scaffold [<App>]             create each app under Src/Web/apps that holds a scaffold.json
                                           and no package.json, or only <App>: run the framework's
                                           official CLI at the version scaffold.json pins, lay the
                                           app's overlay/ over the starter, pin every dependency to the
                                           version installed, then pnpm install and pnpm api;
                                           bootstrap runs it, and an app already created is left alone
        """;
    //#if (templateMaintenance)

    private const string MaintenanceUsage =
        """

        Template repository only:
          web-lockfiles                    write the lockfile of each generated web shape: for every
                                           app under Src/Web/apps holding a package.json, a copy of
                                           the workspace holding that app alone, locked by the pinned
                                           pnpm, into .template.config/variants/web-<App>/Src/Web/
                                           pnpm-lock.yaml; an app holding a scaffold.json gets none,
                                           since web-scaffold writes its lockfile where it is created
          web-lockfiles --check            fail when one of them differs from what the command writes;
                                           verify runs it
        """;
    //#endif

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
            Console.WriteLine(WebUsage);
            //#if (templateMaintenance)
            Console.WriteLine(MaintenanceUsage);
            //#endif
            return 0;
        }

        string? task = null;
        string? name = null;
        string? second = null;

        List<string> featureOptions = [];
        bool noIntegration = false;
        bool apply = false;
        //#if (templateMaintenance)
        bool check = false;
        //#endif
        string configuration = "Debug";

        for (int i = 0; i < arguments.Length; i++)
        {
            string argument = arguments[i];

            if (argument is "--no-integration")
            {
                noIntegration = true;
            }
            //#if (templateMaintenance)
            else if (argument is "--check")
            {
                check = true;
            }
            //#endif
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
        //#if (templateMaintenance)

        if (check && task != "web-lockfiles")
        {
            throw new InvalidOperationException($"'--check' is an option of the web-lockfiles task, not of '{task ?? "(no task)"}'.");
        }
        //#endif

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

                if (File.Exists(Path.Combine(repoRoot, "Src", "Web", "package.json")))
                {
                    WebScaffold(repoRoot, only: null);
                }

                Write("Formatting stable and tools restored. Commit this before anything else.", ConsoleColor.Green);
                break;

            case "hygiene":
                Gates(repoRoot);
                break;

            case "verify":
                Gates(repoRoot);
                //#if (templateMaintenance)
                WebLockfiles(repoRoot, check: true);
                //#endif
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

            case "web-scaffold":
                WebScaffold(repoRoot, name);
                break;

            //#if (templateMaintenance)
            case "web-lockfiles":
                WebLockfiles(repoRoot, check);
                break;

            //#endif
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
    private const string ScaffoldDescriptor = "scaffold.json";

    private static readonly string Pnpm = OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";

    private static readonly Regex ExactVersion = new(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$");

    private static readonly JsonSerializerOptions WrittenJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void WebWorkspace(string repoRoot, params string[] arguments)
    {
        try
        {
            StepIn(Path.Combine(repoRoot, "Src", "Web"), Pnpm, arguments);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                error.Message + " The web workspace needs Node.js and pnpm: run `corepack enable` once, then "
                + "`pnpm install` and `pnpm api` in Src/Web.");
        }
    }

    private static void WebScaffold(string repoRoot, string? only)
    {
        string web = Path.Combine(repoRoot, "Src", "Web");

        if (!File.Exists(Path.Combine(web, "package.json")))
        {
            throw new InvalidOperationException(
                "Src/Web holds no package.json, so there is no web workspace to create an app in. "
                + "dotnet new cleanarch-webapp --workspace adds a first app and the workspace around it.");
        }

        string apps = Path.Combine(web, "apps");
        string[] pending = Directory.Exists(apps)
            ? [.. Directory.GetDirectories(apps)
                .Where(folder => File.Exists(Path.Combine(folder, ScaffoldDescriptor)) && !File.Exists(Path.Combine(folder, "package.json")))
                .Order(StringComparer.Ordinal)]
            : [];

        if (only is not null)
        {
            string folder = Path.Combine(apps, only);

            if (File.Exists(Path.Combine(folder, "package.json")))
            {
                Write($"Src/Web/apps/{only} holds a package.json: it is created already, and left alone.", ConsoleColor.Green);
                pending = [];
            }
            else if (pending.Contains(folder, StringComparer.Ordinal))
            {
                pending = [folder];
            }
            else
            {
                throw new InvalidOperationException(
                    $"Src/Web/apps/{only} holds no {ScaffoldDescriptor}, so there is nothing to create. "
                    + "dotnet new cleanarch-webapp writes one in the app's folder.");
            }
        }

        foreach (string folder in pending)
        {
            ScaffoldApp(web, folder);
        }

        WebWorkspace(repoRoot, pending.Length > 0 ? ["install", "--no-frozen-lockfile"] : ["install"]);

        string[] created = Directory.Exists(apps)
            ? [.. Directory.GetDirectories(apps).Where(folder => File.Exists(Path.Combine(folder, "package.json")))]
            : [];
        bool[] pinned = [.. created.Select(PinInstalledVersions)];

        if (pinned.Contains(true))
        {
            WebWorkspace(repoRoot, "install", "--no-frozen-lockfile");
        }

        WebWorkspace(repoRoot, "api");

        if (pending.Length > 0)
        {
            Write(
                $"Created {string.Join(", ", pending.Select(Path.GetFileName))}. Commit the app with Src/Web/pnpm-lock.yaml: "
                + "pnpm install --frozen-lockfile, CI and the image build read the versions it records.",
                ConsoleColor.Green);
        }
    }

    private static void ScaffoldApp(string web, string folder)
    {
        string app = Path.GetFileName(folder);
        JsonObject descriptor = ReadJson(Path.Combine(folder, ScaffoldDescriptor));
        string name = Text(descriptor, "name", app);
        string cli = Text(descriptor, "package", app) + "@" + Text(descriptor, "version", app);
        string scratch = Path.Combine(web, ".web-scaffold-" + Guid.NewGuid().ToString("N"));
        string starter = Path.Combine(scratch, name);
        string overlay = Path.Combine(folder, "overlay");

        string[] arguments =
        [
            "dlx", cli,
            .. (descriptor["arguments"]?.AsArray() ?? []).Select(argument => (argument?.GetValue<string>() ?? string.Empty)
                .Replace("{name}", name, StringComparison.Ordinal)
                .Replace("{directory}", Path.GetRelativePath(web, starter).Replace('\\', '/'), StringComparison.Ordinal)),
        ];

        try
        {
            Directory.CreateDirectory(scratch);

            try
            {
                StepIn(web, Pnpm, arguments);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException(
                    $"{error.Message} Creating Src/Web/apps/{app} runs {cli}, the framework's official CLI, which pnpm fetches "
                    + "from the npm registry, so this step needs the network; the output above says what failed. The app's folder "
                    + $"is unchanged: run `dotnet run Tools/Tasks.cs web-scaffold {app}` again once the cause is fixed.");
            }

            if (!File.Exists(Path.Combine(starter, "package.json")))
            {
                throw new InvalidOperationException($"{cli} wrote no package.json in {starter}, so Src/Web/apps/{app} is left unchanged.");
            }

            if (Directory.Exists(overlay))
            {
                CopyFiles(overlay, starter, overwrite: true);
            }

            foreach (string removed in (descriptor["remove"]?.AsArray() ?? []).Select(path => Path.Combine(starter, path?.GetValue<string>() ?? ".")))
            {
                if (Directory.Exists(removed))
                {
                    Directory.Delete(removed, recursive: true);
                }

                File.Delete(removed);
            }

            foreach ((string file, JsonNode? patch) in descriptor["merge"]?.AsObject() ?? [])
            {
                string path = Path.Combine(starter, file);
                JsonObject merged = ReadJson(path);
                Merge(merged, patch?.AsObject() ?? []);
                File.WriteAllText(path, merged.ToJsonString(WrittenJson) + "\n");
            }

            CopyFiles(starter, folder, overwrite: false);
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        if (Directory.Exists(overlay))
        {
            Directory.Delete(overlay, recursive: true);
        }

        File.Delete(Path.Combine(folder, ScaffoldDescriptor));
        Write($"Created Src/Web/apps/{app} from {cli} and its overlay.", ConsoleColor.Green);
    }

    private static bool PinInstalledVersions(string folder)
    {
        string manifest = Path.Combine(folder, "package.json");
        JsonObject package = ReadJson(manifest);
        bool changed = false;

        foreach (JsonObject dependencies in new[] { package["dependencies"], package["devDependencies"] }.OfType<JsonObject>())
        {
            foreach ((string dependency, JsonNode? range) in dependencies.ToList())
            {
                string declared = range?.GetValue<string>() ?? string.Empty;

                if (declared.StartsWith("workspace:", StringComparison.Ordinal) || ExactVersion.IsMatch(declared))
                {
                    continue;
                }

                string installed = Path.Combine(folder, "node_modules", dependency, "package.json");

                if (!File.Exists(installed))
                {
                    throw new InvalidOperationException($"{manifest} declares {dependency} {declared}, and pnpm installed no version of it.");
                }

                dependencies[dependency] = Text(ReadJson(installed), "version", installed);
                changed = true;
            }
        }

        if (changed)
        {
            File.WriteAllText(manifest, package.ToJsonString(WrittenJson) + "\n");
        }

        return changed;
    }

    private static void Merge(JsonObject target, JsonObject patch)
    {
        foreach ((string key, JsonNode? value) in patch)
        {
            if (value is null)
            {
                target.Remove(key);
            }
            else if (value is JsonObject nested && target[key] is JsonObject existing)
            {
                Merge(existing, nested);
            }
            else
            {
                target[key] = value.DeepClone();
            }
        }
    }

    private static JsonObject ReadJson(string path) =>
        JsonNode.Parse(
            File.ReadAllText(path),
            documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
        ?? throw new InvalidOperationException($"{path} holds no JSON object.");

    private static string Text(JsonObject json, string key, string owner) =>
        json[key]?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"'{key}' is missing from the JSON of {owner}.");

    private static void CopyFiles(string source, string target, bool overwrite)
    {
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(source))
        {
            string copy = Path.Combine(target, Path.GetFileName(file));

            if (overwrite || !File.Exists(copy))
            {
                File.Copy(file, copy, overwrite);
            }
        }

        foreach (string folder in Directory.GetDirectories(source))
        {
            CopyFiles(folder, Path.Combine(target, Path.GetFileName(folder)), overwrite);
        }
    }

    //#if (templateMaintenance)
    private static void WebLockfiles(string repoRoot, bool check)
    {
        string web = Path.Combine(repoRoot, "Src", "Web");
        string appsFolder = Path.Combine(web, "apps");
        string variants = Path.Combine(repoRoot, ".template.config", "variants");
        string templateJson = File.ReadAllText(Path.Combine(repoRoot, ".template.config", "template.json"));

        string[] apps = Directory.Exists(appsFolder)
            ? [.. Directory.GetDirectories(appsFolder)
                .Where(folder => File.Exists(Path.Combine(folder, "package.json")))
                .Select(folder => Path.GetFileName(folder))
                .Order(StringComparer.Ordinal)]
            : [];

        if (apps.Length == 0)
        {
            throw new InvalidOperationException("Src/Web/apps holds no app with a package.json, so there is no generated web shape to lock.");
        }

        List<string> problems = [];
        string scratch = Path.Combine(Path.GetTempPath(), "web-lockfiles-" + Guid.NewGuid().ToString("N"));

        try
        {
            foreach (string app in apps)
            {
                string copy = Path.Combine(scratch, app);
                CopyWorkspace(web, copy, app);
                StepIn(copy, OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm", "install", "--lockfile-only", "--no-frozen-lockfile");

                string locked = Lines(File.ReadAllText(Path.Combine(copy, "pnpm-lock.yaml")));
                string variant = Path.Combine(variants, "web-" + app, "Src", "Web", "pnpm-lock.yaml");
                string shown = Path.GetRelativePath(repoRoot, variant).Replace('\\', '/');
                bool current = File.Exists(variant) && Lines(File.ReadAllText(variant)) == locked;

                if (!current && check)
                {
                    problems.Add($"{shown} is {(File.Exists(variant) ? "stale" : "missing")}.");
                }
                else if (!current)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(variant)!);
                    File.WriteAllText(variant, locked);
                    Write($"Wrote {shown}.", ConsoleColor.Green);
                }

                string source = $"\"source\": \"./.template.config/variants/web-{app}/\"";

                if (!templateJson.Contains(source, StringComparison.Ordinal))
                {
                    problems.Add(
                        $".template.config/template.json does not generate {shown}: add {{ {source}, \"target\": \"./\", "
                        + "\"condition\": \"(<the symbol choosing this app>)\" } to its sources.");
                }
            }

            foreach (string orphan in Directory.GetDirectories(variants, "web-*"))
            {
                string shown = Path.GetRelativePath(repoRoot, orphan).Replace('\\', '/');

                if (apps.Contains(Path.GetFileName(orphan)["web-".Length..], StringComparer.Ordinal))
                {
                    continue;
                }

                if (check)
                {
                    problems.Add($"{shown} locks an app Src/Web/apps no longer holds.");
                }
                else
                {
                    Directory.Delete(orphan, recursive: true);
                    Write($"Removed {shown}.", ConsoleColor.Green);
                }
            }
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, problems) + Environment.NewLine
                + (check ? "Run `dotnet run Tools/Tasks.cs web-lockfiles` and commit what it writes." : "Fix the above and run the task again."));
        }

        Write($"{apps.Length} generated web shape(s) locked: {string.Join(", ", apps)}.", ConsoleColor.Green);
    }

    private static void CopyWorkspace(string web, string target, string app)
    {
        string apps = Path.Combine(web, "apps");
        string[] outputs = ["node_modules", "dist", "coverage", ".angular"];

        CopyTree(web, target, folder =>
            outputs.Contains(Path.GetFileName(folder), StringComparer.Ordinal)
            || (Path.GetDirectoryName(folder) == apps && Path.GetFileName(folder) != app));
    }

    private static void CopyTree(string source, string target, Func<string, bool> skip)
    {
        Directory.CreateDirectory(target);

        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (string folder in Directory.GetDirectories(source).Where(folder => !skip(folder)))
        {
            CopyTree(folder, Path.Combine(target, Path.GetFileName(folder)), skip);
        }
    }

    private static string Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

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

    private static void Step(string executable, params string[] arguments) => StepIn(null, executable, arguments);

    private static void StepIn(string? directory, string executable, params string[] arguments)
    {
        string line = executable + " " + string.Join(' ', arguments);
        Write("> " + line + (directory is null ? string.Empty : $"   (in {directory})"), ConsoleColor.Cyan);
        Console.Out.Flush();

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = directory ?? string.Empty,
        };
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
