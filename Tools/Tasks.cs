#:property UseAppHost=false
#:property PublishAot=false

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
    private const string _usage =
        """
        Usage: dotnet run Tools/Tasks.cs <task> [<name> [<second>]] [options]
               dotnet run Tools/Tasks.cs help [<task>]
               dotnet run Tools/Tasks.cs -- <task> --help

        dotnet run claims --help before the script sees it: after the file name, write help <task>,
        or put -- before the task, as in -- build --help; either prints only that task's lines.

        Thin wrappers over the dotnet and docker commands of this repository. Each task prints the
        command it runs before running it, so the printed line gives the same result without this
        file. The repository root is the nearest directory at or above this file holding
        cleanarch.json, the project file that records the options the project was generated with
        and the parts added since; a task reads those options there. The compose tasks and the
        vulnerability listing read the working directory. Off Windows, test, check, coverage and
        verify leave out each test project whose TargetFramework names Windows, as sonar does in
        its Linux container, and name it before the run and after it: it still builds, and runs on
        Windows alone.

        Tasks:
        <tasks>

        Checks:
          check                            while you work, in under a minute on a change of a file or two:
                                           over what git diff touches, which is the working tree, the
                                           files not yet tracked, and the commits of this branch since
                                           the merge-base with origin/develop, or with the first of
                                           origin/HEAD, origin/main, origin/master, develop, main and
                                           master that exists; nothing touched is reported, and passes.
                                           It checks the format of the C# files touched, as format
                                           does; builds the projects touched and every project that
                                           references them, or the solution when a file that sets the
                                           build for all of them is touched (Directory.* props and
                                           targets, .editorconfig, global.json, nuget.config, a project
                                           or solution file); then runs the architecture tests and the
                                           unit tests of those projects, never the projects that need
                                           Docker, nor off Windows the ones whose TargetFramework
                                           names Windows, which it names. It leaves out verify's gates over the whole tree,
                                           the integration tests, coverage, the migration drift check,
                                           the vulnerability listing, the generated clients and the
                                           web workspace's install, lint, test and build
          format-fix                       dotnet format over the solution, then a pass naming each file
                                           and line whose name breaks a naming rule (IDE1006), which
                                           dotnet format cannot rename; the task fails while one is left

        Documentation:
          docs-index                       write an index.md into each folder under docs/ that holds a
                                           page, but plans and the folders whose name starts with _:
                                           the folder's pages, its README first and the others by
                                           title, then its subfolders, each with the title and the
                                           description of its frontmatter, and a decision with its
                                           status; remove an index whose folder holds no page.
                                           bootstrap, new-feature, remove-feature --apply,
                                           presentation-join and web-join run it
          docs-index --check               fail when an index differs from what the command writes,
                                           or is missing; hygiene and verify run it

        Migrations:
          migration-add <Name>           add a migration to the business context, or to the identity
                                           context with --context auth; refused, with nothing written,
                                           when the model has not changed since the last migration
          database-update                  apply the pending migrations of the business context, or of
                                           the identity context with --context auth; needs the database
          migration-check                  fail when the business context's model, or the identity
                                           context's with --context auth, holds a change no migration
                                           holds yet; new-feature and new-field run it before they
                                           write
          migration-initial                restore, then create each context's initial migration,
                                           InitialCreate, where its Migrations folder holds no model
                                           snapshot; a context that has one is left alone. bootstrap
                                           runs it, and migration-add, database-update,
                                           migration-bundle and verify refuse until it has run

        Options:
          --no-build                       migration-add, and openapi where the project holds the API:
                                           work from what the last build wrote instead of building
                                           again; after a -- separator, because dotnet run claims the
                                           name. new-feature and new-field pass it to the steps after
                                           their build
          --context <auth>                 migration-add, migration-check, database-update: the
                                           context to target, the business context when left out;
                                           auth is the identity context, which only a project
                                           generated with --auth local holds
          --no-integration                 test: run only the projects that set IsTestProject and
                                           reference neither Testcontainers nor Aspire.Hosting.Testing,
                                           so no Docker daemon is needed; off Windows, not the ones
                                           whose TargetFramework names Windows either, which it names
          --configuration <Debug|Release>  build, test, check, coverage, migration-bundle, verify, and end-to-end
                                           where the project holds the API; after a -- separator,
                                           because dotnet run claims the name
          -m:<N>, -maxcpucount:<N>         restore, build, test, check, coverage, verify: at most N MSBuild nodes,
                                           and at most N test projects running at once; test and
                                           coverage then build first and test with --no-build
          --access, --deletion, --operations, --model, --concurrency, --fields
                                           new-feature: forwarded to Tools/NewFeature.cs, whose
                                           --help says what each one means
          --kind, --input                  new-use-case: forwarded to Tools/NewUseCase.cs, whose
                                           --help says what each one means
          --dry-run                        new-feature, new-field, new-use-case, new-setting, new-event:
                                           print the files and the edits the command makes, and write
                                           and run nothing
          --keys                           new-setting: forwarded to Tools/NewSetting.cs, whose
                                           --help says what each one means
          --fields, --consumer, --unconsumed
                                           new-event: forwarded to Tools/NewEvent.cs, whose
                                           --help says what each one means
          --apply                          remove-feature: remove the feature its manifest records,
                                           edited or not, instead of reporting what removing it
                                           involves: delete its files, undo its recorded edits, then
                                           build, add Remove<Features> and rewrite the references;
                                           refused while what it changes is not committed
          --web, --web-audience, --web-rendering, --web-port
                                           web-join: the --framework, --audience, --rendering and
                                           --port cleanarch-webapp was given, which cleanarch.json
                                           records for the app as --web would; public and spa unless
                                           given, and the port the app's compose.yml holds, which
                                           --web-port must equal
          --to, --source, --json, --keep   upgrade-report: the target version, the latest stable one
                                           when left out; a .nupkg of the package or a folder of
                                           them, read in place of the NuGet sources, once per
                                           source; a file outside the project to write the report
                                           to as JSON as well; a folder outside the project to keep
                                           the two generations in

        Upgrades:
          upgrade-report                   say what a newer version of the package cleanarch.json
                                           names changes in this project, writing nothing in it:
                                           generate the project from the version cleanarch.json
                                           records and from the target, each in a template hive of
                                           its own, add the parts recorded since through their item
                                           templates, then list each file to take as the target
                                           writes it, to merge by hand with the diff between the two
                                           versions, added or removed, apart from what bootstrap
                                           writes, with the options to decide by hand and the
                                           changelog entries in between; no step of a package runs,
                                           and Tools/UpgradeReport.cs --help says more

        Examples:
          dotnet run Tools/Tasks.cs test
          dotnet run Tools/Tasks.cs test --no-integration
          dotnet run Tools/Tasks.cs check
          dotnet run Tools/Tasks.cs new-feature Widgets Widget --access shared
          dotnet run Tools/Tasks.cs new-field Widgets "Budget:decimal(12,2)>=0=0, Notes:text?"
          dotnet run Tools/Tasks.cs new-use-case TodoLists ArchiveTodoList --kind command
          dotnet run Tools/Tasks.cs new-setting TodoLists TodoListLimits --keys "MaxItems:int(1..500)=100"
          dotnet run Tools/Tasks.cs new-event TodoLists TodoListArchivedDomainEvent --unconsumed "Nothing reacts to an archived list"
          dotnet run Tools/Tasks.cs remove-feature Widgets
          dotnet run Tools/Tasks.cs remove-feature Widgets --apply
          dotnet run Tools/Tasks.cs migration-add AddTodoItemPriority
          dotnet run Tools/Tasks.cs migration-add AddAccessKeys --context auth
          dotnet run Tools/Tasks.cs -- build --configuration Release
          dotnet run Tools/Tasks.cs verify -m:4
          dotnet run Tools/Tasks.cs upgrade-report
          dotnet run Tools/Tasks.cs upgrade-report --to 2.1.0 --source ../packages
        """;

    private static readonly string[] _knownTasks =
    [
        "restore",
        "build",
        "test",
        "check",
        "coverage",
        "format",
        "format-fix",
        "new-feature",
        "new-field",
        "new-use-case",
        "new-setting",
        "new-event",
        "remove-feature",
        "migration-initial",
        "migration-add",
        "migration-check",
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
        "sonar",
        "bootstrap",
        "presentation-scaffold",
        "presentation-join",
        "web-scaffold",
        "web-join",
        "docs-index",
        "hygiene",
        "verify",
        "upgrade-report",
        //#if (templateMaintenance)
        "web-lockfiles",
        //#endif
    ];

    //#if (api)
    private const string _apiUsage =
        """

        HTTP API:
          openapi                          write the OpenAPI reference the API describes itself with,
                                           which the clients are written from; bootstrap runs it
          run                              run the API with dotnet run
          end-to-end                       from the root, build and start the Compose stack from
                                           .env.example under a project name of its own, so the
                                           development stack keeps its data; wait until every
                                           service is healthy, run
                                           Tests/EndToEnd against it, then, with a web app, drive each
                                           one in Chromium (packages/browser-smoke in Src/Web), print
                                           the stack's logs on a failure, then remove the stack and
                                           its volumes. A variable
                                           set in the environment overrides .env.example, for the
                                           stack and the suite alike
          --keep-stack                     end-to-end: leave the stack it started running, data included,
                                           for another run or a look at its logs

        Examples:
          dotnet run Tools/Tasks.cs openapi
          dotnet run Tools/Tasks.cs end-to-end --keep-stack
        """;

    //#endif
    private const string _webUsage =
        """

        Presentation projects:
          presentation-scaffold [<Name>]   create each project under Src/Presentation that holds a
                                           scaffold.json, or only the one named <Name>: run its dotnet new
                                           template from the package and version scaffold.json pins,
                                           in a template hive of its own, keep the folders it names
                                           beside the project's own files, refuse a generated project
                                           file that declares what the project's own lacks, add the
                                           project and its tests to the solution, in the solution
                                           folders of their layer, and format them; a
                                           scaffold.json of kind written runs no template, the project
                                           being whole already; bootstrap runs it first, and a project
                                           created already is left alone
          presentation-join <Name>         make the edits the README of the project <Name> lists
                                           under 'Joining a project generated without one', in the
                                           files outside its folders that cleanarch-presentation
                                           cannot edit: each block placed by lines found exactly
                                           once; an edit in place already is left alone, and the
                                           others are made all or none, every line they are placed
                                           by found before a file is written; then rewrite the
                                           documentation index and record the project in
                                           cleanarch.json, with the framework its scaffold.json
                                           names. For <Project>.Worker it also moves
                                           the API's Common/Jobs and Features/*/Jobs, and their unit
                                           tests, into the worker, renames their namespaces from
                                           <Project>.Api. to <Project>.Worker. wherever the project
                                           names them; ADR 0011 is superseded by 0025, as the
                                           README's edits say; it refuses while git
                                           reports a change to a file it would touch, and builds the
                                           solution and the worker's tests in a copy first, writing
                                           nothing when they do not build

        Web apps:
          web-scaffold [<App>]             create each app under Src/Web/apps that holds a scaffold.json
                                           and no package.json, or only <App>: run the framework's
                                           official CLI at the version scaffold.json pins, lay the
                                           app's overlay/ over the starter, pin every dependency to the
                                           version installed, then pnpm install and pnpm api;
                                           bootstrap runs it, and an app already created is left alone
          web-join <App> --web <F>         make the edits the README of Src/Web/apps/<App> lists under
                                           'Joining a project generated without one', as
                                           presentation-join does: its include: line in
                                           docker-compose.yml, its port in .env.example, and for a
                                           Blazor WebAssembly app the lines of the .NET client it
                                           brings; for the first app, while cleanarch.json records no
                                           workspace, those Src/Web/README.md lists before them; then
                                           rewrite the documentation index and record the app in
                                           cleanarch.json, with the client when
                                           Src/Clients holds it
          web-join <package>               record Src/Web/packages/<package> in cleanarch.json, which
                                           cleanarch-webpackage cannot edit
        """;

    private const string _clientUsage =
        """

        .NET client, once the project holds Src/Clients/AT.Client:
          api-client                       regenerate its Generated folder with the Kiota the tool
                                           manifest pins, from the OpenAPI reference openapi writes: a
                                           BOM on each C# file, and kiota-lock.json naming the
                                           reference by a relative path; bootstrap runs it
          api-client --check               fail when Generated differs from what the command writes;
                                           verify runs it
        """;

    private static readonly string[] _checkedTasks =
    [
        "docs-index",
        //#if (api)
        "api-client",
        //#endif
        //#if (templateMaintenance)
        "web-lockfiles",
        //#endif
    ];
    //#if (templateMaintenance)

    private const string _maintenanceUsage =
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
        if (arguments is ["--self-test"])
        {
            return SelfTest();
        }

        ProjectFile projectFile = ProjectFile.Read(ResolveRepoRoot());

        if (arguments is ["help", var helped] && !helped.StartsWith('-'))
        {
            Console.WriteLine(TaskHelp(helped, projectFile));
            return 0;
        }

        if (arguments is [var asked, ..] && !asked.StartsWith('-') && asked != "help" && arguments.Skip(1).Any(argument => argument is "--help" or "-h"))
        {
            Console.WriteLine(TaskHelp(asked, projectFile));
            return 0;
        }

        if (arguments is ["--help"] or ["-h"] or ["help"] or ["help", "--help" or "-h"])
        {
            Console.WriteLine(_usage.Replace("<tasks>", TaskList(projectFile), StringComparison.Ordinal));
            //#if (api)
            Console.WriteLine(_apiUsage);
            //#endif
            Console.WriteLine(_webUsage);
            //#if (api)
            Console.WriteLine(_clientUsage);
            //#endif
            //#if (templateMaintenance)
            Console.WriteLine(_maintenanceUsage);
            //#endif
            return 0;
        }

        string? task = null;
        string? name = null;
        string? second = null;

        List<string> generatorArguments = [];
        List<string> generatorOptions = [];
        List<string> upgradeOptions = [];
        var webOptions = new Dictionary<string, string>(StringComparer.Ordinal);
        bool noIntegration = false;
        bool apply = false;
        bool keepStack = false;
        bool noBuild = false;
        bool check = false;
        string configuration = "Debug";
        string? maxCpuCount = null;
        string? context = null;

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
            else if (argument is "--no-build")
            {
                noBuild = true;
            }
            else if (argument is "--context")
            {
                if (i + 1 >= arguments.Length)
                {
                    throw new InvalidOperationException("'--context' needs a value: auth, the identity context; leave it out for the business context.");
                }

                context = arguments[++i];
            }
            else if (argument.StartsWith("--context=", StringComparison.Ordinal))
            {
                context = argument["--context=".Length..];
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
            else if (_maxCpuCountPrefixes.FirstOrDefault(prefix => argument.StartsWith(prefix, StringComparison.Ordinal)) is { } prefix)
            {
                maxCpuCount = argument[prefix.Length..];

                if (!int.TryParse(maxCpuCount, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1)
                {
                    throw new InvalidOperationException($"'{argument}' needs a whole number of at least 1 after the colon, as in -m:4.");
                }
            }
            else if (_generatorOptions.Keys.FirstOrDefault(option => argument == option || argument.StartsWith(option + "=", StringComparison.Ordinal)) is { } generatorOption)
            {
                generatorOptions.Add(generatorOption);
                generatorArguments.Add(argument);

                if (argument == generatorOption && !_generatorSwitches.Contains(generatorOption, StringComparer.Ordinal))
                {
                    if (i + 1 >= arguments.Length)
                    {
                        throw new InvalidOperationException($"'{argument}' needs a value.");
                    }

                    generatorArguments.Add(arguments[++i]);
                }
            }
            else if (_webJoinOptionNames.FirstOrDefault(option => argument == option || argument.StartsWith(option + "=", StringComparison.Ordinal)) is { } webOption)
            {
                if (argument.Length > webOption.Length)
                {
                    webOptions[webOption] = argument[(webOption.Length + 1)..];
                }
                else if (i + 1 < arguments.Length)
                {
                    webOptions[webOption] = arguments[++i];
                }
                else
                {
                    throw new InvalidOperationException($"'{argument}' needs a value.");
                }
            }
            else if (_upgradeOptionNames.FirstOrDefault(option => argument == option || argument.StartsWith(option + "=", StringComparison.Ordinal)) is { } upgradeOption)
            {
                upgradeOptions.Add(upgradeOption);

                if (argument.Length > upgradeOption.Length)
                {
                    upgradeOptions.Add(argument[(upgradeOption.Length + 1)..]);
                }
                else if (i + 1 < arguments.Length)
                {
                    upgradeOptions.Add(arguments[++i]);
                }
                else
                {
                    throw new InvalidOperationException($"'{argument}' needs a value.");
                }
            }
            else if (argument.StartsWith('-'))
            {
                throw new InvalidOperationException(
                    $"'{argument}' is not an option of this file. The options are --no-integration, --apply, "
                    + "--keep-stack, --no-build, --context <auth>, -m:<N> and --configuration <Debug|Release>, the last after a -- separator, for "
                    + string.Concat(_generatorTasks.Select(generator => $"{generator} {string.Join(", ", GeneratorOptionsOf(generator))}, for "))
                    + $"web-join {string.Join(", ", _webJoinOptionNames)}, and for upgrade-report {string.Join(", ", _upgradeOptionNames)}.");
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

        if (GeneratorOptionRefusal(task, generatorOptions) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }

        if (webOptions.Count > 0 && task != "web-join")
        {
            throw new InvalidOperationException(
                $"'{webOptions.Keys.First()}' is an option of the web-join task, not of '{task ?? "(no task)"}'.");
        }

        if (upgradeOptions.Count > 0 && task != "upgrade-report")
        {
            throw new InvalidOperationException(
                $"'{upgradeOptions[0]}' is an option of the upgrade-report task, not of '{task ?? "(no task)"}'.");
        }

        if (apply && task != "remove-feature")
        {
            throw new InvalidOperationException($"'--apply' is an option of the remove-feature task, not of '{task ?? "(no task)"}'.");
        }

        if (maxCpuCount is not null && task is not ("restore" or "build" or "test" or "check" or "coverage" or "verify"))
        {
            throw new InvalidOperationException(
                $"'-m:{maxCpuCount}' is an option of the restore, build, test, check, coverage and verify tasks, not of '{task ?? "(no task)"}'.");
        }

        if (context is not null && task is not ("migration-add" or "migration-check" or "database-update"))
        {
            throw new InvalidOperationException(
                $"'--context' is an option of the migration-add, migration-check and database-update tasks, not of '{task ?? "(no task)"}'.");
        }

        if (keepStack && task != "end-to-end")
        {
            throw new InvalidOperationException($"'--keep-stack' is an option of the end-to-end task, not of '{task ?? "(no task)"}'.");
        }

        if (noBuild && task is not ("migration-add" or "openapi"))
        {
            throw new InvalidOperationException($"'--no-build' is an option of the migration-add and openapi tasks, not of '{task ?? "(no task)"}'.");
        }

        if (check && (task is null || !_checkedTasks.Contains(task, StringComparer.Ordinal)))
        {
            throw new InvalidOperationException(
                $"'--check' is an option of these tasks only, {string.Join(", ", _checkedTasks)}; not of '{task ?? "(no task)"}'.");
        }

        if (task is null)
        {
            throw new InvalidOperationException(
                "A task is required (help lists them with their options). It has to be one of these:" + Environment.NewLine + TaskList(projectFile));
        }

        if (!_knownTasks.Contains(task, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{task}' does not belong to the set of tasks. Supply one of these and then try the command again:"
                + Environment.NewLine + TaskList(projectFile));
        }

        string repoRoot = projectFile.Root;
        string solution = Path.Combine(repoRoot, projectFile.Name + ".sln");
        string persistence = projectFile.ProjectFolder("Src", "Infrastructure", "Infrastructure.Persistence");
        string auth = projectFile.ProjectFolder("Src", "Infrastructure", "Infrastructure.Auth");
        string[] contexts = projectFile.LocalAuth ? [auth, persistence] : [persistence];
        //#if (api)
        string api = projectFile.ProjectFolder("Src", "Presentation", "Api");
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
                Test(repoRoot, solution, configuration, noIntegration, nodes, modules);
                break;

            case "coverage":
                Coverage(repoRoot, solution, configuration, runsettings, nodes, modules);
                break;

            case "format":
                VerifyWhatTheBuildDoesNotCheck(solution, "--verbosity", "normal");
                break;

            case "format-fix":
                Step("dotnet", "format", solution);
                NormaliseMarkdown(repoRoot);
                RequireNamingStyle(repoRoot, solution);
                break;

            case "check":
                Check(projectFile, solution, configuration, nodes, modules);
                break;

            case "new-feature":
                Step(
                    "dotnet",
                    [
                        "run", Path.Combine(repoRoot, "Tools", "NewFeature.cs"),
                        RequiredSecond(task, name),
                        RequiredSecond(task, second),
                        "--root", repoRoot,
                        .. generatorArguments,
                    ]);
                break;

            case "new-field":
                Step(
                    "dotnet",
                    [
                        "run", Path.Combine(repoRoot, "Tools", "NewFeature.cs"),
                        "new-field",
                        RequiredFields(task, name, second),
                        second!,
                        "--root", repoRoot,
                        .. generatorArguments,
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
                        .. generatorArguments,
                    ]);
                break;

            case "new-setting":
                Step(
                    "dotnet",
                    [
                        "run", Path.Combine(repoRoot, "Tools", "NewSetting.cs"),
                        RequiredPair(task, name, second, "TodoLists TodoListLimits --keys \"MaxItems:int(1..500)=100\""),
                        second!,
                        "--root", repoRoot,
                        .. generatorArguments,
                    ]);
                break;

            case "new-event":
                Step(
                    "dotnet",
                    [
                        "run", Path.Combine(repoRoot, "Tools", "NewEvent.cs"),
                        RequiredPair(task, name, second, "TodoLists TodoListArchivedDomainEvent --unconsumed \"Nothing reacts to it yet\""),
                        second!,
                        "--root", repoRoot,
                        .. generatorArguments,
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
                string addTarget = MigrationContext(context, projectFile.LocalAuth, persistence, auth);
                RequireInitialMigrations(task, addTarget);
                AddMigration(addTarget, RequiredName(task, name), noBuild);
                break;

            case "migration-check":
                string checkTarget = MigrationContext(context, projectFile.LocalAuth, persistence, auth);
                RequireInitialMigrations(task, checkTarget);
                Step(
                    "dotnet", "ef", "migrations", "has-pending-model-changes",
                    "--project", checkTarget,
                    "--startup-project", checkTarget);
                break;

            case "database-update":
                string updateTarget = MigrationContext(context, projectFile.LocalAuth, persistence, auth);
                RequireInitialMigrations(task, updateTarget);
                Step(
                    "dotnet", "ef", "database", "update",
                    "--project", updateTarget,
                    "--startup-project", updateTarget);
                break;

            case "migration-bundle":
                RequireInitialMigrations(task, contexts);

                foreach ((string project, string output) in contexts.Select(context => (context, context == auth ? "efbundle-identity" : "efbundle")))
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
                OpenApiReference(projectFile, api, noBuild);
                break;

            case "api-client":
                ApiClient(projectFile, check);
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
                EndToEnd(projectFile, configuration, keepStack);
                break;

            //#endif

            case "sonar":
                if (!projectFile.Sonar)
                {
                    throw new InvalidOperationException(
                        "cleanarch.json records this project as generated with --sonar false: it has neither the SonarQube service nor the scanner image the task runs.");
                }

                Sonar(repoRoot);
                break;

            case "bootstrap":
                PresentationScaffold(projectFile, solution, only: null);
                Step("dotnet", "format", solution, "--diagnostics", "IDE0055", "IDE0005");
                DocsIndex(repoRoot, check: false);
                //#if (api)
                OpenApiReference(projectFile, api, noBuild: false);
                //#endif

                string finished = "the formatting, the documentation index";
                //#if (api)
                finished += " and the OpenAPI reference";
                //#endif

                try
                {
                    PinTheProjectsTool(repoRoot, projectFile);
                }
                catch (InvalidOperationException error)
                {
                    string version = projectFile.TemplateVersion;
                    throw new InvalidOperationException(
                        error.Message + $" CleanArch.Cli {version}, the tool this project pins, could not be installed: most likely that version is not on a NuGet source this machine uses "
                        + "(nuget.org holds only published releases, so a version not released yet, or a template packed locally, has no package there), or the machine is offline. "
                        + $"Done: {finished}. Not done: the tools' restore, the initial migrations and what follows. "
                        + $"Fix: put CleanArch.Cli.{version}.nupkg (dotnet pack Launcher/CleanArch.Cli in the template's repository) in a folder, run "
                        + $"'dotnet tool install CleanArch.Cli --local --version {version} --add-source <that folder>', then run bootstrap again.");
                }

                try
                {
                    Step("dotnet", "tool", "restore");
                }
                catch (InvalidOperationException error)
                {
                    throw new InvalidOperationException(
                        error.Message + $" Done: {finished}. The local tools are not restored, so the initial migrations "
                        + "are not created and the tasks that run dotnet ef cannot work yet: migration-add, database-update, "
                        + "migration-bundle and verify. Fix the restore and run bootstrap again.");
                }

                InitialMigrations(contexts);
                //#if (api)

                if (HoldsTheClient(projectFile))
                {
                    ApiClient(projectFile, check: false);
                }
                //#endif

                if (File.Exists(Path.Combine(repoRoot, "Src", "Web", "package.json")))
                {
                    WebScaffold(projectFile, solution, only: null);
                }

                Write(
                    /*#if (api)*/
                    "Formatting done, documentation index and OpenAPI reference written, tools restored and each context's initial migration in its Migrations folder. "
                    /*#else
                    "Formatting done, documentation index written, tools restored and each context's initial migration in its Migrations folder. "
                    #endif*/
                    + "Commit this before anything else: the migrations are the project's own from now on.",
                    ConsoleColor.Green);
                break;

            case "docs-index":
                DocsIndex(repoRoot, check);
                break;

            case "hygiene":
                Gates(repoRoot);
                break;

            case "verify":
                RequireInitialMigrations(task, contexts);
                Gates(repoRoot);

                if (projectFile.HasWebWorkspace)
                {
                    RequirePnpm(_pnpm);
                }
                //#if (api)

                if (HoldsTheClient(projectFile))
                {
                    ApiClient(projectFile, check: true);
                }

                WebSchema(projectFile);
                //#endif

                WebAudit(projectFile);

                if (projectFile.HasWebWorkspace)
                {
                    WebGate(repoRoot);
                }

                //#if (templateMaintenance)
                WebLockfiles(repoRoot, check: true);
                //#endif
                Step("dotnet", ["restore", solution, .. nodes]);
                VerifyWhatTheBuildDoesNotCheck(solution, "--no-restore");
                Step("dotnet", ["build", solution, "--configuration", configuration, "--no-restore", .. nodes]);
                TestTheBuiltSolution(repoRoot, solution, configuration, modules);
                Step(
                    "dotnet", "ef", "migrations", "has-pending-model-changes",
                    "--project", persistence,
                    "--startup-project", persistence,
                    "--no-build");
                if (projectFile.LocalAuth)
                {
                    Step(
                        "dotnet", "ef", "migrations", "has-pending-model-changes",
                        "--project", auth,
                        "--startup-project", auth,
                        "--no-build");
                }

                VulnerablePackages(solution);
                //#if (templateMaintenance)
                LauncherGate(repoRoot, configuration);
                //#endif
                break;

            case "upgrade-report":
                Step("dotnet", ["run", Gate(repoRoot, "UpgradeReport.cs"), "--", "--root", repoRoot, .. upgradeOptions]);
                break;

            case "presentation-scaffold":
                /*#if (api)*/
                if (PresentationScaffold(projectFile, solution, name) && File.Exists(OpenApiReferencePath(projectFile)))
                {
                    Step("dotnet", "tool", "restore");
                    ApiClient(projectFile, check: false);
                }
                /*#else
                PresentationScaffold(projectFile, solution, name);
                #endif*/

                break;

            case "presentation-join":
                PresentationJoin(projectFile, RequiredPresentation(task, name), solution);
                break;

            case "web-scaffold":
                /*#if (api)*/
                if (WebScaffold(projectFile, solution, name) && File.Exists(OpenApiReferencePath(projectFile)))
                {
                    Step("dotnet", "tool", "restore");
                    ApiClient(projectFile, check: false);
                }
                /*#else
                WebScaffold(projectFile, solution, name);
                #endif*/

                break;

            case "web-join":
                WebJoin(projectFile, RequiredApp(task, name), webOptions);
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
    private static void OpenApiReference(ProjectFile projectFile, string api, bool noBuild)
    {
        string apiName = projectFile.ProjectName("Api");
        string reference = Path.GetDirectoryName(OpenApiReferencePath(projectFile))!;
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal) { ["ASPNETCORE_ENVIRONMENT"] = "Development" };

        if (projectFile.ExternalAuth)
        {
            defaults["Authentication__External__Authority"] = "https://identity-provider.invalid/";
            defaults["Authentication__External__Audience"] = "openapi-document";
        }

        string project = Path.Combine(api, apiName + ".csproj");
        string[] document = [$"-p:OpenApiDocumentsDirectory={reference}", "-p:OpenApiGenerateDocumentsOptions=--file-name v1"];

        File.Delete(Path.Combine(api, "obj", apiName + ".OpenApiFiles.cache"));

        if (!noBuild)
        {
            StepWith(defaults, "dotnet", ["build", project, "-p:OpenApiGenerateDocumentsOnBuild=true", .. document]);
            return;
        }

        string output = Path.Combine(api, "bin");

        if (!Directory.Exists(output) || !Directory.EnumerateFiles(output, apiName + ".dll", SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException(
                $"--no-build writes the reference from the API's last build, and {apiName} has none: run `dotnet run Tools/Tasks.cs build` first.");
        }

        StepWith(defaults, "dotnet", ["msbuild", project, "-target:GenerateOpenApiDocuments", .. document]);
    }

    private static string OpenApiReferencePath(ProjectFile projectFile) =>
        Path.Combine(projectFile.ProjectFolder("Tests", "Integration", "Api.IntegrationTests"), "OpenApi", "v1.json");

    //#endif
    private static string ClientFolder(ProjectFile projectFile) => projectFile.ProjectFolder("Src", "Clients", "Client");

    private static bool HoldsTheClient(ProjectFile projectFile) => Directory.Exists(ClientFolder(projectFile));

    //#if (api)
    private static void ApiClient(ProjectFile projectFile, bool check)
    {
        string repoRoot = projectFile.Root;
        string reference = OpenApiReferencePath(projectFile);
        string generated = Path.Combine(ClientFolder(projectFile), "Generated");
        string shown = Path.GetRelativePath(repoRoot, generated).Replace('\\', '/');
        string output = check ? Path.Combine(Path.GetTempPath(), "api-client-" + Guid.NewGuid().ToString("N")) : generated;

        if (!HoldsTheClient(projectFile))
        {
            throw new InvalidOperationException(
                "Src/Clients/AT.Client does not exist, so there is no client to write: a presentation that calls the API through it brings it.");
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

    private static void PrintApiSteps(ProjectFile projectFile)
    {
        Console.WriteLine("Next, once the routes and contracts are as they should be:");
        Console.WriteLine("  dotnet run Tools/Tasks.cs openapi");

        if (HoldsTheClient(projectFile))
        {
            Console.WriteLine("  dotnet run Tools/Tasks.cs api-client");
        }

        if (HoldsTheWebSchema(projectFile))
        {
            Console.WriteLine("  pnpm api    (in Src/Web)");
        }
    }

    private static string ApiClientPackage(ProjectFile projectFile) =>
        Path.Combine(projectFile.Root, "Src", "Web", "packages", "api-client");

    private static bool HoldsTheWebSchema(ProjectFile projectFile) =>
        File.Exists(Path.Combine(ApiClientPackage(projectFile), "scripts", "generate.mjs"));

    private static void WebSchema(ProjectFile projectFile)
    {
        if (!HoldsTheWebSchema(projectFile))
        {
            Write("The project has no web workspace, so Src/Web/packages/api-client/src/generated/schema.ts is not checked.", ConsoleColor.Yellow);
            return;
        }

        string reference = OpenApiReferencePath(projectFile);

        if (!File.Exists(reference))
        {
            throw new InvalidOperationException(
                $"{Path.GetRelativePath(projectFile.Root, reference)} is missing: run `dotnet run Tools/Tasks.cs openapi` first.");
        }

        WebWorkspace(projectFile.Root, "install", "--frozen-lockfile");

        try
        {
            StepIn(ApiClientPackage(projectFile), _pnpm, "exec", "node", "scripts/generate.mjs", "--check", reference);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                error.Message + " Src/Web/packages/api-client/src/generated/schema.ts is out of date with the OpenAPI reference: "
                + "run `pnpm api` in Src/Web, then commit what it writes.");
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

    private static void EndToEnd(ProjectFile projectFile, string configuration, bool keepStack)
    {
        string repoRoot = projectFile.Root;
        string[] compose = ["compose", "--project-name", "app-template-end-to-end", "--env-file", ".env.example"];
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);

        if (projectFile.ExternalAuth)
        {
            defaults["EXTERNAL_AUTH_AUTHORITY"] = "https://identity-provider.invalid/";
            defaults["EXTERNAL_AUTH_AUDIENCE"] = "end-to-end";

            if (projectFile.RendersAWebAppOnTheServer)
            {
                defaults["EXTERNAL_AUTH_INTERACTIVE_CLIENT_ID"] = "end-to-end";
            }

            if (projectFile.HasAWebAppOnWebAssembly)
            {
                defaults["EXTERNAL_AUTH_BROWSER_CLIENT_ID"] = "end-to-end";
            }
        }

        if (projectFile.Files)
        {
            defaults["JOBS_FILES_INSPECT_INTERVAL"] = "00:00:10";
        }

        Dictionary<string, string> environment = Unset(defaults);
        bool passed = false;

        try
        {
            Launch(repoRoot, environment, "docker", [.. compose, "up", "--build", "--detach", "--wait", "--wait-timeout", "300"]);
            Step(
                "dotnet", "run",
                "--project", Path.Combine(projectFile.ProjectFolder("Tests", "EndToEnd", "EndToEnd.Tests"), projectFile.ProjectName("EndToEnd.Tests") + ".csproj"),
                "--configuration", configuration);
            BrowserSmoke(projectFile, environment);
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

    private static void BrowserSmoke(ProjectFile projectFile, Dictionary<string, string> environment)
    {
        if (!projectFile.HasWebApps)
        {
            return;
        }

        var smoke = new Dictionary<string, string>(environment, StringComparer.Ordinal) { ["SMOKE_ENV_FILE"] = ".env.example" };

        try
        {
            Launch(Path.Combine(projectFile.Root, "Src", "Web"), smoke, _pnpm, ["--filter", "*browser-smoke", "smoke"]);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                error.Message + " The browser smoke needs `pnpm install --frozen-lockfile` in Src/Web and Chromium: install it with "
                + "`pnpm --filter *browser-smoke exec playwright install --only-shell chromium` in Src/Web, adding `--with-deps` on Linux "
                + "for the system libraries it loads.");
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
    private static void WebAudit(ProjectFile projectFile)
    {
        if (!File.Exists(Path.Combine(projectFile.Root, "Src", "Web", "package.json")))
        {
            return;
        }

        try
        {
            StepIn(Path.Combine(projectFile.Root, "Src", "Web"), _pnpm, "audit");
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                error.Message + " A web package with a known advisory is locked, at any severity. Raise the dependency, "
                + "or pin a patched release under overrides in Src/Web/pnpm-workspace.yaml with a comment saying when it can go; "
                + "then run `pnpm install` in Src/Web and commit the lockfile.");
        }
    }

    private static readonly string[] _webScripts = ["lint", "test", "build"];

    private static void RequirePnpm(string pnpm)
    {
        try
        {
            Capture(null, echo: false, pnpm, "--version");
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                error.Message + " cleanarch.json records a web workspace, so verify installs, lints, tests and builds Src/Web as the web workflow does, "
                + "which needs Node.js, the version in Src/Web/.node-version, and pnpm: install Node.js, run `corepack enable` once, then run verify again.");
        }
    }

    private static void WebGate(string repoRoot)
    {
        WebWorkspace(repoRoot, "install", "--frozen-lockfile");

        foreach (string script in _webScripts)
        {
            StepIn(Path.Combine(repoRoot, "Src", "Web"), _pnpm, script);
        }
    }

    private static void Gates(string repoRoot)
    {
        Step("dotnet", "run", Gate(repoRoot, "Tasks.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "NewFeature.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "NewUseCase.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "NewSetting.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "NewEvent.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "RemoveFeature.cs"), "--self-test");
        Step("dotnet", "run", Gate(repoRoot, "UpgradeReport.cs"), "--self-test");
        OptionalGate(repoRoot, "CheckDocPaths.cs", "--self-test");
        OptionalGate(repoRoot, "CheckDocPaths.cs", repoRoot);
        OptionalGate(repoRoot, "CheckWorkflows.cs", "--self-test");
        OptionalGate(repoRoot, "CheckWorkflows.cs", repoRoot);
        Step("dotnet", "run", Gate(repoRoot, "CoverageGate.cs"), "--self-test");
        OptionalGate(repoRoot, "CheckComments.cs", "--self-test");
        OptionalGate(repoRoot, "CheckComments.cs", repoRoot);
        DocsIndex(repoRoot, check: true);
    }

    private const string _docsFolder = "docs";

    private const string _indexName = "index.md";

    private static readonly string[] _pagesOutsideTheIndex = [_indexName, "log.md"];

    private static readonly string[] _foldersOutsideTheIndex = ["plans"];

    private const string _indexNotice =
        "Written by `dotnet run Tools/Tasks.cs docs-index` from the frontmatter of each page:\nedit the pages, then run it again.";

    private sealed record IndexEntry(string Link, string Title, string Description, string? Status);

    private static void DocsIndex(string repoRoot, bool check)
    {
        string docs = Path.Combine(repoRoot, _docsFolder);
        Dictionary<string, string> indexes = DocsIndexes(docs);
        string[] present = Directory.Exists(docs) ? [.. IndexFiles(docs).Select(file => Path.GetRelativePath(repoRoot, file).Replace('\\', '/'))] : [];
        string[] stale = [.. present.Where(path => !indexes.ContainsKey(path))];

        if (check)
        {
            string[] problems =
            [
                .. indexes
                    .Where(index => !File.Exists(Path.Combine(repoRoot, index.Key)) || File.ReadAllText(Path.Combine(repoRoot, index.Key)) != index.Value)
                    .Select(index => File.Exists(Path.Combine(repoRoot, index.Key)) ? $"{index.Key} differs from what it writes" : $"{index.Key} is missing"),
                .. stale.Select(path => $"{path} indexes a folder that holds no page"),
            ];

            if (problems.Length > 0)
            {
                throw new InvalidOperationException(
                    $"The documentation index is out of date: {string.Join("; ", problems)}. "
                    + "Run `dotnet run Tools/Tasks.cs docs-index` and commit what it writes.");
            }

            Write($"The {indexes.Count} index files under {_docsFolder}/ list their folders' pages as their frontmatter says.", ConsoleColor.Green);
            return;
        }

        foreach ((string path, string text) in indexes)
        {
            File.WriteAllText(Path.Combine(repoRoot, path), text, new UTF8Encoding(false));
        }

        foreach (string path in stale)
        {
            File.Delete(Path.Combine(repoRoot, path));
        }

        Write($"Wrote {indexes.Count} index files under {_docsFolder}/.", ConsoleColor.Green);
    }

    private static Dictionary<string, string> DocsIndexes(string docs)
    {
        var indexes = new Dictionary<string, string>(StringComparer.Ordinal);

        if (Directory.Exists(docs))
        {
            IndexFolder(Path.GetDirectoryName(docs)!, docs, indexes);
        }

        return indexes;
    }

    private static IEnumerable<string> IndexFiles(string folder) =>
        Directory.EnumerateFiles(folder, _indexName)
            .Concat(IndexedFolders(folder).SelectMany(IndexFiles));

    private static IEnumerable<string> IndexedFolders(string folder) =>
        Directory.EnumerateDirectories(folder)
            .Where(child => Path.GetFileName(child) is var name
                && !name.StartsWith('_')
                && !name.StartsWith('.')
                && !_foldersOutsideTheIndex.Contains(name, StringComparer.Ordinal));

    private static IndexEntry? IndexFolder(string root, string folder, Dictionary<string, string> indexes)
    {
        IndexEntry[] pages =
        [
            .. Directory.EnumerateFiles(folder, "*.md")
                .Where(file => !_pagesOutsideTheIndex.Contains(Path.GetFileName(file), StringComparer.Ordinal))
                .Select(file => PageEntry(root, file))
                .OrderBy(page => page.Link != "README.md")
                .ThenBy(page => page.Title, StringComparer.Ordinal),
        ];
        IndexEntry[] folders =
        [
            .. IndexedFolders(folder)
                .Select(child => IndexFolder(root, child, indexes))
                .OfType<IndexEntry>()
                .OrderBy(entry => entry.Title, StringComparer.Ordinal),
        ];

        if (pages.Length == 0 && folders.Length == 0)
        {
            return null;
        }

        string name = Path.GetFileName(folder);
        IndexEntry? readme = pages.FirstOrDefault(page => page.Link == "README.md");
        string title = readme?.Title ?? char.ToUpperInvariant(name[0]) + name[1..].Replace('-', ' ');
        var text = new StringBuilder().Append("# ").Append(title).Append("\n\n").Append(_indexNotice).Append('\n');

        if (pages.Length > 0)
        {
            text.Append('\n');
            text.Append(string.Concat(pages.Select(IndexLine)));
        }

        if (folders.Length > 0)
        {
            text.Append("\n## Folders\n\n");
            text.Append(string.Concat(folders.Select(IndexLine)));
        }

        indexes[Path.GetRelativePath(root, Path.Combine(folder, _indexName)).Replace('\\', '/')] = text.ToString();

        return new IndexEntry($"{name}/{_indexName}", title, readme?.Description ?? string.Empty, null);
    }

    private static string IndexLine(IndexEntry entry) =>
        $"- [{entry.Title}]({entry.Link})"
        + (entry.Description.Length == 0 ? string.Empty : " - " + (entry.Status is null ? string.Empty : entry.Status + ". ") + entry.Description)
        + "\n";

    private static IndexEntry PageEntry(string root, string file)
    {
        string page = Path.GetRelativePath(root, file).Replace('\\', '/');
        string[] lines = File.ReadAllText(file).ReplaceLineEndings("\n").Split('\n');
        int closing = lines[0] == "---" ? Array.IndexOf(lines, "---", 1) : -1;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string line in closing < 0 ? [] : lines[1..closing])
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);

            if (colon > 0)
            {
                string value = line[(colon + 2)..].Trim();
                fields[line[..colon]] = value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;
            }
        }

        string Field(string key) => fields.TryGetValue(key, out string? value) && value.Length > 0
            ? value
            : throw new InvalidOperationException(
                $"{page} has no {key} in a frontmatter at its first line, so the index cannot list it: "
                + "docs/README.md#adding-a-page says what each page's frontmatter holds.");

        return new IndexEntry(
            Path.GetFileName(file),
            Field("title"),
            Field("description"),
            fields.GetValueOrDefault("type") == "decision" ? Field("status") : null);
    }

    private static readonly string[] _styleRulesTheBuildLacks = ["IDE0003", "IDE0049"];

    private static void VerifyWhatTheBuildDoesNotCheck(string solution, params string[] options) =>
        Step("dotnet", ["format", solution, "--diagnostics", .. _styleRulesTheBuildLacks, "--verify-no-changes", .. options]);

    private static readonly string[] _baseCandidates = ["origin/develop", "origin/HEAD", "origin/main", "origin/master", "develop", "main", "master"];

    private static readonly string[] _buildWideFileNames = ["Directory.Build.rsp", "global.json", "nuget.config", ".editorconfig", ".globalconfig"];

    private static readonly string[] _buildWideExtensions = [".csproj", ".props", ".targets", ".sln", ".slnx"];

    private static readonly Regex _namingViolation = new(
        @"^(?<file>.+?)\((?<line>\d+),\d+\): (?:error|warning) IDE1006: (?<message>.+?) \[[^\]]+\]\s*$",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private sealed record ProjectNode(string Path, string Folder, IReadOnlyList<string> References, bool DockerFreeTests, bool WindowsOnly = false);

    private sealed record CheckScope(bool BuildWide, IReadOnlyList<string> Projects);

    private static void Check(ProjectFile projectFile, string solution, string configuration, string[] nodes, string[] modules)
    {
        string repoRoot = projectFile.Root;
        string[] changed = ChangedFiles(repoRoot);

        if (changed.Length == 0)
        {
            Write("Nothing is touched: the working tree and this branch hold no change from the base, so there is nothing to check.", ConsoleColor.Yellow);
            return;
        }

        ProjectNode[] graph = SolutionProjects(repoRoot, solution);
        string architecture = ProjectPath(projectFile, "Tests", "Architecture", "Architecture.Tests");

        if (graph.All(project => project.Path != architecture))
        {
            throw new InvalidOperationException($"The solution holds no {architecture}, the project whose rules every check runs.");
        }

        CheckScope scope = ScopeOf(changed, graph);
        string[] formatted = FormattableFiles(changed, graph, file => File.Exists(Path.Combine(repoRoot, file)));
        string[] built = [.. scope.Projects.Append(architecture).Distinct(StringComparer.Ordinal)];
        (string[] tested, string[] skipped) = TestedProjects(built, graph, architecture, OperatingSystem.IsWindows());
        string scopeLine = scope.BuildWide
            ? "A file that sets the build for every project is touched: the whole solution is built and its Docker-free tests run."
            : $"Touched: {changed.Length} files, {scope.Projects.Count} projects with the ones that reference them.";
        Write(scopeLine, ConsoleColor.DarkGray);

        if (formatted.Length > 0)
        {
            try
            {
                Step("dotnet", ["format", solution, "--diagnostics", .. _styleRulesTheBuildLacks, "--verify-no-changes", "--include", .. formatted]);
            }
            catch (InvalidOperationException error)
            {
                throw new InvalidOperationException(
                    error.Message + " Run `dotnet run Tools/Tasks.cs format-fix`: it fixes what it can and names what it cannot.");
            }
        }

        string[] filters = [];
        try
        {
            string buildTarget = solution;
            string testTarget;

            if (scope.BuildWide)
            {
                testTarget = DockerFreeSolutionFilter(repoRoot, solution);
                filters = [testTarget];
            }
            else
            {
                buildTarget = SolutionFilter(repoRoot, solution, "check-build.slnf", built.Select(path => Path.Combine(repoRoot, path)));
                testTarget = SolutionFilter(repoRoot, solution, "check-test.slnf", tested.Select(path => Path.Combine(repoRoot, path)));
                filters = [buildTarget, testTarget];
            }

            Step("dotnet", ["build", buildTarget, "--configuration", configuration, .. nodes]);
            RunSkipping(repoRoot, skipped, () => Step("dotnet", ["test", "--solution", testTarget, "--configuration", configuration, "--no-build", .. modules]));
        }
        finally
        {
            foreach (string filter in filters)
            {
                File.Delete(filter);
            }
        }

        Write(
            $"Format of {formatted.Length} C# file(s), build of {(scope.BuildWide ? "the solution" : built.Length + " project(s)")}, tests of {(scope.BuildWide ? "every Docker-free project" : tested.Length + " project(s)")}: all passed. "
            + "The integration tests, coverage and the other gates of verify are left to verify, coverage and CI.",
            ConsoleColor.DarkGray);
    }

    private static (string[] Tested, string[] Skipped) TestedProjects(IEnumerable<string> built, IReadOnlyList<ProjectNode> graph, string architecture, bool onWindows)
    {
        ProjectNode Node(string path) => graph.First(project => project.Path == path);
        string[] candidates = [.. built.Where(path => path == architecture || Node(path).DockerFreeTests)];
        string[] skipped = onWindows ? [] : [.. candidates.Where(path => Node(path).WindowsOnly)];

        return ([.. candidates.Except(skipped, StringComparer.Ordinal)], skipped);
    }

    private static string ProjectPath(ProjectFile projectFile, string tree, string layer, string part) =>
        $"{tree}/{layer}/{projectFile.FolderOf(part)}/{projectFile.ProjectName(part)}.csproj";

    private static string[] ChangedFiles(string repoRoot)
    {
        if (Capture(repoRoot, echo: false, "git", "rev-parse", "--show-toplevel").ExitCode != 0)
        {
            throw new InvalidOperationException($"check reads what git diff touches, and {repoRoot} is not inside a git working tree.");
        }

        string? baseline = PickBase(candidate => Capture(repoRoot, echo: false, "git", "rev-parse", "--verify", "--quiet", candidate + "^{commit}").ExitCode == 0);
        string baseCommit = "HEAD";

        if (baseline is not null)
        {
            (int exit, string output) = Capture(repoRoot, echo: false, "git", "merge-base", "HEAD", baseline);
            baseCommit = exit == 0 ? output.Trim() : baseline;
        }

        Write($"Compared with {(baseCommit == "HEAD" ? "HEAD" : baseline + " at " + baseCommit[..Math.Min(8, baseCommit.Length)])}, the working tree and the files not yet tracked.", ConsoleColor.DarkGray);

        return
        [
            .. GitLines(repoRoot, "diff", "--name-only", "--no-renames", "--relative", "-z", baseCommit),
            .. GitLines(repoRoot, "ls-files", "--others", "--exclude-standard", "-z"),
        ];
    }

    private static string? PickBase(Func<string, bool> exists) => _baseCandidates.FirstOrDefault(exists);

    private static string[] GitLines(string repoRoot, params string[] arguments)
    {
        (int exit, string output) = Capture(repoRoot, echo: true, "git", arguments);

        return exit == 0
            ? [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)]
            : throw new InvalidOperationException($"'git {string.Join(' ', arguments)}' exited with {exit}: {output.Trim()}");
    }

    private static ProjectNode[] SolutionProjects(string repoRoot, string solution)
    {
        string[] paths =
        [
            .. Regex.Matches(File.ReadAllText(solution), @"""([^""]+\.csproj)""")
                .Select(match => match.Groups[1].Value.Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];
        string Relative(string full) => Path.GetRelativePath(repoRoot, full).Replace('\\', '/');

        return
        [
            .. paths.Select(path =>
            {
                string full = Path.GetFullPath(Path.Combine(repoRoot, path));
                string text = File.ReadAllText(full);
                string[] references =
                [
                    .. XDocument.Parse(text).Descendants("ProjectReference")
                        .Select(reference => reference.Attribute("Include")?.Value)
                        .OfType<string>()
                        .Select(include => Relative(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(full)!, include.Replace('\\', '/'))))),
                ];

                return new ProjectNode(path, Path.GetDirectoryName(path)!.Replace('\\', '/'), references, IsDockerFreeTestProject(text), WindowsTargetFramework(text) is not null);
            }),
        ];
    }

    private static CheckScope ScopeOf(IEnumerable<string> changed, IReadOnlyList<ProjectNode> graph)
    {
        string[] files = [.. changed];

        if (files.Any(file => SetsTheBuildForEveryProject(file, graph)))
        {
            return new CheckScope(BuildWide: true, [.. graph.Select(project => project.Path)]);
        }

        HashSet<string> affected = [.. files.Select(file => OwnerOf(file, graph)).OfType<ProjectNode>().Select(project => project.Path)];

        for (bool grew = true; grew;)
        {
            ProjectNode[] dependents = [.. graph.Where(project => !affected.Contains(project.Path) && project.References.Any(affected.Contains))];
            grew = dependents.Length > 0;
            affected.UnionWith(dependents.Select(project => project.Path));
        }

        return new CheckScope(BuildWide: false, [.. affected.Order(StringComparer.Ordinal)]);
    }

    private static ProjectNode? OwnerOf(string file, IReadOnlyList<ProjectNode> graph) =>
        graph.Where(project => file.StartsWith(project.Folder + "/", StringComparison.Ordinal)).MaxBy(project => project.Folder.Length);

    private static bool SetsTheBuildForEveryProject(string file, IReadOnlyList<ProjectNode> graph)
    {
        int slash = file.LastIndexOf('/');
        string name = file[(slash + 1)..];
        string folder = slash < 0 ? string.Empty : file[..slash];

        bool buildWide = _buildWideFileNames.Contains(name, StringComparer.OrdinalIgnoreCase)
            || _buildWideExtensions.Any(extension => name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        return buildWide
            && (folder.Length == 0
                || graph.Any(project => project.Folder == folder
                    || project.Folder.StartsWith(folder + "/", StringComparison.Ordinal)
                    || folder.StartsWith(project.Folder + "/", StringComparison.Ordinal)));
    }

    private static string[] FormattableFiles(IEnumerable<string> changed, IReadOnlyList<ProjectNode> graph, Func<string, bool> exists) =>
        [.. changed.Where(file => file.EndsWith(".cs", StringComparison.Ordinal) && exists(file) && OwnerOf(file, graph) is not null).Order(StringComparer.Ordinal)];

    private static void RequireNamingStyle(string repoRoot, string solution)
    {
        (int exit, string output) = Capture(repoRoot, echo: true, "dotnet", "format", solution, "--diagnostics", "IDE1006", "--verify-no-changes", "--no-restore");
        string[] violations = NamingViolations(repoRoot, output);

        if (violations.Length > 0)
        {
            throw new InvalidOperationException(
                "dotnet format cannot rename a symbol, so these names break a naming rule of .editorconfig (IDE1006) and are left as they are:"
                + Environment.NewLine + string.Join(Environment.NewLine, violations.Select(violation => "  " + violation))
                + Environment.NewLine + "Rename each one, with the IDE's rename or by hand, then run format-fix again.");
        }

        if (exit != 0)
        {
            Console.Write(output);
            throw new InvalidOperationException($"dotnet format exited with {exit} and named no naming violation: its output is above.");
        }
    }

    private static readonly string[] _markdownSkippedDirectories = ["bin", "obj", "node_modules", "plans"];

    private static readonly string[] _markdownKeptDotDirectories = [".github", ".template.config"];

    private static void NormaliseMarkdown(string repoRoot)
    {
        foreach (string file in MarkdownFiles(repoRoot))
        {
            byte[] content = File.ReadAllBytes(file);
            byte[] normalised = NormalisedMarkdown(content);

            if (!normalised.AsSpan().SequenceEqual(content))
            {
                File.WriteAllBytes(file, normalised);
                Write($"{Path.GetRelativePath(repoRoot, file).Replace('\\', '/')}: saved as UTF-8 without a BOM, with LF endings.", ConsoleColor.DarkGray);
            }
        }
    }

    private static IEnumerable<string> MarkdownFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.md")
            .Concat(Directory.EnumerateDirectories(directory)
                .Where(child => Path.GetFileName(child) is var name
                    && !_markdownSkippedDirectories.Contains(name, StringComparer.Ordinal)
                    && (!name.StartsWith('.') || _markdownKeptDotDirectories.Contains(name, StringComparer.Ordinal)))
                .SelectMany(MarkdownFiles));

    private static byte[] NormalisedMarkdown(byte[] content)
    {
        ReadOnlySpan<byte> text = content.AsSpan(content is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0);
        var normalised = new List<byte>(text.Length);

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] != (byte)'\r')
            {
                normalised.Add(text[index]);
            }
            else if (index + 1 == text.Length || text[index + 1] != (byte)'\n')
            {
                normalised.Add((byte)'\n');
            }
        }

        return [.. normalised];
    }

    private static string[] NamingViolations(string repoRoot, string output) =>
        [.. _namingViolation.Matches(output)
            .Select(match => $"{Path.GetRelativePath(repoRoot, match.Groups["file"].Value).Replace('\\', '/')}:{match.Groups["line"].Value}: {match.Groups["message"].Value}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

    //#if (templateMaintenance)
    private static void LauncherGate(string repoRoot, string configuration)
    {
        string launcher = Path.Combine(repoRoot, "Launcher", "CleanArch.Create.slnx");

        Step("dotnet", "restore", launcher);
        VerifyWhatTheBuildDoesNotCheck(launcher, "--no-restore");
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
        string[] skipped = SkippedHere(TestProjects(repoRoot));

        if (nodes.Length > 0 || skipped.Length > 0)
        {
            Step("dotnet", ["build", solution, "--configuration", configuration, .. nodes]);
            built = ["--no-build"];
        }

        RunSkipping(repoRoot, skipped, () =>
        {
            string? filter = skipped.Length > 0 ? RunnableSolutionFilter(repoRoot, solution, skipped) : null;
            try
            {
                Step(
                    "dotnet",
                    [
                        "test", "--solution", filter ?? solution,
                        "--configuration", configuration,
                        .. built,
                        .. modules,
                        "--results-directory", results,
                        "--coverage",
                        "--coverage-settings", runsettings,
                        "--coverage-output-format", "cobertura",
                    ]);
            }
            finally
            {
                DeleteFilter(filter);
            }

            string minimum = CoverageMinimum(Path.Combine(repoRoot, "coverage.minimum"));

            Step(
                "dotnet", "run", Gate(repoRoot, "CoverageGate.cs"),
                "--root", results,
                "--minimum", minimum);
        });
    }

    private static void Test(string repoRoot, string solution, string configuration, bool noIntegration, string[] nodes, string[] modules)
    {
        string[] tests = TestProjects(repoRoot, dockerFreeOnly: noIntegration);
        string[] skipped = SkippedHere(tests);
        RunSkipping(repoRoot, skipped, () =>
        {
            string? run = noIntegration
                ? DockerFreeSolutionFilter(repoRoot, solution)
                : skipped.Length > 0 ? RunnableSolutionFilter(repoRoot, solution, skipped) : null;
            string? build = noIntegration && skipped.Length > 0 ? SolutionFilter(repoRoot, solution, "docker-free-build.slnf", tests) : null;
            try
            {
                if (nodes.Length == 0 && skipped.Length == 0)
                {
                    Step("dotnet", "test", "--solution", run ?? solution, "--configuration", configuration);
                }
                else
                {
                    Step("dotnet", ["build", build ?? run ?? solution, "--configuration", configuration, .. nodes]);
                    Step("dotnet", ["test", "--solution", run ?? solution, "--configuration", configuration, "--no-build", .. modules]);
                }
            }
            finally
            {
                DeleteFilter(run);
                DeleteFilter(build);
            }
        });
    }

    private static void TestTheBuiltSolution(string repoRoot, string solution, string configuration, string[] modules)
    {
        string[] skipped = SkippedHere(TestProjects(repoRoot));
        RunSkipping(repoRoot, skipped, () =>
        {
            string? filter = skipped.Length > 0 ? RunnableSolutionFilter(repoRoot, solution, skipped) : null;
            try
            {
                Step("dotnet", ["test", "--solution", filter ?? solution, "--configuration", configuration, "--no-build", .. modules]);
            }
            finally
            {
                DeleteFilter(filter);
            }
        });
    }

    private static void Sonar(string repoRoot)
    {
        Step("docker", "compose", "--profile", "sonar", "up", "--detach", "--wait", "sonarqube");

        string[] tests = TestProjects(repoRoot, dockerFreeOnly: true);
        string[] skipped = WindowsOnly(tests, File.ReadAllText, onWindows: false);
        RunSkipping(repoRoot, skipped, () => Step(
            "docker", "compose", "--profile", "sonar",
            "run", "--rm", "--build", "sonar-scanner",
            "/bin/sh", "-c", ScanScript(repoRoot, tests.Except(skipped, StringComparer.Ordinal))));
    }

    private static string ScanScript(string repoRoot, IEnumerable<string> testProjects)
    {
        IEnumerable<string> tests = testProjects
            .Select(project => Path.GetRelativePath(repoRoot, project).Replace('\\', '/'))
            .Select(project =>
                $"""
                dotnet test --project "{project}" \
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

    private const string _scaffoldDescriptor = "scaffold.json";

    private static readonly string _pnpm = OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";

    private static readonly Regex _exactVersion = new(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$");

    private static readonly JsonSerializerOptions _writtenJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void WebWorkspace(string repoRoot, params string[] arguments)
    {
        try
        {
            StepIn(Path.Combine(repoRoot, "Src", "Web"), _pnpm, arguments);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                error.Message + " The web workspace needs Node.js and pnpm: run `corepack enable` once, then "
                + "`pnpm install` and `pnpm api` in Src/Web.");
        }
    }

    private static bool WebScaffold(ProjectFile projectFile, string solution, string? only)
    {
        string web = Path.Combine(projectFile.Root, "Src", "Web");

        if (!File.Exists(Path.Combine(web, "package.json")))
        {
            throw new InvalidOperationException(
                "Src/Web holds no package.json, so there is no web workspace to create an app in. "
                + "dotnet new cleanarch-webapp --workspace adds a first app and the workspace around it.");
        }

        string apps = Path.Combine(web, "apps");
        string[] pending = Directory.Exists(apps)
            ? [.. Directory.GetDirectories(apps)
                .Where(folder => File.Exists(Path.Combine(folder, _scaffoldDescriptor)) && !File.Exists(Path.Combine(folder, "package.json")))
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
                Write($"Src/Web/apps/{only} holds no {_scaffoldDescriptor}: it is created already, and left alone.", ConsoleColor.Green);
                pending = [];
            }
            else
            {
                throw new InvalidOperationException(
                    $"Src/Web/apps/{only} holds no {_scaffoldDescriptor}, so there is nothing to create. "
                    + "dotnet new cleanarch-webapp writes one in the app's folder.");
            }
        }

        bool client = false;

        foreach (string folder in pending)
        {
            if (ReadJson(Path.Combine(folder, _scaffoldDescriptor))["kind"]?.GetValue<string>() is "dotnet")
            {
                client |= ScaffoldProject(projectFile, solution, folder);
            }
            else
            {
                ScaffoldApp(web, folder);
            }
        }

        WebWorkspace(projectFile.Root, pending.Length > 0 ? ["install", "--no-frozen-lockfile"] : ["install"]);

        string[] created = Directory.Exists(apps)
            ? [.. Directory.GetDirectories(apps).Where(folder => File.Exists(Path.Combine(folder, "package.json")))]
            : [];
        bool[] pinned = [.. created.Select(PinInstalledVersions)];

        if (pinned.Contains(true))
        {
            WebWorkspace(projectFile.Root, "install", "--no-frozen-lockfile");
        }

        WebWorkspace(projectFile.Root, "api");

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
        JsonObject descriptor = ReadJson(Path.Combine(folder, _scaffoldDescriptor));
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
                Launch(scratch, new Dictionary<string, string>(StringComparer.Ordinal), _pnpm, arguments, closeInput: true);
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
                File.WriteAllText(path, merged.ToJsonString(_writtenJson) + "\n");
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

        File.Delete(Path.Combine(folder, _scaffoldDescriptor));
        Write($"Created Src/Web/apps/{app} from {cli} and its overlay.", ConsoleColor.Green);
    }

    private static readonly string[] _generatedPropertiesTheBuildSets = ["TargetFramework", "Nullable", "ImplicitUsings"];

    private static bool PresentationScaffold(ProjectFile projectFile, string solution, string? only)
    {
        string presentation = Path.Combine(projectFile.Root, "Src", "Presentation");
        string[] pending = [.. Directory.GetDirectories(presentation)
            .Where(folder => File.Exists(Path.Combine(folder, _scaffoldDescriptor)))
            .Order(StringComparer.Ordinal)];

        if (only is not null)
        {
            string folder = Path.Combine(presentation, projectFile.FolderOfProject(only));
            string shown = Path.GetRelativePath(projectFile.Root, folder).Replace('\\', '/');

            if (pending.Contains(folder, StringComparer.Ordinal))
            {
                pending = [folder];
            }
            else if (File.Exists(Path.Combine(folder, only + ".csproj")))
            {
                Write($"{shown} holds no {_scaffoldDescriptor}: it is created already, and left alone.", ConsoleColor.Green);
                pending = [];
            }
            else
            {
                throw new InvalidOperationException($"{shown} holds no {_scaffoldDescriptor}, so there is nothing to create.");
            }
        }

        bool client = false;

        foreach (string folder in pending)
        {
            client |= ScaffoldProject(projectFile, solution, folder);
        }

        return client;
    }

    private static bool ScaffoldProject(ProjectFile projectFile, string solution, string folder)
    {
        string repoRoot = projectFile.Root;
        string project = Path.GetFileName(folder);
        string shown = Path.GetRelativePath(repoRoot, folder).Replace('\\', '/');
        string task = shown.StartsWith("Src/Web/", StringComparison.Ordinal) ? "web-scaffold" : "presentation-scaffold";
        JsonObject descriptor = ReadJson(Path.Combine(folder, _scaffoldDescriptor));
        string kind = descriptor["kind"]?.GetValue<string>() ?? "pnpm";

        if (kind is not ("dotnet" or "written"))
        {
            throw new InvalidOperationException(
                $"{shown}/{_scaffoldDescriptor} is of kind '{kind}', and a .NET project is created by dotnet new or written by the template.");
        }

        string name = Text(descriptor, "name", project);
        string package = Text(descriptor, "package", project) + "@" + Text(descriptor, "version", project);

        if (kind == "dotnet")
        {
            Generate(descriptor, folder, task == "web-scaffold" ? project : name, name, package, shown, task);
        }

        File.Delete(Path.Combine(folder, _scaffoldDescriptor));

        string source = Path.Combine(folder, name + ".csproj");
        bool client = HoldsTheClient(projectFile) && File.ReadAllText(source).Contains(@"..\..\Clients\", StringComparison.Ordinal);
        string tests = Path.Combine(repoRoot, "Tests", task == "web-scaffold" ? "Web" : "Presentation", projectFile.FolderOfProject(name + ".UnitTests"), name + ".UnitTests.csproj");
        string[] projects =
        [
            source,
            .. new[] { tests }.Where(File.Exists),
            .. (client ? ClientProjects(projectFile) : []),
        ];

        foreach (string[] arguments in SolutionAddArguments(repoRoot, solution, projects))
        {
            Step("dotnet", arguments);
        }

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

    private static string SolutionFolderOf(string repoRoot, string project) =>
        Path.GetRelativePath(repoRoot, Path.GetDirectoryName(Path.GetDirectoryName(project))!).Replace('\\', '/');

    private static string[][] SolutionAddArguments(string repoRoot, string solution, string[] projects) =>
        [.. projects
            .GroupBy(project => SolutionFolderOf(repoRoot, project), StringComparer.Ordinal)
            .Select(group => (string[])["sln", solution, "add", "--solution-folder", group.Key, .. group])];

    private static string[] ClientProjects(ProjectFile projectFile) =>
        [.. new[]
        {
            Path.Combine(ClientFolder(projectFile), projectFile.ProjectName("Client") + ".csproj"),
            Path.Combine(projectFile.ProjectFolder("Tests", "Clients", "Client.UnitTests"), projectFile.ProjectName("Client.UnitTests") + ".csproj"),
        }.Where(File.Exists)];

    private const string _joiningSection = "## Joining a project generated without one";

    private static readonly Regex _joiningEdit = new(
        @"^- in `(?<path>[^`]+)`, (?<mode>after|before|replace|remove) [^:]*:$", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static void PresentationJoin(ProjectFile projectFile, string project, string solution)
    {
        string prefix = projectFile.Name + ".";

        if (!project.StartsWith(prefix, StringComparison.Ordinal) || project.Length == prefix.Length)
        {
            throw new InvalidOperationException($"'{project}' is not a presentation project of {projectFile.Name}: its name is {prefix}<Name>.");
        }

        string name = project[prefix.Length..];
        string relative = "Src/Presentation/" + projectFile.FolderOf(name);
        string descriptor = Path.Combine(projectFile.Root, relative, _scaffoldDescriptor);
        bool recorded = projectFile.Presentations.Contains(name, StringComparer.Ordinal);

        if (!recorded && !File.Exists(descriptor))
        {
            throw new InvalidOperationException(
                $"{relative} holds no {_scaffoldDescriptor} to read its framework from, so no file was changed: name the folder cleanarch-presentation wrote, before presentation-scaffold creates it.");
        }

        if (name == _workerName)
        {
            WorkerJoin(projectFile, relative, solution);
        }
        else
        {
            Join(projectFile.Root, relative, "cleanarch-presentation", "presentation-scaffold", required: true);
        }

        DocsIndex(projectFile.Root, check: false);
        //#if (api)
        PrintApiSteps(projectFile);
        //#endif

        if (!recorded)
        {
            projectFile.RecordPresentation(name, Text(ReadJson(descriptor), "framework", relative));
        }

        RecordClient(projectFile);
    }

    private const string _workerName = "Worker";

    private static readonly string[] _notInTheProject = [".git", "bin", "obj", "node_modules", "artifacts", "TestResults"];

    private static readonly Regex _unreachable = new(
        @"^\s*(?<file>[^\s(][^(\r\n]*)\(\d+,\d+\): error (?:CS0246|CS0103|CS0234): The (?:type or namespace )?name '(?<name>[^']+)'(?: does not exist in the namespace '(?<space>[^']+)')?",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(2));

    private static void WorkerJoin(ProjectFile projectFile, string relative, string solution)
    {
        string root = projectFile.Root;
        string readme = $"{relative}/README.md";

        if (!Directory.Exists(projectFile.ProjectFolder("Src", "Presentation", "Api")))
        {
            throw new InvalidOperationException(
                $"{projectFile.Name} holds no API, whose jobs the worker takes: a project without the API runs them in its worker already, so no file was changed.");
        }

        if (!File.Exists(Path.Combine(root, readme)))
        {
            throw new InvalidOperationException($"{readme} does not exist: name the folder cleanarch-presentation wrote under Src/Presentation.");
        }

        List<ReadmeEdit> edits = ReadmeEdits(File.ReadAllLines(Path.Combine(root, readme)), readme);
        Dictionary<string, List<string>> files = EditedFiles(root, edits);
        (ReadmeEdit[] made, ReadmeEdit[] inPlace) = MakeEdits(edits, files);
        (string From, string To)[] moves = JobFilesToMove(projectFile);
        (string From, string To)[] namespaces = MovedNamespaces(projectFile, moves);

        var written = new Dictionary<string, (string Text, bool Bom)>(StringComparer.Ordinal);
        foreach (string path in made.Select(edit => edit.Path).Distinct(StringComparer.Ordinal))
        {
            written[path] = (string.Join('\n', files[path]), HasBom(Path.Combine(root, path)));
        }

        string Renamed(string text) => RenamedNamespaces(text, namespaces);

        foreach ((string from, string to) in moves)
        {
            written[to] = (Renamed(File.ReadAllText(Path.Combine(root, from))), HasBom(Path.Combine(root, from)));
        }

        string[] leaving = [.. moves.Select(move => move.From)];
        foreach (string path in namespaces.Length == 0 ? [] : ProjectTextFiles(root).Where(path => !leaving.Contains(path, StringComparer.Ordinal)))
        {
            (string text, bool bom) = written.TryGetValue(path, out var pending) ? pending : (File.ReadAllText(Path.Combine(root, path)), HasBom(Path.Combine(root, path)));
            string renamed = Renamed(text);

            if (renamed != text || written.ContainsKey(path))
            {
                written[path] = (renamed, bom);
            }
        }

        written[readme] = (
            WithoutTheJoiningSection(written.TryGetValue(readme, out var renamedReadme) ? renamedReadme.Text : File.ReadAllText(Path.Combine(root, readme))),
            HasBom(Path.Combine(root, readme)));

        foreach (ReadmeEdit edit in inPlace)
        {
            Console.WriteLine($"In place already: {edit}");
        }

        string[] itemsOwn = [relative + "/", $"Tests/Presentation/{projectFile.FolderOf(_workerName + ".UnitTests")}/"];
        string[] touched = [.. leaving, .. written.Keys.Except(moves.Select(move => move.To), StringComparer.Ordinal)];
        if (touched.Length > 0)
        {
            RequireCommitted(root, touched, itemsOwn);
        }

        string trial = Directory.CreateTempSubdirectory("cleanarch-worker-").FullName;
        try
        {
            CopyTree(root, trial, folder => _notInTheProject.Contains(Path.GetFileName(folder), StringComparer.Ordinal));
            WriteTheJoin(trial, written, leaving);
            BuildTheJobsInTheWorker(projectFile, trial, Path.Combine(trial, Path.GetFileName(solution)), [.. moves.Select(move => move.To)]);
        }
        finally
        {
            Directory.Delete(trial, recursive: true);
        }

        WriteTheJoin(root, written, leaving);

        foreach ((string from, string to) in moves)
        {
            Console.WriteLine($"Moved: {from} to {to}");
        }

        foreach ((string from, string to) in namespaces)
        {
            Console.WriteLine($"Renamed: {from} to {to}");
        }

        foreach (ReadmeEdit edit in made)
        {
            Console.WriteLine($"Applied: {edit}");
        }

        Write(
            $"Joined {relative} to the project, the jobs with it. Run `dotnet run Tools/Tasks.cs presentation-scaffold {projectFile.ProjectName(_workerName)}` "
            + "next: it adds the worker and its tests to the solution.",
            ConsoleColor.Green);
    }

    private static string WithoutTheJoiningSection(string readme)
    {
        string[] lines = readme.Split('\n');
        int start = Array.FindIndex(lines, line => line.TrimEnd('\r') == _joiningSection);
        int next = Array.FindIndex(lines, start + 1, line => line.StartsWith("## ", StringComparison.Ordinal));

        return next < 0
            ? string.Join('\n', lines[..start]).TrimEnd() + "\n"
            : string.Join('\n', [.. lines[..start], .. lines[next..]]);
    }

    private static void WriteTheJoin(string root, Dictionary<string, (string Text, bool Bom)> written, string[] leaving)
    {
        foreach ((string path, (string text, bool bom)) in written)
        {
            string full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text, new UTF8Encoding(bom));
        }

        foreach (string path in leaving)
        {
            File.Delete(Path.Combine(root, path));
        }

        foreach (string folder in leaving.Select(path => Path.GetDirectoryName(Path.Combine(root, path))!).Distinct(StringComparer.Ordinal).OrderByDescending(folder => folder.Length))
        {
            for (string? empty = folder; empty is not null && Directory.Exists(empty) && !Directory.EnumerateFileSystemEntries(empty).Any(); empty = Path.GetDirectoryName(empty))
            {
                Directory.Delete(empty);
            }
        }
    }

    private static (string From, string To)[] JobFilesToMove(ProjectFile projectFile)
    {
        string root = projectFile.Root;
        string api = projectFile.ProjectFolder("Src", "Presentation", "Api");
        string apiTests = projectFile.ProjectFolder("Tests", "Presentation", "Api.UnitTests");
        string worker = projectFile.ProjectFolder("Src", "Presentation", _workerName);
        string workerTests = projectFile.ProjectFolder("Tests", "Presentation", _workerName + ".UnitTests");

        IEnumerable<(string From, string To)> Folder(string folder, string from, string to) =>
            Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .Select(file => (Relative(file), Relative(Path.Combine(to, Path.GetRelativePath(from, file)))))
                : [];

        IEnumerable<string> FeatureJobs(string project) =>
            Directory.Exists(Path.Combine(project, "Features"))
                ? Directory.EnumerateDirectories(Path.Combine(project, "Features")).Select(feature => Path.Combine(feature, "Jobs"))
                : [];

        string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

        (string From, string To)[] moves =
        [
            .. Folder(Path.Combine(api, "Common", "Jobs"), api, worker),
            .. FeatureJobs(api).SelectMany(folder => Folder(folder, api, worker)),
            .. FeatureJobs(apiTests).SelectMany(folder => Folder(folder, apiTests, workerTests)),
        ];

        string[] taken = [.. moves.Where(move => File.Exists(Path.Combine(root, move.To))).Select(move => move.To)];
        return taken.Length == 0
            ? moves
            : throw new InvalidOperationException(
                $"The worker holds these files already, which the API's jobs would replace, so no file was changed: {string.Join(", ", taken)}.");
    }

    private static (string From, string To)[] MovedNamespaces(ProjectFile projectFile, (string From, string To)[] moves)
    {
        string apiFolder = $"Src/Presentation/{projectFile.FolderOf("Api")}/";
        string apiTestsFolder = $"Tests/Presentation/{projectFile.FolderOf("Api.UnitTests")}/";

        string? Namespace(string from, string folder, string project) =>
            from.StartsWith(folder, StringComparison.Ordinal) && from[folder.Length..].Split('/') is var parts && Array.IndexOf(parts, "Jobs") is > 0 and int jobs
                ? $"{projectFile.Name}.{project}.{string.Join('.', parts[..(jobs + 1)])}"
                : null;

        return [.. moves
            .Select(move => Namespace(move.From, apiFolder, "Api") ?? Namespace(move.From, apiTestsFolder, "Api.UnitTests"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(space => (space, WorkerNamespace(projectFile.Name, space)))];
    }

    private static string WorkerNamespace(string product, string space) =>
        $"{product}.{_workerName}{space[$"{product}.Api".Length..]}";

    private static string RenamedNamespaces(string text, (string From, string To)[] namespaces) =>
        namespaces.Aggregate(text, (current, space) => Regex.Replace(
            current, $@"(?<![\w.]){Regex.Escape(space.From)}(?!\w)", _ => space.To, RegexOptions.None, TimeSpan.FromSeconds(5)));

    private static IEnumerable<string> ProjectTextFiles(string root) =>
        Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(path => !path.Split('/').SkipLast(1).Any(folder => _notInTheProject.Contains(folder, StringComparer.Ordinal)))
            .Where(path => !File.ReadAllBytes(Path.Combine(root, path)).Contains((byte)0));

    private static bool HasBom(string path) => File.ReadAllBytes(path) is [0xEF, 0xBB, 0xBF, ..];

    private static void RequireCommitted(string root, string[] paths, string[] newFolders)
    {
        (int inside, _) = Capture(root, echo: false, "git", "rev-parse", "--is-inside-work-tree");
        if (inside != 0)
        {
            throw new InvalidOperationException(
                "presentation-join moves the API's jobs into the worker, which only `git restore` undoes, and this project is no git repository, so no file was changed: "
                + "run `git init`, commit the project, then run the task again.");
        }

        (int exit, string output) = Capture(root, echo: false, "git", ["status", "--porcelain=v1", "--untracked-files=all", "--", .. paths]);
        string[] changed = [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !(line.StartsWith("?? ", StringComparison.Ordinal) && newFolders.Any(folder => line[3..].StartsWith(folder, StringComparison.Ordinal))))];

        if (exit != 0 || changed.Length > 0)
        {
            throw new InvalidOperationException(
                "presentation-join moves the API's jobs into the worker, which only `git restore` undoes, and these files it would move, edit or remove differ from "
                + "what git last committed, so no file was changed: commit or restore them, then run the task again."
                + Environment.NewLine + (exit != 0 ? output.TrimEnd() : string.Join(Environment.NewLine, changed)));
        }
    }

    private static readonly Regex _declaredType = new(
        @"^\s*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|file)\s+)*(?:class|record|struct|interface|enum)\s+(?<type>[A-Za-z_]\w*)",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(2));

    private static void BuildTheJobsInTheWorker(ProjectFile projectFile, string trial, string solution, string[] moved)
    {
        string workerTests = Path.Combine(
            trial, "Tests", "Presentation", projectFile.FolderOf(_workerName + ".UnitTests"), projectFile.ProjectName(_workerName + ".UnitTests") + ".csproj");

        foreach (string target in (string[])[solution, workerTests])
        {
            (int exit, string output) = Capture(trial, echo: true, "dotnet", "build", target);
            if (exit == 0)
            {
                continue;
            }

            string[] apisTypes = [.. Directory.EnumerateFiles(projectFile.ProjectFolder("Src", "Presentation", "Api").Replace(projectFile.Root, trial, StringComparison.Ordinal), "*.cs", SearchOption.AllDirectories)
                .SelectMany(file => _declaredType.Matches(File.ReadAllText(file)).Select(match => match.Groups["type"].Value))
                .Distinct(StringComparer.Ordinal)];
            string[] missing = [.. _unreachable.Matches(output)
                .Select(match => (File: Path.GetRelativePath(trial, match.Groups["file"].Value.Trim()).Replace('\\', '/'), match))
                .Select(found => $"{found.File} names "
                    + (found.match.Groups["space"].Success ? $"{found.match.Groups["space"].Value}.{found.match.Groups["name"].Value}" : found.match.Groups["name"].Value)
                    + (moved.Contains(found.File, StringComparer.Ordinal) && NamedApiTypes(Path.Combine(trial, found.File), apisTypes) is { Length: > 0 } named
                        ? $", and with it {string.Join(", ", named)}"
                        : string.Empty))
                .Distinct(StringComparer.Ordinal)];

            throw new InvalidOperationException(
                missing.Length > 0
                    ? "Built in a copy of the project with the jobs in the worker, these name what the worker cannot reach, a type or a namespace only the API declares, "
                      + "so no file was changed: " + string.Join("; ", missing) + ". Move each such type to the application layer, or into the job's own folder, "
                      + "commit, then run the task again."
                    : $"Built in a copy of the project with the jobs in the worker, {Path.GetFileName(target)} does not build, so no file was changed:"
                      + Environment.NewLine + output.TrimEnd());
        }
    }

    private static string[] NamedApiTypes(string file, string[] apisTypes)
    {
        string code = File.ReadAllText(file);

        return [.. apisTypes.Where(type => Regex.IsMatch(code, $@"\b{Regex.Escape(type)}\b", RegexOptions.None, TimeSpan.FromSeconds(2))).Order(StringComparer.Ordinal)];
    }

    private static void WebJoin(ProjectFile projectFile, string name, Dictionary<string, string> options)
    {
        string package = Path.Combine(projectFile.Root, "Src", "Web", "packages", name);
        string app = Path.Combine(projectFile.Root, "Src", "Web", "apps", name);

        if (!Directory.Exists(app) && Directory.Exists(package))
        {
            if (options.Count > 0)
            {
                throw new InvalidOperationException($"Src/Web/packages/{name} is a package: {string.Join(", ", options.Keys)} belong to an app.");
            }

            projectFile.RecordWebPackage(name, WorkspaceScope(projectFile.Root));
            return;
        }

        string framework = options.GetValueOrDefault("--web")
            ?? throw new InvalidOperationException(
                $"The 'web-join' task records the app in {ProjectFile.FileName} with the --framework cleanarch-webapp was given: "
                + $"dotnet run Tools/Tasks.cs web-join {name} --web <framework>, with --web-audience and --web-rendering when its "
                + "--audience and --rendering were not public and spa, and --web-port when it was given a --port.");
        string audience = options.GetValueOrDefault("--web-audience", "public");
        string rendering = options.GetValueOrDefault("--web-rendering", "spa");

        if (audience is not ("public" or "admin") || rendering is not ("spa" or "ssr"))
        {
            throw new InvalidOperationException($"--web-audience is public or admin, and --web-rendering spa or ssr; not '{audience}' and '{rendering}'.");
        }

        Join(projectFile.Root, "Src/Web/apps/" + name, "cleanarch-webapp", "web-scaffold", required: false, FirstAppsWorkspaceReadme(projectFile));
        PlaceTheServerRenderingDecision(projectFile.Root, "Src/Web/apps/" + name);
        DocsIndex(projectFile.Root, check: false);
        //#if (api)
        PrintApiSteps(projectFile);
        //#endif
        projectFile.RecordWebApp(name, framework, audience, rendering, WebAppPort(app, name, options.GetValueOrDefault("--web-port")), WorkspaceScope(projectFile.Root));
        RecordClient(projectFile);
    }

    private static int WebAppPort(string app, string name, string? given)
    {
        string compose = Path.Combine(app, "compose.yml");
        Match held = File.Exists(compose)
            ? Regex.Match(File.ReadAllText(compose), @"_HOST_PORT:-(?<port>\d+)\}", RegexOptions.None, TimeSpan.FromSeconds(5))
            : Match.Empty;

        if (!held.Success)
        {
            throw new InvalidOperationException($"Src/Web/apps/{name}/compose.yml holds no WEB_<NAME>_HOST_PORT default, so the app's port is not known.");
        }

        int port = int.Parse(held.Groups["port"].Value, CultureInfo.InvariantCulture);

        if (given is not null && (!int.TryParse(given, NumberStyles.None, CultureInfo.InvariantCulture, out int asked) || asked != port))
        {
            throw new InvalidOperationException($"--web-port {given} is not the port Src/Web/apps/{name}/compose.yml holds, {port}: give cleanarch-webapp's --port.");
        }

        return port;
    }

    private const string _serverRenderingDecision = "0024-a-server-rendered-web-app-runs-on-its-own-server.md";

    private static void PlaceTheServerRenderingDecision(string repoRoot, string app)
    {
        string brought = Path.Combine(repoRoot, app, _serverRenderingDecision);
        string decision = Path.Combine(repoRoot, "docs", "decisions", _serverRenderingDecision);

        if (!File.Exists(brought))
        {
            return;
        }

        if (File.Exists(decision))
        {
            File.Delete(brought);
            Console.WriteLine($"In place already: docs/decisions/{_serverRenderingDecision}");
            return;
        }

        File.Move(brought, decision);
        Console.WriteLine($"Moved: {app}/{_serverRenderingDecision} to docs/decisions/{_serverRenderingDecision}");
    }

    private static string? FirstAppsWorkspaceReadme(ProjectFile projectFile)
    {
        const string workspaceReadme = "Src/Web/README.md";
        string path = Path.Combine(projectFile.Root, workspaceReadme);

        return !projectFile.HasWebWorkspace && File.Exists(path) && File.ReadAllLines(path).Contains(_joiningSection, StringComparer.Ordinal)
            ? workspaceReadme
            : null;
    }

    private static void RecordClient(ProjectFile projectFile)
    {
        if (HoldsTheClient(projectFile))
        {
            projectFile.RecordClient();
        }
    }

    private static string WorkspaceScope(string repoRoot)
    {
        string manifest = Path.Combine(repoRoot, "Src", "Web", "package.json");
        string packageName = File.Exists(manifest) ? ReadJson(manifest)["name"]?.GetValue<string>() ?? string.Empty : string.Empty;
        int slash = packageName.IndexOf('/', StringComparison.Ordinal);

        return packageName.StartsWith('@') && slash > 1
            ? packageName[1..slash]
            : throw new InvalidOperationException("Src/Web/package.json names no @<scope>/ package, so the workspace's scope cannot be recorded.");
    }

    private static void Join(string repoRoot, string folder, string itemTemplate, string scaffoldTask, bool required, string? workspaceReadme = null)
    {
        string relative = $"{folder}/README.md";
        string readme = Path.Combine(repoRoot, relative);

        if (!Directory.Exists(Path.Combine(repoRoot, folder)) || (required && !File.Exists(readme)))
        {
            throw new InvalidOperationException($"{relative} does not exist: name the folder {itemTemplate} wrote under {Path.GetDirectoryName(folder)!.Replace('\\', '/')}.");
        }

        string[] text = File.Exists(readme) ? File.ReadAllLines(readme) : [];
        var edits = new List<ReadmeEdit>();

        if (workspaceReadme is not null)
        {
            edits.AddRange(ReadmeEdits(File.ReadAllLines(Path.Combine(repoRoot, workspaceReadme)), workspaceReadme));
        }

        if (required || text.Contains(_joiningSection, StringComparer.Ordinal))
        {
            edits.AddRange(ReadmeEdits(text, relative));
        }

        if (edits.Count == 0)
        {
            Write($"{folder} lists no edit outside its folder.", ConsoleColor.Green);
            return;
        }

        string listed = string.Join(" and ", edits.Select(edit => edit.Readme).Distinct(StringComparer.Ordinal));
        Dictionary<string, List<string>> files = EditedFiles(repoRoot, edits);
        (ReadmeEdit[] made, ReadmeEdit[] inPlace) = MakeEdits(edits, files);

        foreach (ReadmeEdit edit in inPlace)
        {
            Console.WriteLine($"In place already: {edit}");
        }

        if (made.Length == 0)
        {
            Write($"Every edit {listed} lists is in place already, so no file was changed.", ConsoleColor.Green);
            return;
        }

        foreach (string path in made.Select(edit => edit.Path).Distinct(StringComparer.Ordinal))
        {
            WriteLines(repoRoot, path, files[path]);
        }

        foreach (ReadmeEdit edit in made)
        {
            Console.WriteLine($"Applied: {edit}");
        }

        Write(
            $"Joined {folder} to the project. Run `dotnet run Tools/Tasks.cs {scaffoldTask} {Path.GetFileName(folder)}` "
            + "next: it creates the pages and adds the project to the solution.",
            ConsoleColor.Green);
    }

    private static Dictionary<string, List<string>> EditedFiles(string repoRoot, IEnumerable<ReadmeEdit> edits)
    {
        var files = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (ReadmeEdit edit in edits.Where(edit => !files.ContainsKey(edit.Path)))
        {
            string full = Path.Combine(repoRoot, edit.Path);

            if (!File.Exists(full))
            {
                throw new InvalidOperationException($"{edit}: {edit.Path} does not exist, so no file was changed.");
            }

            files[edit.Path] = [.. File.ReadAllText(full).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')];
        }

        return files;
    }

    private static void WriteLines(string repoRoot, string path, List<string> lines)
    {
        string full = Path.Combine(repoRoot, path);
        bool bom = File.Exists(full) && File.ReadAllBytes(full) is [0xEF, 0xBB, 0xBF, ..];
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, string.Join('\n', lines), new UTF8Encoding(bom));
    }

    private static (ReadmeEdit[] Made, ReadmeEdit[] InPlace) MakeEdits(IEnumerable<ReadmeEdit> edits, Dictionary<string, List<string>> files)
    {
        var made = new List<ReadmeEdit>();
        var inPlace = new List<ReadmeEdit>();

        foreach (ReadmeEdit edit in edits)
        {
            if (InPlace(edit, files[edit.Path]))
            {
                inPlace.Add(edit);
            }
            else
            {
                Apply(edit, files[edit.Path]);
                made.Add(edit);
            }
        }

        return ([.. made], [.. inPlace]);
    }

    private static List<ReadmeEdit> ReadmeEdits(string[] readme, string relative)
    {
        int start = Array.IndexOf(readme, _joiningSection);

        if (start < 0)
        {
            throw new InvalidOperationException($"{relative} has no '{_joiningSection}' section, so there is nothing to join.");
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

            Match bullet = _joiningEdit.Match(header);
            if (!bullet.Success)
            {
                throw new InvalidOperationException($"{relative}: '{header}' names no file and no 'after', 'before', 'replace' or 'remove'.");
            }

            string mode = bullet.Groups["mode"].Value;
            List<string> anchor = Fence(readme, ref index, relative);
            List<string> lines = mode == "remove" ? [] : Fence(readme, ref index, relative);
            edits.Add(new ReadmeEdit(relative, edits.Count + 1, bullet.Groups["path"].Value, mode, anchor, lines));
        }

        if (edits.Count == 0)
        {
            throw new InvalidOperationException($"{relative}: '{_joiningSection}' lists no edit in the form this task reads.");
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
                $"{edit}: the lines it is placed by occur {found.Length} times in {edit.Path}, not once, so no file was changed:\n{string.Join('\n', edit.Anchor)}");
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

    private sealed record ReadmeEdit(string Readme, int Number, string Path, string Mode, List<string> Anchor, List<string> Lines)
    {
        public override string ToString() => $"edit {Number} of {Readme}, {Mode} {Anchor.Count} line(s) of {Path}";
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
                .Where(property => !_generatedPropertiesTheBuildSets.Contains(property, StringComparer.Ordinal))
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

                if (declared.StartsWith("workspace:", StringComparison.Ordinal) || _exactVersion.IsMatch(declared))
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
            File.WriteAllText(manifest, package.ToJsonString(_writtenJson) + "\n");
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

    private static void PinTheProjectsTool(string repoRoot, ProjectFile projectFile)
    {
        string manifest = Path.Combine(repoRoot, ".config", "dotnet-tools.json");
        bool pinned = File.Exists(manifest) && ReadJson(manifest)["tools"]?[_projectToolPackage.ToLowerInvariant()] is not null;
        if (pinned || IsTheTemplatesCheckout(repoRoot))
        {
            return;
        }

        Step("dotnet", "tool", "install", _projectToolPackage, "--local", "--version", projectFile.TemplateVersion);
    }

    private const string _projectToolPackage = "CleanArch.Cli";

    private static bool IsTheTemplatesCheckout(string repoRoot) =>
        File.Exists(Path.Combine(repoRoot, ".template.config", "template.json"));

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

    private static string Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    //#endif
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

    private static string Gate(string repoRoot, string fileName) => Path.Combine(repoRoot, "Tools", fileName);

    private const string _initialMigrationName = "InitialCreate";

    private static readonly string[] _maxCpuCountPrefixes = ["-m:", "-maxcpucount:"];

    private static readonly string[] _generatorTasks = ["new-feature", "new-field", "new-use-case", "new-setting", "new-event"];

    private static readonly Dictionary<string, string[]> _generatorOptions = new(StringComparer.Ordinal)
    {
        ["--access"] = ["new-feature"],
        ["--deletion"] = ["new-feature"],
        ["--operations"] = ["new-feature"],
        ["--model"] = ["new-feature"],
        ["--concurrency"] = ["new-feature"],
        ["--fields"] = ["new-feature", "new-event"],
        ["--kind"] = ["new-use-case"],
        ["--input"] = ["new-use-case"],
        ["--keys"] = ["new-setting"],
        ["--consumer"] = ["new-event"],
        ["--unconsumed"] = ["new-event"],
        ["--dry-run"] = _generatorTasks,
    };

    private static readonly string[] _generatorSwitches = ["--dry-run"];

    private static IEnumerable<string> GeneratorOptionsOf(string generator) =>
        _generatorOptions.Where(pair => pair.Value.Contains(generator, StringComparer.Ordinal)).Select(pair => pair.Key);

    private static string? GeneratorOptionRefusal(string? task, IEnumerable<string> options) =>
        options
            .Where(option => !_generatorOptions[option].Contains(task, StringComparer.Ordinal))
            .Select(option => _generatorOptions[option] is var owners && owners.Length == 1
                ? $"'{option}' is an option of the {owners[0]} task, not of '{task ?? "(no task)"}'."
                : $"'{option}' is an option of the {string.Join(", ", owners[..^1])} and {owners[^1]} tasks, not of '{task ?? "(no task)"}'.")
            .FirstOrDefault();

    private static readonly string[] _webJoinOptionNames = ["--web", "--web-audience", "--web-rendering", "--web-port"];

    private static readonly string[] _upgradeOptionNames = ["--to", "--source", "--json", "--keep"];

    private static string TaskHelp(string task, ProjectFile projectFile)
    {
        if (!_knownTasks.Contains(task, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{task}' does not belong to the set of tasks. Supply one of these and then try the command again:"
                + Environment.NewLine + TaskList(projectFile));
        }

        StringBuilder everything = new StringBuilder(_usage.Replace("<tasks>", TaskList(projectFile), StringComparison.Ordinal))
            .AppendLine().AppendLine(_webUsage);
        //#if (api)
        everything.AppendLine(_apiUsage).AppendLine(_clientUsage);
        //#endif
        //#if (templateMaintenance)
        everything.AppendLine(_maintenanceUsage);
        //#endif

        var word = new Regex($@"(?<![\w-]){Regex.Escape(task)}(?![\w-])");
        List<string> shown = [];
        List<string> entry = [];
        void Flush()
        {
            if (entry.Count > 0 && (entry[0].Trim().Split(' ')[0] == task || (entry[0].TrimStart().StartsWith('-') && word.IsMatch(string.Join(' ', entry)))))
            {
                shown.AddRange(entry);
            }

            entry.Clear();
        }

        foreach (string line in everything.ToString().ReplaceLineEndings("\n").Split('\n'))
        {
            bool starts = line.StartsWith("  ", StringComparison.Ordinal) && !line.StartsWith("   ", StringComparison.Ordinal);
            if (starts || !line.StartsWith("   ", StringComparison.Ordinal))
            {
                Flush();
            }

            if (starts || (entry.Count > 0 && line.StartsWith("   ", StringComparison.Ordinal)))
            {
                entry.Add(line);
            }
        }

        Flush();

        string usage = $"Usage: dotnet run Tools/Tasks.cs {task} [<name> [<second>]] [options]";
        string body = shown.Count > 0
            ? string.Join(Environment.NewLine, shown)
            : "  Runs the dotnet command it prints before running it.";
        return usage + Environment.NewLine + Environment.NewLine + body;
    }

    private static string TaskList(ProjectFile projectFile) =>
        "  " + string.Join(", ", _knownTasks.Where(task => task != "sonar" || projectFile.Sonar));

    private static string RequiredSecond(string task, string? second)
    {
        if (string.IsNullOrWhiteSpace(second))
        {
            throw new InvalidOperationException(
                $"The '{task}' task needs two names and --access, e.g. dotnet run Tools/Tasks.cs {task} Widgets Widget --access shared");
        }

        return second;
    }

    private static string RequiredPair(string task, string? first, string? second, string example)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            throw new InvalidOperationException(
                $"The '{task}' task needs two names and its options, e.g. dotnet run Tools/Tasks.cs {task} {example}");
        }

        return first;
    }

    private static string RequiredFields(string task, string? features, string? fields)
    {
        if (string.IsNullOrWhiteSpace(features) || string.IsNullOrWhiteSpace(fields))
        {
            throw new InvalidOperationException(
                $"The '{task}' task needs the feature and the fields to add, each that cannot be null with its default, "
                + $"e.g. dotnet run Tools/Tasks.cs {task} Widgets \"Budget:decimal(12,2)>=0=0, Notes:text?\"");
        }

        return features;
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
                $"The '{task}' task needs the presentation project's name, e.g. dotnet run Tools/Tasks.cs {task} AppTemplate.Portal");
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
                "dotnet", "ef", "migrations", "add", _initialMigrationName,
                "--project", context,
                "--startup-project", context);
        }
    }

    private static readonly Regex _emptyMigrationMethod = new(
        @"void\s+(?:Up|Down)\s*\(\s*MigrationBuilder\s+\w+\s*\)\s*\{\s*\}",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static string MigrationContext(string? requested, bool localAuth, string persistence, string auth)
    {
        if (requested is null)
        {
            return persistence;
        }

        if (requested != "auth")
        {
            throw new InvalidOperationException(
                $"'{requested}' is not a context of this project. Leave --context out for the business context, or give auth for the identity context.");
        }

        return localAuth
            ? auth
            : throw new InvalidOperationException(
                "--context auth targets the identity context, which only a project generated with --auth local holds; "
                + "this one keeps no accounts of its own. Leave --context out for the business context.");
    }

    internal static bool IsEmptyMigration(string source) =>
        _emptyMigrationMethod.Count(source) == 2;

    private static void AddMigration(string context, string name, bool noBuild)
    {
        string folder = Path.Combine(context, "Migrations");
        string snapshotPath = Directory.EnumerateFiles(folder, "*ModelSnapshot.cs").Single();
        byte[] snapshot = File.ReadAllBytes(snapshotPath);
        HashSet<string> before = [.. Directory.EnumerateFiles(folder, "*.cs")];

        Step(
            "dotnet",
            [
                "ef", "migrations", "add", name,
                "--project", context,
                "--startup-project", context,
                .. noBuild ? ["--no-build"] : Array.Empty<string>(),
            ]);

        string[] written = [.. Directory.EnumerateFiles(folder, "*.cs").Where(file => !before.Contains(file))];
        string? migration = written.SingleOrDefault(file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal));

        if (migration is null || !IsEmptyMigration(File.ReadAllText(migration)))
        {
            return;
        }

        foreach (string file in written)
        {
            File.Delete(file);
        }

        File.WriteAllBytes(snapshotPath, snapshot);
        throw new InvalidOperationException(
            $"The model of {Path.GetFileName(context)} has not changed since its last migration; nothing is written. "
            + "A new entity needs its schema constant, its set and its ApplyConfiguration call in the context (docs/guides/migrations.md).");
    }

    private static int SelfTest()
    {
        const string empty = """
            public partial class Nothing : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                {
                }

                protected override void Down(MigrationBuilder migrationBuilder)
                {
                }
            }
            """;
        const string addsAColumn = """
            public partial class AddX : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                {
                    migrationBuilder.AddColumn<string>(name: "X", table: "Notes", nullable: true);
                }

                protected override void Down(MigrationBuilder migrationBuilder)
                {
                }
            }
            """;
        const string dropsOnly = """
            public partial class DropX : Migration
            {
                protected override void Up(MigrationBuilder migrationBuilder)
                {
                }

                protected override void Down(MigrationBuilder migrationBuilder)
                {
                    migrationBuilder.DropColumn(name: "X", table: "Notes");
                }
            }
            """;
        string windowsEmpty = empty.Replace("\n", "\r\n", StringComparison.Ordinal);

        (string Case, bool Passed)[] cases =
        [
            ("an empty Up and Down is empty", IsEmptyMigration(empty)),
            ("an empty migration with CRLF line ends is empty", IsEmptyMigration(windowsEmpty)),
            ("a migration with an operation in Up is not empty", !IsEmptyMigration(addsAColumn)),
            ("a migration with an operation in Down only is not empty", !IsEmptyMigration(dropsOnly)),
            ("no --context targets the business context", MigrationContext(null, true, "persistence", "auth") == "persistence"),
            ("--context auth targets the identity context", MigrationContext("auth", true, "persistence", "auth") == "auth"),
            ("--context auth is refused where there is no identity context", Refused(() => MigrationContext("auth", false, "persistence", "auth"), "--auth local")),
            ("an unknown context is refused", Refused(() => MigrationContext("billing", true, "persistence", "auth"), "not a context")),
            .. CheckCases(),
            .. SkipCases(),
            .. GeneratorOptionCases(),
            .. JoinCases(),
            .. DocsIndexCases(),
            .. WebAppRecordCases(),
            .. SolutionFolderCases(),
            ("verify refuses a web workspace without pnpm, saying how to get it", Refused(() => RequirePnpm("pnpm-not-on-this-path"), "run `corepack enable` once")),
            ("format-fix drops a Markdown file's BOM and turns each CR ending into LF", NormalisedMarkdown([0xEF, 0xBB, 0xBF, (byte)'a', (byte)'\r', (byte)'\n', (byte)'b', (byte)'\r']).SequenceEqual("a\nb\n"u8.ToArray())),
            ("format-fix leaves a Markdown file without a BOM or a CR as it is", NormalisedMarkdown("# \u00e9\n"u8.ToArray()).SequenceEqual("# \u00e9\n"u8.ToArray())),
        ];

        foreach ((string label, bool passed) in cases)
        {
            Write((passed ? "ok     " : "FAILED ") + label, passed ? ConsoleColor.Green : ConsoleColor.Red);
        }

        return cases.All(result => result.Passed) ? 0 : 1;
    }

    private static (string Case, bool Passed)[] SolutionFolderCases()
    {
        string root = Path.Combine(Path.GetTempPath(), "repo");
        string[][] added = SolutionAddArguments(
            root,
            "X.sln",
            [
                Path.Combine(root, "Src", "Presentation", "X.Desktop", "App.Desktop.csproj"),
                Path.Combine(root, "Tests", "Presentation", "X.Desktop.UnitTests", "App.Desktop.UnitTests.csproj"),
                Path.Combine(root, "Src", "Clients", "X.Client", "App.Client.csproj"),
            ]);

        return
        [
            ("a scaffolded project joins the solution folder of its layer, one call per folder", added.Length == 3
                && added[0].SequenceEqual(["sln", "X.sln", "add", "--solution-folder", "Src/Presentation", Path.Combine(root, "Src", "Presentation", "X.Desktop", "App.Desktop.csproj")])
                && added[1][4] == "Tests/Presentation"
                && added[2][4] == "Src/Clients"),
        ];
    }

    private static (string Case, bool Passed)[] GeneratorOptionCases() =>
    [
        ("--dry-run reaches every generator", _generatorTasks.All(generator => GeneratorOptionRefusal(generator, ["--dry-run"]) is null)),
        ("--fields reaches new-feature and new-event", GeneratorOptionRefusal("new-feature", ["--fields"]) is null && GeneratorOptionRefusal("new-event", ["--fields"]) is null),
        ("--fields is refused for new-setting", GeneratorOptionRefusal("new-setting", ["--fields"]) == "'--fields' is an option of the new-feature and new-event tasks, not of 'new-setting'."),
        ("--keys is refused for new-event", GeneratorOptionRefusal("new-event", ["--keys"]) == "'--keys' is an option of the new-setting task, not of 'new-event'."),
        ("--dry-run is refused outside the generators", GeneratorOptionRefusal("build", ["--dry-run"]) == "'--dry-run' is an option of the new-feature, new-field, new-use-case, new-setting and new-event tasks, not of 'build'."),
    ];

    private static (string Case, bool Passed)[] CheckCases()
    {
        ProjectNode[] graph =
        [
            new("Src/Domain/P.Domain.Core/X.Domain.Core.csproj", "Src/Domain/P.Domain.Core", [], false),
            new("Src/Domain/P.Domain/X.Domain.csproj", "Src/Domain/P.Domain", ["Src/Domain/P.Domain.Core/X.Domain.Core.csproj"], false),
            new("Src/Application/P.Application/X.Application.csproj", "Src/Application/P.Application", ["Src/Domain/P.Domain/X.Domain.csproj"], false),
            new("Src/Infrastructure/P.Infrastructure/X.Infrastructure.csproj", "Src/Infrastructure/P.Infrastructure", ["Src/Application/P.Application/X.Application.csproj"], false),
            new("Tests/Domain/P.Domain.UnitTests/X.Domain.UnitTests.csproj", "Tests/Domain/P.Domain.UnitTests", ["Src/Domain/P.Domain/X.Domain.csproj"], true),
            new("Tests/Domain/P.Domain.Core.UnitTests/X.Domain.Core.UnitTests.csproj", "Tests/Domain/P.Domain.Core.UnitTests", ["Src/Domain/P.Domain.Core/X.Domain.Core.csproj"], true),
            new("Tests/Integration/P.Integration/X.Integration.csproj", "Tests/Integration/P.Integration", ["Src/Infrastructure/P.Infrastructure/X.Infrastructure.csproj"], false),
        ];

        string[] Projects(params string[] changed) => [.. ScopeOf(changed, graph).Projects.Select(path => path[(path.LastIndexOf('/') + 1)..]).Order(StringComparer.Ordinal)];

        const string violations =
            "/r/Src/A.cs(14,17): error IDE1006: Naming rule violation: Missing prefix: '_' [/r/Src/X.csproj]\n"
            + "/r/Src/A.cs(14,17): error IDE1006: Naming rule violation: Missing prefix: '_' [/r/Src/X.csproj]\r\n"
            + "/r/Src/B.cs(3,9): warning IDE0055: Fix formatting [/r/Src/X.csproj]\n";

        return
        [
            ("a file of a project touches it and every project that references it, however far", Projects("Src/Domain/P.Domain/Features/Notes/Note.cs").SequenceEqual(
                ["X.Application.csproj", "X.Domain.UnitTests.csproj", "X.Domain.csproj", "X.Infrastructure.csproj", "X.Integration.csproj"])),
            ("the nearest project owns a file, not the project whose folder name it starts with", Projects("Src/Domain/P.Domain.Core/Id.cs").SequenceEqual(
                ["X.Application.csproj", "X.Domain.Core.UnitTests.csproj", "X.Domain.Core.csproj", "X.Domain.UnitTests.csproj", "X.Domain.csproj", "X.Infrastructure.csproj", "X.Integration.csproj"])),
            ("a folder whose name merely starts with a project's folder name belongs to no project", Projects("Src/Domain/P.DomainExtras/Id.cs").Length == 0),
            ("a test project touches nothing but itself", Projects("Tests/Domain/P.Domain.UnitTests/NoteTests.cs").SequenceEqual(["X.Domain.UnitTests.csproj"])),
            ("a file outside every project touches none", Projects("docs/README.md", "Tools/Tasks.cs").Length == 0),
            ("a file is placed by its path alone, so a deleted one still touches its project", Projects("Src/Application/P.Application/Gone.cs").Length == 3),
            ("Directory.Build.props sets the build for every project", ScopeOf(["Directory.Build.props"], graph) is { BuildWide: true, Projects.Count: 7 }),
            ("a root .editorconfig sets the build for every project", ScopeOf(["Src/Domain/P.Domain/Note.cs", ".editorconfig"], graph).BuildWide),
            ("a project file sets the build for every project", ScopeOf(["Src/Domain/P.Domain/X.Domain.csproj"], graph).BuildWide),
            ("an .editorconfig above no project does not", !ScopeOf(["Launcher/.editorconfig"], graph).BuildWide),
            ("an .editorconfig inside a project does", ScopeOf(["Src/Domain/P.Domain/Generated/.editorconfig"], graph).BuildWide),
            ("a Markdown file in the root does not", !ScopeOf(["README.md"], graph).BuildWide),
            ("only C# files that exist and belong to a project are formatted", FormattableFiles(
                ["Src/Domain/P.Domain/B.cs", "Src/Domain/P.Domain/A.cs", "Src/Domain/P.Domain/Gone.cs", "Src/Domain/P.Domain/a.md", "Tools/Tasks.cs"],
                graph,
                file => !file.EndsWith("Gone.cs", StringComparison.Ordinal)).SequenceEqual(["Src/Domain/P.Domain/A.cs", "Src/Domain/P.Domain/B.cs"])),
            ("the base is the first candidate that exists", PickBase(candidate => candidate is "origin/main" or "develop") == "origin/main"),
            ("origin/develop comes before the remote's default branch", PickBase(candidate => candidate is "origin/develop" or "origin/HEAD") == "origin/develop"),
            ("no candidate leaves HEAD as the base", PickBase(_ => false) is null),
            ("a naming violation is named once with its file and line, relative to the root", NamingViolations("/r", violations).SequenceEqual(
                ["Src/A.cs:14: Naming rule violation: Missing prefix: '_'"])),
        ];
    }

    private static (string Case, bool Passed)[] SkipCases()
    {
        Dictionary<string, string> texts = new(StringComparer.Ordinal)
        {
            ["Tests/Presentation/P.Desktop.UnitTests/X.Desktop.UnitTests.csproj"] =
                "<Project><PropertyGroup><TargetFramework>net10.0-windows</TargetFramework></PropertyGroup></Project>",
            ["Tests/Presentation/P.Kiosk.UnitTests/X.Kiosk.UnitTests.csproj"] =
                "<Project><PropertyGroup>\n    <TargetFramework> net10.0-windows10.0.19041.0 </TargetFramework>\n  </PropertyGroup></Project>",
            ["Tests/Domain/P.Domain.UnitTests/X.Domain.UnitTests.csproj"] =
                "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>",
            ["Tests/Clients/P.Client.UnitTests/X.Client.UnitTests.csproj"] = "<Project><PropertyGroup /></Project>",
        };
        ProjectNode[] graph =
        [
            new("Src/Presentation/P.Desktop/X.Desktop.csproj", "Src/Presentation/P.Desktop", [], false, WindowsOnly: true),
            new("Tests/Presentation/P.Desktop.UnitTests/X.Desktop.UnitTests.csproj", "Tests/Presentation/P.Desktop.UnitTests", ["Src/Presentation/P.Desktop/X.Desktop.csproj"], true, WindowsOnly: true),
            new("Tests/Domain/P.Domain.UnitTests/X.Domain.UnitTests.csproj", "Tests/Domain/P.Domain.UnitTests", [], true),
            new("Tests/Architecture/P.Architecture.Tests/X.Architecture.Tests.csproj", "Tests/Architecture/P.Architecture.Tests", [], true),
        ];
        string[] built = [.. graph.Select(project => project.Path)];
        const string architecture = "Tests/Architecture/P.Architecture.Tests/X.Architecture.Tests.csproj";
        (string[] testedOff, string[] skippedOff) = TestedProjects(built, graph, architecture, onWindows: false);
        (string[] testedOn, string[] skippedOn) = TestedProjects(built, graph, architecture, onWindows: true);

        return
        [
            ("a test project on net10.0-windows runs on Windows alone", WindowsTargetFramework(texts["Tests/Presentation/P.Desktop.UnitTests/X.Desktop.UnitTests.csproj"]) == "net10.0-windows"),
            ("a Windows target framework with a platform version runs on Windows alone", WindowsTargetFramework(texts["Tests/Presentation/P.Kiosk.UnitTests/X.Kiosk.UnitTests.csproj"]) == "net10.0-windows10.0.19041.0"),
            ("a test project on net10.0, or on the framework the props set, runs everywhere",
                WindowsTargetFramework(texts["Tests/Domain/P.Domain.UnitTests/X.Domain.UnitTests.csproj"]) is null
                && WindowsTargetFramework(texts["Tests/Clients/P.Client.UnitTests/X.Client.UnitTests.csproj"]) is null),
            ("off Windows, the Windows-only test projects are the ones left out", WindowsOnly(texts.Keys, path => texts[path], onWindows: false).Order(StringComparer.Ordinal).SequenceEqual(
                ["Tests/Presentation/P.Desktop.UnitTests/X.Desktop.UnitTests.csproj", "Tests/Presentation/P.Kiosk.UnitTests/X.Kiosk.UnitTests.csproj"])),
            ("on Windows, no test project is left out", WindowsOnly(texts.Keys, path => texts[path], onWindows: true).Length == 0),
            ("off Windows, check runs the other tests and names the Windows-only one it leaves out",
                skippedOff.SequenceEqual(["Tests/Presentation/P.Desktop.UnitTests/X.Desktop.UnitTests.csproj"])
                && testedOff.Order(StringComparer.Ordinal).SequenceEqual([architecture, "Tests/Domain/P.Domain.UnitTests/X.Domain.UnitTests.csproj"])),
            ("on Windows, check runs the Windows-only test project with the others", skippedOn.Length == 0 && testedOn.Length == 3),
            ("a skip names the project's folder, its target framework and where it runs",
                SkipLine("Tests/Presentation/P.Desktop.UnitTests", "net10.0-windows")
                    == "Skipped Tests/Presentation/P.Desktop.UnitTests: it targets net10.0-windows and runs on Windows alone."),
        ];
    }

    private static (string Case, bool Passed)[] JoinCases()
    {
        ReadmeEdit Edit(int number, string mode, string[] anchor, string[] lines) => new("README.md", number, "file", mode, [.. anchor], [.. lines]);
        Dictionary<string, List<string>> File(params string[] lines) => new(StringComparer.Ordinal) { ["file"] = [.. lines] };

        Dictionary<string, List<string>> partly = File("include:", "  - path: a", "services:");
        (ReadmeEdit[] made, ReadmeEdit[] inPlace) = MakeEdits([Edit(1, "after", ["include:"], ["  - path: a"]), Edit(2, "before", ["services:"], ["  - path: b"])], partly);

        Dictionary<string, List<string>> chained = File("services:");
        MakeEdits([Edit(1, "before", ["services:"], ["include:"]), Edit(2, "after", ["include:"], ["  - path: a"])], chained);

        (string From, string To)[] toTheWorker = [("n.Api.Features.Invoices.Jobs", "n.Worker.Features.Invoices.Jobs")];

        return
        [
            ("a namespace the worker takes keeps what follows the API's, whatever the product's name holds",
                WorkerNamespace("n", "n.Api.Features.Region.Api.Jobs") == "n.Worker.Features.Region.Api.Jobs"
                && WorkerNamespace("n", "n.Api.UnitTests.Features.Invoices.Jobs") == "n.Worker.UnitTests.Features.Invoices.Jobs"),
            ("a namespace the worker takes is renamed where it stands whole, never inside a longer name",
                RenamedNamespaces("using n.Api.Features.Invoices.Jobs;\nusing n.Api.Features.Invoices.Jobs.Purge;\nusing Common.Api.Features.Invoices.Jobs;\nusing n.Api.Features.Invoices.JobsArchive;", toTheWorker)
                == "using n.Worker.Features.Invoices.Jobs;\nusing n.Worker.Features.Invoices.Jobs.Purge;\nusing Common.Api.Features.Invoices.Jobs;\nusing n.Api.Features.Invoices.JobsArchive;"),
            ("an edit in place already is left alone and the others are made", made is [{ Number: 2 }] && inPlace is [{ Number: 1 }]
                && partly["file"].SequenceEqual(["include:", "  - path: a", "  - path: b", "services:"])),
            ("an edit is placed by the lines an earlier edit made", chained["file"].SequenceEqual(["include:", "  - path: a", "services:"])),
            ("an edit that names no way to place it is refused, naming the ways", Refused(
                () => ReadmeEdits([_joiningSection, "", "- in `file`, at the rank of its number, add:", "", "  ```markdown", "  | x |", "  ```"], "README.md"),
                "names no file and no 'after', 'before', 'replace' or 'remove'")),
            ("an edit whose lines to place it by are missing is refused, naming it", Refused(
                () => MakeEdits([Edit(1, "after", ["services:"], ["x"]), Edit(2, "after", ["missing"], ["y"])], File("services:")),
                "edit 2 of README.md, after 1 line(s) of file: the lines it is placed by occur 0 times")),
        ];
    }

    private static (string Case, bool Passed)[] DocsIndexCases()
    {
        string root = Directory.CreateTempSubdirectory("docs-index-").FullName;
        TextWriter output = Console.Out;

        void Page(string path, string frontmatter)
        {
            string full = Path.Combine(root, _docsFolder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, $"---\n{frontmatter}\n---\n\n# Heading\n");
        }

        string Read(string path) => File.ReadAllText(Path.Combine(root, _docsFolder, path));

        bool Checked()
        {
            try
            {
                DocsIndex(root, check: true);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        try
        {
            Console.SetOut(TextWriter.Null);
            Page("README.md", "type: reference\ntitle: Documentation\ndescription: Which page answers what.");
            Page("guides/second.md", "type: guide\ntitle: B task\ndescription: \"How to do B: in one sentence.\"");
            Page("guides/first.md", "type: guide\ntitle: A task\ndescription: How to do A.");
            Page("decisions/README.md", "type: reference\ntitle: Decisions\ndescription: Why each choice was made.");
            Page("decisions/0002-b.md", "type: decision\ntitle: 0002. B\ndescription: Why B.\nstatus: Accepted");
            Page("decisions/0001-a.md", "type: decision\ntitle: 0001. A\ndescription: Why A.\nstatus: Superseded");
            Page("_page-templates/feature.md", "type: template\ntitle: <Feature>\ndescription: <description>");
            Page("plans/plan.md", "title: A plan");
            Directory.CreateDirectory(Path.Combine(root, "docs", "empty"));
            File.WriteAllText(Path.Combine(root, "docs", "empty", _indexName), "# Empty\n");
            bool refusedBeforeWriting = !Checked();

            DocsIndex(root, check: false);
            string guides = Read("guides/index.md");
            string decisions = Read("decisions/index.md");
            string top = Read("index.md");
            bool fresh = Checked();
            Page("guides/first.md", "type: guide\ntitle: A task\ndescription: How to do A, said otherwise.");
            bool staleRefused = !Checked();
            Page("guides/third.md", "type: guide\ntitle: C task");
            bool untitled = Refused(() => DocsIndex(root, check: false), $"{_docsFolder}/guides/third.md has no description");

            return
            [
                ("the index lists a folder's pages by title, with their descriptions", guides == "# Guides\n\n" + _indexNotice + "\n\n"
                    + "- [A task](first.md) - How to do A.\n- [B task](second.md) - How to do B: in one sentence.\n"),
                ("the index lists the folder's README first, then each decision by number with its status", decisions == "# Decisions\n\n" + _indexNotice + "\n\n"
                    + "- [Decisions](README.md) - Why each choice was made.\n- [0001. A](0001-a.md) - Superseded. Why A.\n- [0002. B](0002-b.md) - Accepted. Why B.\n"),
                ("the index of a folder links its subfolders' indexes, a folder without a README by its name", top == "# Documentation\n\n" + _indexNotice + "\n\n"
                    + "- [Documentation](README.md) - Which page answers what.\n\n## Folders\n\n"
                    + "- [Decisions](decisions/index.md) - Why each choice was made.\n- [Guides](guides/index.md)\n"),
                ("the index leaves out the plans, the folders named from _ and the folders without a page", !File.Exists(Path.Combine(root, "docs", "plans", _indexName))
                    && !File.Exists(Path.Combine(root, "docs", "_page-templates", _indexName)) && !File.Exists(Path.Combine(root, "docs", "empty", _indexName))),
                ("--check refuses an index missing, or kept for a folder without a page", refusedBeforeWriting),
                ("--check passes on the index the task wrote", fresh),
                ("--check refuses an index a page's frontmatter no longer matches", staleRefused),
                ("a page without a description is refused, naming it", untitled),
            ];
        }
        finally
        {
            Console.SetOut(output);
            Directory.Delete(root, recursive: true);
        }
    }

    private static (string Case, bool Passed)[] WebAppRecordCases()
    {
        string root = Directory.CreateTempSubdirectory("web-app-record-").FullName;
        TextWriter output = Console.Out;

        try
        {
            string path = Path.Combine(root, ProjectFile.FileName);
            File.WriteAllText(
                path,
                """{"template":{"package":"P","version":"1.0.0"},"name":"X","web":{"scope":"x","apps":[{"name":"Site","framework":"vite-react","audience":"public","rendering":"spa","owner":"kept"}],"packages":[]}}""");
            Console.SetOut(TextWriter.Null);
            ProjectFile.Read(root).RecordWebApp("Site", "vite-react", "public", "spa", 8083, "x");
            string recorded = File.ReadAllText(path);
            ProjectFile.Read(root).RecordWebApp("Site", "vite-react", "public", "spa", 8083, "x");
            bool unchanged = File.ReadAllText(path) == recorded;
            Console.SetOut(output);

            JsonObject app = ((JsonObject)JsonNode.Parse(recorded)!)["web"]!["apps"]![0]!.AsObject();
            string folder = Path.Combine(root, "Site");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "compose.yml"), "ports:\n  - \"127.0.0.1:${WEB_SITE_HOST_PORT:-8083}:8080\"\n");

            return
            [
                ("a port recorded for an app already recorded keeps its other keys and their order",
                    app.Select(property => property.Key).SequenceEqual(["name", "framework", "audience", "rendering", "owner", "port"]) && app["owner"]!.GetValue<string>() == "kept"),
                ("recording the same app and port again writes nothing", unchanged),
                ("the port is the one the app's compose.yml holds", WebAppPort(folder, "Site", null) == 8083 && WebAppPort(folder, "Site", "8083") == 8083),
                ("a --web-port other than the one compose.yml holds is refused", Refused(() => WebAppPort(folder, "Site", "8085"), "is not the port")),
                ("an app without a compose.yml port is refused", Refused(() => WebAppPort(root, "Site", null), "holds no WEB_<NAME>_HOST_PORT")),
            ];
        }
        finally
        {
            Console.SetOut(output);
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool Refused(Action action, string saying)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException error)
        {
            return error.Message.Contains(saying, StringComparison.Ordinal);
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
            projects = projects.Where(path => IsDockerFreeTestProject(File.ReadAllText(path)));
        }

        string[] paths = [.. projects.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
        if (paths.Length == 0)
        {
            throw new InvalidOperationException(
                "Discovery found no test project under Tests/. A run over nothing is a false green.");
        }

        return paths;
    }

    private static bool IsDockerFreeTestProject(string projectText) =>
        projectText.Contains("<IsTestProject>true</IsTestProject>", StringComparison.Ordinal)
        && !projectText.Contains("Testcontainers", StringComparison.OrdinalIgnoreCase)
        && !projectText.Contains("Aspire.Hosting.Testing", StringComparison.OrdinalIgnoreCase);

    private static string DockerFreeSolutionFilter(string repoRoot, string solution)
    {
        string[] tests = TestProjects(repoRoot, dockerFreeOnly: true);

        return SolutionFilter(repoRoot, solution, "docker-free.slnf", tests.Except(SkippedHere(tests), StringComparer.Ordinal));
    }

    private static string RunnableSolutionFilter(string repoRoot, string solution, string[] skipped)
    {
        HashSet<string> left = [.. skipped.Select(Path.GetFullPath)];

        return SolutionFilter(
            repoRoot,
            solution,
            "runnable.slnf",
            SolutionProjects(repoRoot, solution)
                .Select(project => Path.GetFullPath(Path.Combine(repoRoot, project.Path)))
                .Where(project => !left.Contains(project)));
    }

    private static void DeleteFilter(string? filter)
    {
        if (filter is not null)
        {
            File.Delete(filter);
        }
    }

    private static readonly Regex _windowsTargetFramework = new(
        @"<TargetFramework>\s*(?<framework>net\d+(\.\d+)*-windows(\d+(\.\d+)*)?)\s*</TargetFramework>",
        RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private static string? WindowsTargetFramework(string projectText) =>
        _windowsTargetFramework.Match(projectText) is { Success: true } match ? match.Groups["framework"].Value : null;

    private static string[] WindowsOnly(IEnumerable<string> projects, Func<string, string> read, bool onWindows) =>
        onWindows ? [] : [.. projects.Where(project => WindowsTargetFramework(read(project)) is not null)];

    private static string[] SkippedHere(IEnumerable<string> testProjects) =>
        WindowsOnly(testProjects, File.ReadAllText, OperatingSystem.IsWindows());

    private static string SkipLine(string folder, string framework) =>
        $"Skipped {folder}: it targets {framework} and runs on Windows alone.";

    private static void RunSkipping(string repoRoot, string[] skipped, Action run)
    {
        void Announce()
        {
            foreach (string project in skipped)
            {
                string full = Path.GetFullPath(Path.Combine(repoRoot, project));
                string folder = Path.GetRelativePath(repoRoot, Path.GetDirectoryName(full)!).Replace('\\', '/');
                Write(SkipLine(folder, WindowsTargetFramework(File.ReadAllText(full))!), ConsoleColor.Yellow);
            }
        }

        Announce();
        try
        {
            run();
        }
        finally
        {
            Announce();
        }
    }

    private static string SolutionFilter(string repoRoot, string solution, string fileName, IEnumerable<string> projects)
    {
        string directory = Path.Combine(repoRoot, "artifacts", "test");
        Directory.CreateDirectory(directory);
        string filter = Path.Combine(directory, fileName);
        string solutionDirectory = Path.GetDirectoryName(solution) ?? repoRoot;

        var content = new JsonObject
        {
            ["solution"] = new JsonObject
            {
                ["path"] = Path.GetRelativePath(directory, solution).Replace('\\', '/'),
                ["projects"] = new JsonArray([.. projects
                    .Select(project => JsonValue.Create(Path.GetRelativePath(solutionDirectory, project).Replace('\\', '/')))]),
            },
        };
        File.WriteAllText(filter, content.ToJsonString(_writtenJson));

        return filter;
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
            if (File.Exists(Path.Combine(directory.FullName, ProjectFile.FileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            $"{ProjectFile.FileName} is in neither '{start}' nor any directory above it, so the repository root cannot "
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

    private static (int ExitCode, string Output) Capture(string? directory, bool echo, string executable, params string[] arguments)
    {
        if (echo)
        {
            Write("> " + executable + " " + string.Join(' ', arguments), ConsoleColor.Cyan);
            Console.Out.Flush();
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = directory ?? string.Empty,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"'{executable}' started no process.");
            Task<string> errors = process.StandardError.ReadToEndAsync();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return (process.ExitCode, output + errors.GetAwaiter().GetResult());
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException($"'{executable}' is not on PATH.");
        }
    }

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

internal sealed class ProjectFile
{
    internal const string FileName = "cleanarch.json";

    private static readonly string[] _keyOrder = ["template", "name", "options", "client", "web", "presentation"];

    private static readonly string[] _appKeyOrder = ["name", "framework", "audience", "rendering", "port"];

    private static readonly JsonSerializerOptions _written = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
    };

    private readonly JsonObject _json;

    private ProjectFile(string root, JsonObject json)
    {
        Root = root;
        _json = json;
    }

    internal string Root { get; }

    internal string Name => Text(_json, "name");

    internal string Initials => string.Concat(Name.Split('.').Select(SegmentInitials));

    internal string TemplateVersion =>
        _json["template"] is JsonObject template
            ? Text(template, "version")
            : throw new InvalidOperationException($"{FilePath} records no template.");

    internal bool LocalAuth => Option("auth") == "local";

    internal bool ExternalAuth => Option("auth") == "external";

    internal bool Files => Flag("files");

    internal bool Sonar => Flag("sonar");

    internal IReadOnlyList<string> Presentations =>
        _json["presentation"] is JsonArray presentations ? [.. presentations.OfType<JsonObject>().Select(entry => Text(entry, "name"))] : [];

    internal bool HasWebWorkspace => _json["web"] is JsonObject;

    internal bool HasWebApps => _json["web"]?["apps"] is JsonArray { Count: > 0 };

    internal bool RendersAWebAppOnTheServer =>
        _json["web"]?["apps"] is JsonArray apps
        && apps.OfType<JsonObject>().Any(app => app["rendering"]?.GetValue<string>() == "ssr");

    internal bool HasAWebAppOnWebAssembly =>
        _json["web"]?["apps"] is JsonArray apps
        && apps.OfType<JsonObject>().Any(app => app["framework"]?.GetValue<string>() == "blazor-wasm");

    private string FilePath => System.IO.Path.Combine(Root, FileName);

    internal string ProjectName(string part) => $"{Name}.{part}";

    internal string FolderOf(string part) => $"{Initials}.{part}";

    internal string FolderOfProject(string project) =>
        project.StartsWith(Name + ".", StringComparison.Ordinal)
            ? FolderOf(project[(Name.Length + 1)..])
            : throw new InvalidOperationException($"'{project}' is not a project of {Name}: its name starts with {Name}.");

    internal string ProjectFolder(string tree, string layer, string part) => System.IO.Path.Combine(Root, tree, layer, FolderOf(part));

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));

    internal static ProjectFile Read(string root)
    {
        string path = System.IO.Path.Combine(root, FileName);

        return JsonNode.Parse(File.ReadAllText(path)) is JsonObject json
            ? new ProjectFile(root, json)
            : throw new InvalidOperationException($"{path} holds no JSON object.");
    }

    internal void RecordClient()
    {
        if (_json["client"]?.GetValue<bool>() != true)
        {
            Place(_json, "client", JsonValue.Create(true), _keyOrder);
            Save("the .NET client");
        }
    }

    internal void RecordPresentation(string name, string framework)
    {
        if (_json["presentation"] is not JsonArray presentations)
        {
            presentations = [];
            Place(_json, "presentation", presentations, _keyOrder);
        }

        presentations.Add((JsonNode)new JsonObject { ["name"] = name, ["framework"] = framework });
        Save($"the presentation project {name}, of framework {framework}");
    }

    internal void RecordWebApp(string name, string framework, string audience, string rendering, int port, string scope)
    {
        JsonArray apps = WebList(scope, "apps");
        JsonObject? recorded = apps.OfType<JsonObject>().FirstOrDefault(entry => entry["name"]?.GetValue<string>() == name);
        var wanted = new (string Key, JsonNode Value)[]
        {
            ("name", JsonValue.Create(name)), ("framework", JsonValue.Create(framework)), ("audience", JsonValue.Create(audience)),
            ("rendering", JsonValue.Create(rendering)), ("port", JsonValue.Create(port)),
        };

        if (recorded is not null && wanted.All(entry => JsonNode.DeepEquals(recorded[entry.Key], entry.Value)))
        {
            Console.WriteLine($"{FileName} records the web app {name} already.");
            return;
        }

        if (recorded is null)
        {
            recorded = [];
            apps.Add((JsonNode)recorded);
        }

        foreach ((string key, JsonNode value) in wanted)
        {
            Place(recorded, key, value, _appKeyOrder);
        }

        Save($"the web app {name}, of framework {framework}, for the {audience} audience, rendered {(rendering == "ssr" ? "on the server" : "in the browser")}, on port {port}");
    }

    internal void RecordWebPackage(string name, string scope)
    {
        JsonArray packages = WebList(scope, "packages");

        if (packages.Any(entry => entry?.GetValue<string>() == name))
        {
            Console.WriteLine($"{FileName} records the web package {name} already.");
            return;
        }

        packages.Add((JsonNode)JsonValue.Create(name));
        Save($"the web package {name}");
    }

    private JsonArray WebList(string scope, string key)
    {
        if (_json["web"] is not JsonObject web)
        {
            web = new JsonObject { ["scope"] = scope, ["apps"] = new JsonArray(), ["packages"] = new JsonArray() };
            Place(_json, "web", web, _keyOrder);
        }

        if (web[key] is not JsonArray list)
        {
            list = [];
            Place(web, key, list, ["scope", "apps", "packages"]);
        }

        return list;
    }

    private static void Place(JsonObject json, string key, JsonNode? value, string[] order)
    {
        if (json.ContainsKey(key))
        {
            json[key] = value;
            return;
        }

        int index = order.Skip(Array.IndexOf(order, key) + 1)
            .Where(json.ContainsKey)
            .Select(json.IndexOf)
            .DefaultIfEmpty(json.Count)
            .First();
        json.Insert(index, key, value);
    }

    private void Save(string recorded)
    {
        File.WriteAllText(FilePath, _json.ToJsonString(_written) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"{FileName} records {recorded} now.");
    }

    private string Option(string key) =>
        _json["options"]?[key]?.GetValue<string>()
        ?? throw new InvalidOperationException($"{FilePath} records no options.{key}.");

    private bool Flag(string key) =>
        _json["options"]?[key]?.GetValue<bool>()
        ?? throw new InvalidOperationException($"{FilePath} records no options.{key}.");

    private static string Text(JsonObject json, string key) =>
        json[key]?.GetValue<string>() is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"'{key}' is missing from {FileName}.");
}
