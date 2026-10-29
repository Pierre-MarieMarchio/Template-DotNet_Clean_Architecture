#:property UseAppHost=false

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

        Migrations:
          migration-initial                restore, then create each context's initial migration,
                                           InitialCreate, where its Migrations folder holds no model
                                           snapshot; a context that has one is left alone. bootstrap
                                           runs it, and migration-add, database-update,
                                           migration-bundle and verify refuse until it has run

        Options:
          --no-integration                 test: run only the projects that set IsTestProject and
                                           reference no Testcontainers, so no Docker daemon is needed
          --configuration <Debug|Release>  build, test, coverage, migration-bundle, verify, and end-to-end
                                           where the project holds the API; after a -- separator,
                                           because dotnet run claims the name
          -m:<N>, -maxcpucount:<N>         restore, build, test, coverage, verify: at most N MSBuild nodes,
                                           and at most N test projects running at once; test and
                                           coverage then build first and test with --no-build
          --access, --deletion, --operations, --model, --concurrency
                                           new-feature: forwarded to Tools/NewFeature.cs, whose
                                           --help says what each one means
          --kind, --input                  new-use-case: forwarded to Tools/NewUseCase.cs, whose
                                           --help says what each one means
          --apply                          remove-feature: delete an untouched generated skeleton
                                           instead of reporting what removing it involves

        Examples:
          dotnet run Tools/Tasks.cs test
          dotnet run Tools/Tasks.cs test --no-integration
          dotnet run Tools/Tasks.cs new-feature Widgets Widget --access shared
          dotnet run Tools/Tasks.cs new-use-case TodoLists ArchiveTodoList --kind command
          dotnet run Tools/Tasks.cs remove-feature Widgets
          dotnet run Tools/Tasks.cs remove-feature Widgets --apply
          dotnet run Tools/Tasks.cs migration-add AddTodoItemPriority
          dotnet run Tools/Tasks.cs -- build --configuration Release
          dotnet run Tools/Tasks.cs verify -m:4
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
        "new-use-case",
        "remove-feature",
        "migration-initial",
        "migration-add",
        "database-update",
        "migration-bundle",
        //#if (api)
        "openapi",
        "api-client",
        "run",
        //#endif
        "compose-up",
        "compose-down",
        //#if (api)
        "end-to-end",
        //#endif
        //#if (sonar)
        "sonar",
        //#endif
        "bootstrap",
        "presentation-scaffold",
        "presentation-join",
        "web-scaffold",
        "web-join",
        "hygiene",
        "verify",
        //#if (templateMaintenance)
        "web-lockfiles",
        //#endif
    ];

    //#if (api)
    private const string ApiUsage =
        """

        HTTP API:
          openapi                          write the OpenAPI reference the API describes itself with,
                                           which the clients are written from; bootstrap runs it
          run                              run the API with dotnet run
          end-to-end                       from the root, build and start the Compose stack from
                                           .env.example under a project name of its own, so the
                                           development stack keeps its data; wait until every
                                           service is healthy, run
                                           Tests/EndToEnd against it, print the stack's logs on a
                                           failure, then remove the stack and its volumes. A variable
                                           set in the environment overrides .env.example, for the
                                           stack and the suite alike
          --keep-stack                     end-to-end: leave the stack it started running, data included,
                                           for another run or a look at its logs

        Examples:
          dotnet run Tools/Tasks.cs openapi
          dotnet run Tools/Tasks.cs end-to-end --keep-stack
        """;

    //#endif
    private const string WebUsage =
        """

        Presentation projects:
          presentation-scaffold [<Name>]   create each project under Src/Presentation that holds a
                                           scaffold.json, or only the one named <Name>: run its dotnet new
                                           template from the package and version scaffold.json pins,
                                           in a template hive of its own, keep the folders it names
                                           beside the project's own files, refuse a generated project
                                           file that declares what the project's own lacks, add the
                                           project, its <Name>.Core sibling when it has one, and
                                           their tests to the solution and format them; a
                                           scaffold.json of kind written runs no template, the project
                                           being whole already; bootstrap runs it first, and a project
                                           created already is left alone
          presentation-join <Name>         make the edits the README of Src/Presentation/<Name> lists
                                           under 'Joining a project generated without one', in the
                                           files outside its folders that cleanarch-presentation
                                           cannot edit: each block placed by lines found exactly
                                           once, every edit or none; edits all in place already are
                                           left alone

        Web apps:
          web-scaffold [<App>]             create each app under Src/Web/apps that holds a scaffold.json
                                           and no package.json, or only <App>: run the framework's
                                           official CLI at the version scaffold.json pins, lay the
                                           app's overlay/ over the starter, pin every dependency to the
                                           version installed, then pnpm install and pnpm api;
                                           bootstrap runs it, and an app already created is left alone
          web-join <App>                   make the edits the README of Src/Web/apps/<App> lists under
                                           'Joining a project generated without one', as
                                           presentation-join does: a Blazor WebAssembly app lists the
                                           lines of the .NET client it brings
        """;

    private const string ClientUsage =
        """

        .NET client, once the project holds Src/Clients/AppTemplate.Client:
          api-client                       regenerate its Generated folder with the Kiota the tool
                                           manifest pins, from the OpenAPI reference openapi writes: a
                                           BOM on each C# file, and kiota-lock.json naming the
                                           reference by a relative path; bootstrap runs it
          api-client --check               fail when Generated differs from what the command writes;
                                           verify runs it
        """;

    private static readonly string[] CheckedTasks =
    [
        //#if (api)
        "api-client",
        //#endif
        //#if (templateMaintenance)
        "web-lockfiles",
        //#endif
    ];
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
            //#if (api)
            Console.WriteLine(ApiUsage);
            //#endif
            Console.WriteLine(WebUsage);
            //#if (api)
            Console.WriteLine(ClientUsage);
            //#endif
            //#if (templateMaintenance)
            Console.WriteLine(MaintenanceUsage);
            //#endif
            return 0;
        }

        string? task = null;
        string? name = null;
        string? second = null;

        List<string> featureOptions = [];
        List<string> useCaseOptions = [];
        bool noIntegration = false;
        bool apply = false;
        bool keepStack = false;
        bool check = false;
        string configuration = "Debug";
        string? maxCpuCount = null;

        for (int i = 0; i < arguments.Length; i++)
        {
            string argument = arguments[i];

            if (argument is "--no-integration")
            {
                noIntegration = true;
            }
            else if (argument is "--check")
            {
                check = true;
            }
            else if (argument is "--apply")
            {
                apply = true;
            }
            else if (argument is "--keep-stack")
            {
                keepStack = true;
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
            else if (MaxCpuCountPrefixes.FirstOrDefault(prefix => argument.StartsWith(prefix, StringComparison.Ordinal)) is { } prefix)
            {
                maxCpuCount = argument[prefix.Length..];

                if (!int.TryParse(maxCpuCount, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1)
                {
                    throw new InvalidOperationException($"'{argument}' needs a whole number of at least 1 after the colon, as in -m:4.");
                }
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
            else if (UseCaseOptionNames.Any(option => argument == option || argument.StartsWith(option + "=", StringComparison.Ordinal)))
            {
                useCaseOptions.Add(argument);

                if (!argument.Contains('=', StringComparison.Ordinal))
                {
                    if (i + 1 >= arguments.Length)
                    {
                        throw new InvalidOperationException($"'{argument}' needs a value.");
                    }

                    useCaseOptions.Add(arguments[++i]);
                }
            }
            else if (argument.StartsWith('-'))
            {
                throw new InvalidOperationException(
                    $"'{argument}' is not an option of this file. The options are --no-integration, --apply, "
                    + "--keep-stack, -m:<N> and --configuration <Debug|Release>, the last after a -- separator, for "
                    + $"new-feature {string.Join(", ", FeatureOptionNames)}, and for new-use-case {string.Join(", ", UseCaseOptionNames)}.");
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

        if (useCaseOptions.Count > 0 && task != "new-use-case")
        {
            throw new InvalidOperationException(
                $"'{useCaseOptions[0]}' is an option of the new-use-case task, not of '{task ?? "(no task)"}'.");
        }

        if (apply && task != "remove-feature")
        {
            throw new InvalidOperationException($"'--apply' is an option of the remove-feature task, not of '{task ?? "(no task)"}'.");
        }

        if (maxCpuCount is not null && task is not ("restore" or "build" or "test" or "coverage" or "verify"))
        {
            throw new InvalidOperationException(
                $"'-m:{maxCpuCount}' is an option of the restore, build, test, coverage and verify tasks, not of '{task ?? "(no task)"}'.");
        }

        if (keepStack && task != "end-to-end")
        {
            throw new InvalidOperationException($"'--keep-stack' is an option of the end-to-end task, not of '{task ?? "(no task)"}'.");
        }

        if (check && (task is null || !CheckedTasks.Contains(task, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                $"'--check' is an option of these tasks only, {string.Join(", ", CheckedTasks)}; not of '{task ?? "(no task)"}'.");
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
        string[] contexts =
        [
            //#if (localAuth)
            auth,
            //#endif
            persistence,
        ];
        //#if (api)
        string api = Path.Combine(repoRoot, "Src", "Presentation", "AppTemplate.Api");
        //#endif
        string runsettings = Path.Combine(repoRoot, "coverage.runsettings");
        string[] nodes = maxCpuCount is null ? [] : [$"-maxcpucount:{maxCpuCount}"];
        string[] modules = maxCpuCount is null ? [] : ["--max-parallel-test-modules", maxCpuCount];

        switch (task)
        {
            case "restore":
                Step("dotnet", ["restore", solution, .. nodes]);
                break;

            case "build":
                Step("dotnet", ["build", solution, "--configuration", configuration, .. nodes]);
                break;

            case "test":
                if (noIntegration)
                {
                    var failed = new List<string>();

                    foreach (string project in TestProjects(repoRoot, dockerFreeOnly: true))
                    {
                        try
                        {
                            if (maxCpuCount is null)
                            {
                                Step("dotnet", "test", project, "--configuration", configuration);
                            }
                            else
                            {
                                Step("dotnet", ["build", project, "--configuration", configuration, .. nodes]);
                                Step("dotnet", "test", project, "--configuration", configuration, "--no-build");
                            }
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
                else if (maxCpuCount is null)
                {
                    Step("dotnet", "test", solution, "--configuration", configuration);
                }
                else
                {
                    Step("dotnet", ["build", solution, "--configuration", configuration, .. nodes]);
                    Step("dotnet", ["test", solution, "--configuration", configuration, "--no-build", .. modules]);
                }

                break;

            case "coverage":
                Coverage(repoRoot, solution, configuration, runsettings, nodes, modules);
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

            case "new-use-case":
                Step(
                    "dotnet",
                    [
                        "run", Path.Combine(repoRoot, "Tools", "NewUseCase.cs"),
                        RequiredUseCase(task, name),
                        RequiredUseCase(task, second),
                        "--root", repoRoot,
                        .. useCaseOptions,
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

            case "migration-initial":
                Step("dotnet", "restore", solution);
                InitialMigrations(contexts);
                break;

            case "migration-add":
                RequireInitialMigrations(task, persistence);
                Step(
                    "dotnet", "ef", "migrations", "add", RequiredName(task, name),
                    "--project", persistence,
                    "--startup-project", persistence);
                break;

            case "database-update":
                RequireInitialMigrations(task, persistence);
                Step(
                    "dotnet", "ef", "database", "update",
                    "--project", persistence,
                    "--startup-project", persistence);
                break;

            case "migration-bundle":
                RequireInitialMigrations(task, contexts);

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

            //#if (api)
            case "openapi":
                OpenApiReference(repoRoot, api);
                break;

            case "api-client":
                ApiClient(repoRoot, api, check);
                break;

            case "run":
                Step("dotnet", "run", "--project", api);
                break;

            //#endif
            case "compose-up":
                Step("docker", "compose", "up", "-d", "--wait");
                break;

            case "compose-down":
                Step("docker", "compose", "down");
                break;

            //#if (api)
            case "end-to-end":
                EndToEnd(repoRoot, configuration, keepStack);
                break;

            //#endif

            //#if (sonar)
            case "sonar":
                Sonar(repoRoot);
                break;

            //#endif
            case "bootstrap":
                PresentationScaffold(repoRoot, solution, only: null);
                Step("dotnet", "format", solution);
                Step("dotnet", "format", solution, "--verify-no-changes", "--no-restore");
                //#if (api)
                OpenApiReference(repoRoot, api);
                //#endif

                try
                {
                    Step("dotnet", "tool", "restore");
                }
                catch (InvalidOperationException error)
                {
                    throw new InvalidOperationException(
                        error.Message + " The formatting is done and stable. The local tools are not restored, so the initial migrations "
                        + "are not created and the tasks that run dotnet ef cannot work yet: migration-add, database-update, "
                        + "migration-bundle and verify. Fix the restore and run bootstrap again.");
                }

                InitialMigrations(contexts);
                //#if (api)

                if (HoldsTheClient(repoRoot))
                {
                    ApiClient(repoRoot, api, check: false);
                }
                //#endif

                if (File.Exists(Path.Combine(repoRoot, "Src", "Web", "package.json")))
                {
                    WebScaffold(repoRoot, solution, only: null);
                }

                Write(
                    /*#if (api)*/
                    "Formatting stable, OpenAPI reference written, tools restored and each context's initial migration in its Migrations folder. "
                    /*#else
                    "Formatting stable, tools restored and each context's initial migration in its Migrations folder. "
                    #endif*/
                    + "Commit this before anything else: the migrations are the project's own from now on.",
                    ConsoleColor.Green);
                break;

            case "hygiene":
                Gates(repoRoot);
                break;

            case "verify":
                RequireInitialMigrations(task, contexts);
                Gates(repoRoot);
                //#if (api)

                if (HoldsTheClient(repoRoot))
                {
                    ApiClient(repoRoot, api, check: true);
                }
                //#endif

                //#if (templateMaintenance)
                WebLockfiles(repoRoot, check: true);
                //#endif
                Step("dotnet", ["restore", solution, .. nodes]);
                Step("dotnet", "format", solution, "--verify-no-changes", "--no-restore");
                Step("dotnet", ["build", solution, "--configuration", configuration, "--no-restore", .. nodes]);
                Step("dotnet", ["test", solution, "--configuration", configuration, "--no-build", .. modules]);
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
                //#if (templateMaintenance)
                LauncherGate(repoRoot, configuration);
                //#endif
                break;

            case "presentation-scaffold":
                /*#if (api)*/
                if (PresentationScaffold(repoRoot, solution, name) && File.Exists(OpenApiReferencePath(repoRoot, api)))
                {
                    Step("dotnet", "tool", "restore");
                    ApiClient(repoRoot, api, check: false);
                }
                /*#else
                PresentationScaffold(repoRoot, solution, name);
                #endif*/

                break;

            case "presentation-join":
                Join(repoRoot, "Src/Presentation/" + RequiredPresentation(task, name), "cleanarch-presentation", "presentation-scaffold");
                break;

            case "web-scaffold":
                /*#if (api)*/
                if (WebScaffold(repoRoot, solution, name) && File.Exists(OpenApiReferencePath(repoRoot, api)))
                {
                    Step("dotnet", "tool", "restore");
                    ApiClient(repoRoot, api, check: false);
                }
                /*#else
                WebScaffold(repoRoot, solution, name);
                #endif*/

                break;

            case "web-join":
                Join(repoRoot, "Src/Web/apps/" + RequiredApp(task, name), "cleanarch-webapp", "web-scaffold");
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

    //#if (api)
    private static void OpenApiReference(string repoRoot, string api)
    {
        string apiName = Path.GetFileName(api);
        string reference = Path.Combine(repoRoot, "Tests", "Integration", apiName + ".IntegrationTests", "OpenApi");

        File.Delete(Path.Combine(api, "obj", apiName + ".OpenApiFiles.cache"));

        StepWith(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                //#if (externalAuth)
                ["Authentication__External__Authority"] = "https://identity-provider.invalid/",
                ["Authentication__External__Audience"] = "openapi-document",
                //#endif
            },
            "dotnet",
            "build", Path.Combine(api, apiName + ".csproj"),
            "-p:OpenApiGenerateDocumentsOnBuild=true",
            $"-p:OpenApiDocumentsDirectory={reference}",
            "-p:OpenApiGenerateDocumentsOptions=--file-name v1");
    }

    private static string OpenApiReferencePath(string repoRoot, string api) =>
        Path.Combine(repoRoot, "Tests", "Integration", Path.GetFileName(api) + ".IntegrationTests", "OpenApi", "v1.json");

    //#endif
    private static string ClientFolder(string repoRoot) => Path.Combine(repoRoot, "Src", "Clients", "AppTemplate.Client");

    private static bool HoldsTheClient(string repoRoot) => Directory.Exists(ClientFolder(repoRoot));

    //#if (api)
    private static void ApiClient(string repoRoot, string api, bool check)
    {
        string reference = OpenApiReferencePath(repoRoot, api);
        string generated = Path.Combine(ClientFolder(repoRoot), "Generated");
        string shown = Path.GetRelativePath(repoRoot, generated).Replace('\\', '/');
        string output = check ? Path.Combine(Path.GetTempPath(), "api-client-" + Guid.NewGuid().ToString("N")) : generated;

        if (!HoldsTheClient(repoRoot))
        {
            throw new InvalidOperationException(
                "Src/Clients/AppTemplate.Client does not exist, so there is no client to write: a presentation that calls the API through it brings it.");
        }

        if (!File.Exists(reference))
        {
            throw new InvalidOperationException(
                $"{Path.GetRelativePath(repoRoot, reference)} is missing: run `dotnet run Tools/Tasks.cs openapi` first.");
        }

        try
        {
            Step(
                "dotnet", "tool", "run", "kiota", "generate",
                "--language", "CSharp",
                "--openapi", reference,
                "--output", output,
                "--class-name", "ApiClient",
                "--namespace-name", "AppTemplate.Client.Generated",
                "--exclude-backward-compatible",
                "--clean-output",
                "--log-level", "Warning");

            File.Delete(Path.Combine(output, ".kiota.log"));

            foreach (string file in Directory.EnumerateFiles(output, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
                File.WriteAllText(file, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }

            string lockFile = Path.Combine(output, "kiota-lock.json");
            var settings = JsonNode.Parse(File.ReadAllText(lockFile))!;
            settings["descriptionLocation"] = Path.GetRelativePath(generated, reference).Replace('\\', '/');
            File.WriteAllText(
                lockFile,
                settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" }) + "\n");

            if (!check)
            {
                Write($"Wrote {shown} from the OpenAPI reference.", ConsoleColor.Green);
                return;
            }

            string[] drifted = [.. TreeDifferences(generated, output)];

            if (drifted.Length > 0)
            {
                throw new InvalidOperationException(
                    $"{shown} differs from what Kiota writes from the OpenAPI reference:" + Environment.NewLine
                    + string.Join(Environment.NewLine, drifted.Select(path => "  " + path)) + Environment.NewLine
                    + "Run `dotnet run Tools/Tasks.cs api-client` and commit what it writes.");
            }

            Write($"{shown} is what Kiota writes from the OpenAPI reference.", ConsoleColor.Green);
        }
        finally
        {
            if (check && Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }
        }
    }

    private static IEnumerable<string> TreeDifferences(string committed, string written)
    {
        string[] Files(string root) => Directory.Exists(root)
            ? [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))]
            : [];

        return Files(committed)
            .Union(Files(written), StringComparer.Ordinal)
            .Where(path =>
            {
                string left = Path.Combine(committed, path);
                string right = Path.Combine(written, path);

                return !File.Exists(left) || !File.Exists(right) || !File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
            })
            .Order(StringComparer.Ordinal);
    }

    private static void EndToEnd(string repoRoot, string configuration, bool keepStack)
    {
        string[] compose = ["compose", "--project-name", "app-template-end-to-end", "--env-file", ".env.example"];
        /*#if (externalAuth || files)*/
        Dictionary<string, string> environment = Unset(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            //#if (externalAuth)
            ["EXTERNAL_AUTH_AUTHORITY"] = "https://identity-provider.invalid/",
            ["EXTERNAL_AUTH_AUDIENCE"] = "end-to-end",
            //#endif
            //#if (externalAuth && webServerRendering)
            ["EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID"] = "end-to-end",
            //#endif
            //#if (files)
            ["JOBS_FILES_INSPECT_INTERVAL"] = "00:00:10",
            //#endif
        });
        /*#else
        Dictionary<string, string> environment = [];
        #endif*/
        bool passed = false;

        try
        {
            Launch(repoRoot, environment, "docker", [.. compose, "up", "--build", "--detach", "--wait", "--wait-timeout", "300"]);
            Step(
                "dotnet", "run",
                "--project", Path.Combine(repoRoot, "Tests", "EndToEnd", "AppTemplate.EndToEnd.Tests", "AppTemplate.EndToEnd.Tests.csproj"),
                "--configuration", configuration);
            passed = true;
        }
        finally
        {
            if (!passed)
            {
                StackLogs(repoRoot, environment, compose);
            }

            if (!keepStack)
            {
                Launch(repoRoot, environment, "docker", [.. compose, "down", "--volumes"]);
            }
        }
    }

    private static void StackLogs(string repoRoot, Dictionary<string, string> environment, string[] compose)
    {
        try
        {
            Launch(repoRoot, environment, "docker", [.. compose, "logs", "--tail", "100"]);
        }
        catch (InvalidOperationException error)
        {
            Write(error.Message, ConsoleColor.Red);
        }
    }

    //#endif
    private static void Gates(string repoRoot)
    {
        Step("dotnet", "run", Gate(repoRoot, "NewFeature.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "NewUseCase.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "RemoveFeature.cs"), "--self-test");
        OptionalGate(repoRoot, "CheckDocPaths.cs", "--self-test");
        OptionalGate(repoRoot, "CheckDocPaths.cs", repoRoot);
        OptionalGate(repoRoot, "CheckWorkflows.cs", "--self-test");
        OptionalGate(repoRoot, "CheckWorkflows.cs", repoRoot);
        Step("dotnet", "run", Gate(repoRoot, "CoverageGate.cs"), "--self-test");
        OptionalGate(repoRoot, "CheckComments.cs", "--self-test");
        OptionalGate(repoRoot, "CheckComments.cs", repoRoot);
    }

    //#if (templateMaintenance)
    private static void LauncherGate(string repoRoot, string configuration)
    {
        string launcher = Path.Combine(repoRoot, "Launcher", "CleanArch.Create.slnx");

        Step("dotnet", "restore", launcher);
        Step("dotnet", "format", launcher, "--verify-no-changes", "--no-restore");
        Step("dotnet", "build", launcher, "--configuration", configuration, "--no-restore");
        Step("dotnet", "test", "--solution", launcher, "--configuration", configuration, "--no-build");
    }

    //#endif
    private static void OptionalGate(string repoRoot, string fileName, params string[] arguments)
    {
        string path = Path.Combine(repoRoot, "Tools", fileName);

        if (File.Exists(path))
        {
            Step("dotnet", ["run", path, .. arguments]);
        }
    }

    private static void Coverage(string repoRoot, string solution, string configuration, string runsettings, string[] nodes, string[] modules)
    {
        string results = Path.Combine(repoRoot, "TestResults");
        if (Directory.Exists(results))
        {
            Directory.Delete(results, recursive: true);
        }

        string[] built = [];

        if (nodes.Length > 0)
        {
            Step("dotnet", ["build", solution, "--configuration", configuration, .. nodes]);
            built = ["--no-build"];
        }

        Step(
            "dotnet",
            [
                "test", solution,
                "--configuration", configuration,
                .. built,
                .. modules,
                "--results-directory", results,
                "--coverage",
                "--coverage-settings", runsettings,
                "--coverage-output-format", "cobertura",
            ]);

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

    private static bool WebScaffold(string repoRoot, string solution, string? only)
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

            if (pending.Contains(folder, StringComparer.Ordinal))
            {
                pending = [folder];
            }
            else if (File.Exists(Path.Combine(folder, "package.json")) || Directory.EnumerateFiles(folder, "*.csproj").Any())
            {
                Write($"Src/Web/apps/{only} holds no {ScaffoldDescriptor}: it is created already, and left alone.", ConsoleColor.Green);
                pending = [];
            }
            else
            {
                throw new InvalidOperationException(
                    $"Src/Web/apps/{only} holds no {ScaffoldDescriptor}, so there is nothing to create. "
                    + "dotnet new cleanarch-webapp writes one in the app's folder.");
            }
        }

        bool client = false;

        foreach (string folder in pending)
        {
            if (ReadJson(Path.Combine(folder, ScaffoldDescriptor))["kind"]?.GetValue<string>() is "dotnet")
            {
                client |= ScaffoldProject(repoRoot, solution, folder);
            }
            else
            {
                ScaffoldApp(web, folder);
            }
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

        return client;
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
                .Replace("{directory}", name, StringComparison.Ordinal)),
        ];

        try
        {
            Directory.CreateDirectory(scratch);

            try
            {
                Launch(scratch, new Dictionary<string, string>(StringComparer.Ordinal), Pnpm, arguments, closeInput: true);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException(
                    $"{error.Message} Creating Src/Web/apps/{app} runs {cli}, the framework's official CLI, which pnpm fetches "
                    + "from the npm registry, so this step needs Node.js, pnpm (`corepack enable` once per machine) and the network; "
                    + "the output above says which failed. The app's folder is unchanged: run "
                    + $"`dotnet run Tools/Tasks.cs web-scaffold {app}` again once the cause is fixed.");
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

    private static readonly string[] GeneratedPropertiesTheBuildSets = ["TargetFramework", "Nullable", "ImplicitUsings"];

    private static bool PresentationScaffold(string repoRoot, string solution, string? only)
    {
        string presentation = Path.Combine(repoRoot, "Src", "Presentation");
        string[] pending = [.. Directory.GetDirectories(presentation)
            .Where(folder => File.Exists(Path.Combine(folder, ScaffoldDescriptor)))
            .Order(StringComparer.Ordinal)];

        if (only is not null)
        {
            string folder = Path.Combine(presentation, only);

            if (pending.Contains(folder, StringComparer.Ordinal))
            {
                pending = [folder];
            }
            else if (File.Exists(Path.Combine(folder, only + ".csproj")))
            {
                Write($"Src/Presentation/{only} holds no {ScaffoldDescriptor}: it is created already, and left alone.", ConsoleColor.Green);
                pending = [];
            }
            else
            {
                throw new InvalidOperationException($"Src/Presentation/{only} holds no {ScaffoldDescriptor}, so there is nothing to create.");
            }
        }

        bool client = false;

        foreach (string folder in pending)
        {
            client |= ScaffoldProject(repoRoot, solution, folder);
        }

        return client;
    }

    private static bool ScaffoldProject(string repoRoot, string solution, string folder)
    {
        string project = Path.GetFileName(folder);
        string shown = Path.GetRelativePath(repoRoot, folder).Replace('\\', '/');
        string task = shown.StartsWith("Src/Web/", StringComparison.Ordinal) ? "web-scaffold" : "presentation-scaffold";
        JsonObject descriptor = ReadJson(Path.Combine(folder, ScaffoldDescriptor));
        string kind = descriptor["kind"]?.GetValue<string>() ?? "pnpm";

        if (kind is not ("dotnet" or "written"))
        {
            throw new InvalidOperationException(
                $"{shown}/{ScaffoldDescriptor} is of kind '{kind}', and a .NET project is created by dotnet new or written by the template.");
        }

        string name = Text(descriptor, "name", project);
        string package = Text(descriptor, "package", project) + "@" + Text(descriptor, "version", project);

        if (kind == "dotnet")
        {
            Generate(descriptor, folder, project, name, package, shown, task);
        }

        File.Delete(Path.Combine(folder, ScaffoldDescriptor));

        string[] sources =
        [
            Path.Combine(folder, name + ".csproj"),
            .. new[] { Path.Combine(Path.GetDirectoryName(folder)!, name + ".Core", name + ".Core.csproj") }.Where(File.Exists),
        ];
        bool client = HoldsTheClient(repoRoot) && sources.Any(source => File.ReadAllText(source).Contains(@"..\..\Clients\", StringComparison.Ordinal));
        string tests = task == "web-scaffold" ? "Web" : "Presentation";
        string[] projects =
        [
            .. sources,
            .. sources
                .Select(Path.GetFileNameWithoutExtension)
                .Select(source => Path.Combine(repoRoot, "Tests", tests, source + ".UnitTests", source + ".UnitTests.csproj"))
                .Where(File.Exists),
            .. (client ? ClientProjects(repoRoot) : []),
        ];

        Step("dotnet", ["sln", solution, "add", .. projects]);

        foreach (string added in projects)
        {
            Step("dotnet", "format", added);
        }

        string origin = kind == "dotnet" ? package : $"the template, in the shape {Text(descriptor, "generator", project)} gives it at {package}";
        Write($"Created {shown} from {origin}. Commit it: the generated files are the project's own from now on.", ConsoleColor.Green);

        return client;
    }

    private static void Generate(JsonObject descriptor, string folder, string project, string name, string package, string shown, string task)
    {
        string scratch = Path.Combine(Path.GetTempPath(), task + "-" + Guid.NewGuid().ToString("N")[..12]);
        string generated = Path.Combine(scratch, name);
        string template = Text(descriptor, "template", project);
        string[] hive = ["--debug:custom-hive", Path.Combine(scratch, "hive"), "--debug:disable-sdk-templates"];

        try
        {
            Directory.CreateDirectory(scratch);

            try
            {
                Launch(scratch, new Dictionary<string, string>(StringComparer.Ordinal), "dotnet", ["new", "install", package, .. hive], closeInput: true);

                string[] arguments =
                [
                    "new", template,
                    .. (descriptor["arguments"]?.AsArray() ?? []).Select(argument => (argument?.GetValue<string>() ?? string.Empty)
                        .Replace("{name}", name, StringComparison.Ordinal)
                        .Replace("{directory}", name, StringComparison.Ordinal)),
                    .. DeclinedScriptsWhereOffered(scratch, template, hive),
                    .. hive,
                ];

                Launch(scratch, new Dictionary<string, string>(StringComparer.Ordinal), "dotnet", arguments, closeInput: true);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException(
                    $"{error.Message} Creating {shown} installs {package}, the framework's own templates, "
                    + "from NuGet, so this step needs the network and that package on a NuGet source; the output above says what "
                    + $"failed. The project's folder is unchanged: run `dotnet run Tools/Tasks.cs {task} {project}` again once the "
                    + "cause is fixed.");
            }

            RefuseWhatTheProjectLacks(Path.Combine(generated, name + ".csproj"), Path.Combine(folder, name + ".csproj"));

            foreach (string kept in (descriptor["keep"]?.AsArray() ?? []).Select(path => path?.GetValue<string>() ?? string.Empty))
            {
                string source = Path.Combine(generated, kept);
                string target = Path.Combine(folder, kept);

                if (File.Exists(source))
                {
                    if (!File.Exists(target))
                    {
                        File.Copy(source, target);
                    }
                }
                else
                {
                    CopyFiles(source, target, overwrite: false);
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
    }

    private static string[] ClientProjects(string repoRoot) =>
        [.. new[]
        {
            Path.Combine(ClientFolder(repoRoot), "AppTemplate.Client.csproj"),
            Path.Combine(repoRoot, "Tests", "Clients", "AppTemplate.Client.UnitTests", "AppTemplate.Client.UnitTests.csproj"),
        }.Where(File.Exists)];

    private const string JoiningSection = "## Joining a project generated without one";

    private static readonly Regex JoiningEdit = new(
        @"^- in `(?<path>[^`]+)`, (?<mode>after|before|replace|remove) [^:]*:$", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static void Join(string repoRoot, string folder, string itemTemplate, string scaffoldTask)
    {
        string relative = $"{folder}/README.md";
        string readme = Path.Combine(repoRoot, relative);

        if (!File.Exists(readme))
        {
            throw new InvalidOperationException($"{relative} does not exist: name the folder {itemTemplate} wrote under {Path.GetDirectoryName(folder)!.Replace('\\', '/')}.");
        }

        List<ReadmeEdit> edits = ReadmeEdits(File.ReadAllLines(readme), relative);
        var files = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (string path in edits.Select(edit => edit.Path).Distinct(StringComparer.Ordinal))
        {
            string full = Path.Combine(repoRoot, path);

            if (!File.Exists(full))
            {
                throw new InvalidOperationException($"{relative} edits {path}, which does not exist, so no file was changed.");
            }

            files[path] = [.. File.ReadAllText(full).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
        }

        ReadmeEdit[] inPlace = [.. edits.Where(edit => InPlace(edit, files[edit.Path]))];

        if (inPlace.Length == edits.Count)
        {
            Write($"Every edit {relative} lists is in place already, so no file was changed.", ConsoleColor.Green);
            return;
        }

        if (inPlace.Length > 0)
        {
            throw new InvalidOperationException(
                $"Some edits {relative} lists are in place already and the others are not, so no file was changed: "
                + string.Join("; ", inPlace.Select(edit => edit.ToString()))
                + ". Make the others by hand, or restore those files and run the task again.");
        }

        foreach (ReadmeEdit edit in edits)
        {
            Apply(edit, files[edit.Path]);
        }

        foreach ((string path, List<string> lines) in files)
        {
            string full = Path.Combine(repoRoot, path);
            bool bom = File.ReadAllBytes(full) is [0xEF, 0xBB, 0xBF, ..];
            File.WriteAllText(full, string.Join('\n', lines), new UTF8Encoding(bom));
        }

        foreach (ReadmeEdit edit in edits)
        {
            Console.WriteLine($"Applied: {edit}");
        }

        Write(
            $"Joined {folder} to the project. Run `dotnet run Tools/Tasks.cs {scaffoldTask} {Path.GetFileName(folder)}` "
            + "next: it creates the pages and adds the project to the solution.",
            ConsoleColor.Green);
    }

    private static List<ReadmeEdit> ReadmeEdits(string[] readme, string relative)
    {
        int start = Array.IndexOf(readme, JoiningSection);

        if (start < 0)
        {
            throw new InvalidOperationException($"{relative} has no '{JoiningSection}' section, so there is nothing to join.");
        }

        var edits = new List<ReadmeEdit>();
        int index = start + 1;

        while (index < readme.Length && !readme[index].StartsWith("## ", StringComparison.Ordinal))
        {
            if (!readme[index].StartsWith("- in ", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            string header = readme[index].Trim();
            while (!header.EndsWith(':') && ++index < readme.Length)
            {
                header += " " + readme[index].Trim();
            }

            Match bullet = JoiningEdit.Match(header);
            if (!bullet.Success)
            {
                throw new InvalidOperationException($"{relative}: '{header}' names no file and no 'after', 'before', 'replace' or 'remove'.");
            }

            string mode = bullet.Groups["mode"].Value;
            List<string> anchor = Fence(readme, ref index, relative);
            List<string> lines = mode == "remove" ? [] : Fence(readme, ref index, relative);
            edits.Add(new ReadmeEdit(bullet.Groups["path"].Value, mode, anchor, lines));
        }

        if (edits.Count == 0)
        {
            throw new InvalidOperationException($"{relative}: '{JoiningSection}' lists no edit in the form this task reads.");
        }

        return edits;
    }

    private static List<string> Fence(string[] readme, ref int index, string relative)
    {
        while (++index < readme.Length && !readme[index].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            if (readme[index].StartsWith("- ", StringComparison.Ordinal) || readme[index].StartsWith("## ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{relative}: an edit ends at '{readme[index]}' before its lines.");
            }
        }

        if (index >= readme.Length)
        {
            throw new InvalidOperationException($"{relative} ends inside an edit.");
        }

        int indent = readme[index].Length - readme[index].TrimStart().Length;
        var lines = new List<string>();

        while (++index < readme.Length && readme[index].Trim() != "```")
        {
            string line = readme[index];
            lines.Add(line.Length >= indent && line[..indent].Trim().Length == 0 ? line[indent..] : line.TrimStart());
        }

        return lines;
    }

    private static int[] Occurrences(List<string> lines, List<string> block) =>
        [.. Enumerable.Range(0, Math.Max(0, lines.Count - block.Count + 1)).Where(at => Holds(lines, block, at))];

    private static bool Holds(List<string> lines, List<string> block, int at) =>
        at >= 0 && at + block.Count <= lines.Count && block.Select((line, offset) => lines[at + offset].TrimEnd() == line.TrimEnd()).All(same => same);

    private static bool InPlace(ReadmeEdit edit, List<string> lines)
    {
        int[] anchors = Occurrences(lines, edit.Anchor);

        return edit.Mode switch
        {
            "after" => anchors.Any(at => Holds(lines, edit.Lines, at + edit.Anchor.Count)),
            "before" => anchors.Any(at => Holds(lines, edit.Lines, at - edit.Lines.Count)),
            "replace" => Occurrences(lines, edit.Lines) is { Length: > 0 } placed
                && anchors.All(at => placed.Any(start => at >= start && at + edit.Anchor.Count <= start + edit.Lines.Count)),
            _ => anchors.Length == 0,
        };
    }

    private static void Apply(ReadmeEdit edit, List<string> lines)
    {
        int[] found = Occurrences(lines, edit.Anchor);

        if (found.Length != 1)
        {
            throw new InvalidOperationException(
                $"{edit.Path}: the lines to place the edit by occur {found.Length} times, not once, so no file was changed:\n{string.Join('\n', edit.Anchor)}");
        }

        int at = found[0];

        switch (edit.Mode)
        {
            case "after":
                lines.InsertRange(at + edit.Anchor.Count, edit.Lines);
                break;
            case "before":
                lines.InsertRange(at, edit.Lines);
                break;
            default:
                lines.RemoveRange(at, edit.Anchor.Count);
                lines.InsertRange(at, edit.Lines);
                break;
        }
    }

    private sealed record ReadmeEdit(string Path, string Mode, List<string> Anchor, List<string> Lines)
    {
        public override string ToString() => $"{Mode} {Anchor.Count} line(s) of {Path}";
    }

    private static string[] DeclinedScriptsWhereOffered(string directory, string template, string[] hive)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            UseShellExecute = false,
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };

        foreach (string argument in (string[])["new", template, "--help", .. hive])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("'dotnet' started no process.");

        process.StandardInput.Close();
        string help = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return help.Contains("--allow-scripts", StringComparison.Ordinal) ? ["--allow-scripts", "no"] : [];
    }

    private static void RefuseWhatTheProjectLacks(string generated, string own)
    {
        if (!File.Exists(generated))
        {
            throw new InvalidOperationException($"dotnet new wrote no {Path.GetFileName(generated)}, so {Path.GetDirectoryName(own)} is left unchanged.");
        }

        XDocument template = XDocument.Load(generated);
        XDocument project = XDocument.Load(own);

        string[] lacking =
        [
            .. template.Descendants("PackageReference")
                .Select(reference => (string?)reference.Attribute("Include"))
                .OfType<string>()
                .Where(package => !project.Descendants("PackageReference").Any(reference => (string?)reference.Attribute("Include") == package))
                .Select(package => $"the package {package}"),
            .. template.Descendants("PropertyGroup").Elements()
                .Select(property => property.Name.LocalName)
                .Where(property => !GeneratedPropertiesTheBuildSets.Contains(property, StringComparer.Ordinal))
                .Where(property => !project.Descendants(property).Any())
                .Select(property => $"the property {property}"),
        ];

        if (lacking.Length > 0)
        {
            throw new InvalidOperationException(
                $"The project file dotnet new wrote declares {string.Join(", ", lacking)}, which {Path.GetFileName(own)} does not, "
                + "so the files it generated may need it: add each to the project file, a package's version to Directory.Packages.props, "
                + "or change the template's arguments in scaffold.json, then run the task again.");
        }
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

    private const string InitialMigrationName = "InitialCreate";

    private static readonly string[] MaxCpuCountPrefixes = ["-m:", "-maxcpucount:"];

    private static readonly string[] FeatureOptionNames = ["--access", "--deletion", "--operations", "--model", "--concurrency"];

    private static readonly string[] UseCaseOptionNames = ["--kind", "--input"];

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

    private static string RequiredUseCase(string task, string? second)
    {
        if (string.IsNullOrWhiteSpace(second))
        {
            throw new InvalidOperationException(
                $"The '{task}' task needs the feature and the use case, and --kind, e.g. dotnet run Tools/Tasks.cs {task} TodoLists ArchiveTodoList --kind command");
        }

        return second;
    }

    private static string RequiredPresentation(string task, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                $"The '{task}' task needs the presentation project's folder name, e.g. dotnet run Tools/Tasks.cs {task} AppTemplate.Portal");
        }

        return name;
    }

    private static string RequiredApp(string task, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException(
                $"The '{task}' task needs the app's folder name under Src/Web/apps, e.g. dotnet run Tools/Tasks.cs {task} BackOffice");
        }

        return name;
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

    private static void InitialMigrations(string[] contexts)
    {
        foreach (string context in contexts)
        {
            if (HasMigrations(context))
            {
                Write($"{Path.GetFileName(context)} already has its migrations; they are left as they are.", ConsoleColor.DarkGray);
                continue;
            }

            Step(
                "dotnet", "ef", "migrations", "add", InitialMigrationName,
                "--project", context,
                "--startup-project", context);
        }
    }

    private static void RequireInitialMigrations(string task, params string[] contexts)
    {
        string[] missing = [.. contexts.Where(context => !HasMigrations(context)).Select(Path.GetFileName).OfType<string>()];

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"'{task}' needs the initial migration of {string.Join(" and ", missing)}, and there is none yet. "
                + "Run `dotnet run Tools/Tasks.cs bootstrap`, which creates it with migration-initial, then commit it (docs/guides/migrations.md).");
        }
    }

    private static bool HasMigrations(string context)
    {
        string folder = Path.Combine(context, "Migrations");

        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*ModelSnapshot.cs").Any();
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
            projects = projects.Where(path => File.ReadAllText(path) is var project
                && project.Contains("<IsTestProject>true</IsTestProject>", StringComparison.Ordinal)
                && !project.Contains("Testcontainers", StringComparison.OrdinalIgnoreCase));
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

    private static void StepIn(string? directory, string executable, params string[] arguments) =>
        Launch(directory, new Dictionary<string, string>(StringComparer.Ordinal), executable, arguments);

    private static void StepWith(IReadOnlyDictionary<string, string> defaults, string executable, params string[] arguments) =>
        Launch(null, Unset(defaults), executable, arguments);

    private static Dictionary<string, string> Unset(IReadOnlyDictionary<string, string> defaults) =>
        defaults
            .Where(variable => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable.Key)))
            .ToDictionary(variable => variable.Key, variable => variable.Value, StringComparer.Ordinal);

    private static void Launch(string? directory, Dictionary<string, string> environment, string executable, string[] arguments, bool closeInput = false)
    {
        string line = string.Concat(environment.Select(variable => $"{variable.Key}={variable.Value} "))
            + executable + " " + string.Join(' ', arguments);
        Write("> " + line + (directory is null ? string.Empty : $"   (in {directory})"), ConsoleColor.Cyan);
        Console.Out.Flush();

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = directory ?? string.Empty,
            RedirectStandardInput = closeInput,
        };
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

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
            if (closeInput)
            {
                process.StandardInput.Close();
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"'{line}' exited with {process.ExitCode}.");
            }
        }
    }
}
