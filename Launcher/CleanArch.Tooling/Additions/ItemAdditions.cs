using System.Text.RegularExpressions;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Markdown;
using CleanArch.Tooling.Output;
using CleanArch.Tooling.Processes;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;
using Spectre.Console;

namespace CleanArch.Tooling.Additions;

public sealed record AdditionArguments(
    string? Name,
    IReadOnlyDictionary<string, string> TemplateOptions,
    string? TemplateSource,
    string? Hive);

public sealed record AdditionContext(
    Questionnaire Questionnaire,
    TemplateSource Source,
    TemplateEngine Engine,
    IProcessRunner Processes,
    IAnsiConsole Console,
    AdditionArguments Arguments,
    string? Version,
    string Invocation,
    bool DryRun = false);

public sealed partial class ItemAdditions
{
    public const string PresentationJoinTask = "presentation-join";
    public const string WebJoinTask = "web-join";
    public const string PresentationScaffoldTask = "presentation-scaffold";
    public const string JoiningSection = "## Joining a project generated without one";
    public const string PresentationCreateSection = "## Create the project";

    public static readonly IReadOnlyDictionary<string, string> PresentationNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["blazor-server"] = "Portal",
        ["mcp"] = "Mcp",
        ["avalonia"] = "Desktop",
        ["wpf"] = "Desktop",
        ["worker"] = "Worker",
    };

    private const string _nameKey = "--name";
    private const string _reviewKey = "--review";
    private const string _frameworkSymbol = "framework";
    private const string _itemNameSymbol = "name";
    private const string _dotnet = "dotnet";

    private static readonly IReadOnlyDictionary<string, string> _nothing = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, Condition> _alwaysAsked = new Dictionary<string, Condition>();
    private static readonly (string Name, string Symbol)[] _webJoinOptions = [("--web", "framework"), ("--web-audience", "audience"), ("--web-rendering", "rendering"), ("--web-port", "port")];

    private readonly AdditionContext _context;

    public ItemAdditions(AdditionContext context)
    {
        _context = context;
    }

    private Conversation Conversation => _context.Questionnaire.Conversation;

    public static string? NameRefusal(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers, string name)
    {
        var named = new Dictionary<string, string>(answers, StringComparer.Ordinal) { [_itemNameSymbol] = name };
        return manifest.Refusals.FirstOrDefault(refusal => refusal.When.Symbols.Contains(_itemNameSymbol) && manifest.Holds(refusal.When, named))?.Explanation;
    }

    public static Question Reviewed(Conversation conversation, Func<IReadOnlyDictionary<string, string>, IEnumerable<KeyValuePair<string, string>>> recap, Func<IReadOnlyDictionary<string, string>, string> subject) =>
        new(_reviewKey, (answers, _) =>
        {
            if (conversation.Review(subject(answers), [.. recap(answers)]))
            {
                return Reply.Passed(null);
            }

            return conversation.CanGoBack
                ? Reply.Backward
                : throw new LauncherException("Stopped. Nothing was written.");
        });

    public static void Install(Conversation conversation, TemplateEngine engine, TemplateSource source, string? templateVersion, string workingDirectory)
    {
        string version = templateVersion
            ?? throw new LauncherException("This template source has no templateVersion, so there's no package version to install.");
        string package = $"{TemplateSource.PackageId} {version}";
        conversation.Perform($"Installing {package}", $"{package} ready", () => engine.EnsureInstalled(source, version, workingDirectory));
    }

    public static void Equivalents(
        Conversation conversation,
        bool asks,
        string invocation,
        string command,
        IReadOnlyList<string> dotnetNew,
        string? templateSource,
        string? hive,
        bool setup = false,
        IReadOnlyList<string>? then = null)
    {
        List<string> launcher = [command, .. dotnetNew.Skip(1), "--yes"];
        if (setup)
        {
            launcher.Add("--setup");
        }

        if (templateSource is not null)
        {
            launcher.AddRange(["--template-source", templateSource]);
        }

        List<string> engine = [.. dotnetNew];
        if (hive is not null)
        {
            launcher.AddRange(["--hive", hive]);
            engine.AddRange(["--debug:custom-hive", hive]);
        }

        string engineLine = TemplateEngine.CommandLine("dotnet new", engine);
        conversation.Equivalents(
            asks ? [TemplateEngine.CommandLine(invocation, launcher), engineLine, .. then ?? []] : [engineLine, .. then ?? []],
            asks);
    }

    public void WebApp(ProjectFacts project)
    {
        TemplateManifest manifest = _context.Source.Manifest(TemplateKind.WebApp);
        Conversation.Begin($"Add a web app to {project.Name}", _context.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workspace"] = project.HasWorkspace ? Condition.False : Condition.True,
            ["scope"] = project.Scope,
            ["projectName"] = project.Name,
            ["auth"] = project.Auth,
            ["examples"] = project.Examples,
            ["files"] = project.Files ? Condition.True : Condition.False,
            ["hasClient"] = project.HasClient ? Condition.True : Condition.False,
            ["ci"] = project.Ci ? Condition.True : Condition.False,
            ["agentFiles"] = project.AgentFiles ? Condition.True : Condition.False,
        };
        Detected(project, manifest, detected);

        Asked asked = AskItem(
            manifest,
            "App name",
            name => AppNameProblem(name) ?? (Directory.Exists(Path.Combine(project.WebFolder, "apps", name)) ? "there's already an app with that name." : null),
            detected,
            $"Add it to {project.Name}");
        string name = asked.Name!;

        string folder = Path.Combine(project.WebFolder, "apps", name);
        if (Directory.Exists(folder))
        {
            throw new LauncherException($"There's already an app in '{folder}'. Pick another name with --name.");
        }

        if (manifest.Resolve("framework", asked.Options) == "blazor-wasm" && project.BlazorWebAssemblyApp is { } existing)
        {
            throw new LauncherException($"{project.Name} already has a Blazor WebAssembly app, Src/Web/apps/{existing}. This version adds only one per project.");
        }

        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        bool workspace = Condition.IsTruthy(manifest.Resolve("workspace", asked.Options));
        string[] join =
        [
            "run", "Tools/Tasks.cs", WebJoinTask, name,
            .. _webJoinOptions.SelectMany(option => manifest.Resolve(option.Symbol, asked.Options) is { } value ? [option.Name, value] : Array.Empty<string>()),
        ];
        if (_context.DryRun)
        {
            DryRun(manifest, name, options, [join]);
            return;
        }

        Install(Conversation, _context.Engine, _context.Source, _context.Version, project.Root);
        Conversation.Perform(
            $"Adding {name}",
            $"Added {name}",
            () => _context.Engine.Run(project.Root, [manifest.ShortName, "-n", name, .. options], [WebJoinTask]));

        string appReadme = Path.Combine(folder, "README.md");
        bool joined = RunTask(project.Root, "Join", "Making the edits its README lists outside its folder and recording it in cleanarch.json", join);

        Equivalents("web-app", [manifest.ShortName, "-n", name, .. options], [TemplateEngine.CommandLine(_dotnet, join)]);
        string readme = workspace && !joined ? Path.Combine(project.WebFolder, "README.md") : appReadme;
        string heading = joined ? "## Join the stack" : JoiningSection;
        Conversation.Close(new Closing(Path.GetRelativePath(project.Root, readme).Replace(Path.DirectorySeparatorChar, '/'), [], MarkdownSection.Of(readme, heading) ?? $"Read {readme}.", StepsLeft: !joined));
    }

    public void WebPackage(ProjectFacts project)
    {
        if (!project.HasWorkspace)
        {
            throw new LauncherException($"{project.Name} has no web workspace yet. Add a web app first with web-app: it creates Src/Web.");
        }

        TemplateManifest manifest = _context.Source.Manifest(TemplateKind.WebPackage);
        Conversation.Begin($"Add a package to {project.Name}'s web workspace", _context.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = project.Scope };
        Detected(project, manifest, detected);

        Asked asked = AskItem(manifest, "Package name", PackageNameProblem, detected, $"Add it to {project.Name}");
        string name = asked.Name!;

        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        string[] join = ["run", "Tools/Tasks.cs", WebJoinTask, name];
        if (_context.DryRun)
        {
            DryRun(manifest, name, options, [join]);
            return;
        }

        Install(Conversation, _context.Engine, _context.Source, _context.Version, project.Root);
        Conversation.Perform($"Adding {name}", $"Added {name}", () => _context.Engine.Run(project.Root, [manifest.ShortName, "-n", name, .. options], [WebJoinTask]));

        bool joined = RunTask(project.Root, "Join", "Recording it in cleanarch.json", join);

        Equivalents("web-package", [manifest.ShortName, "-n", name, .. options], [TemplateEngine.CommandLine(_dotnet, join)]);
        Conversation.Close(joined ? null : new Closing(null, [], $"Record it with {TemplateEngine.CommandLine(_dotnet, join)}.", StepsLeft: true));
    }

    public void Presentation(ProjectFacts project)
    {
        TemplateManifest manifest = _context.Source.Manifest(TemplateKind.Presentation);
        Conversation.Begin($"Add a presentation project to {project.Name}", _context.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["projectName"] = project.Name,
            ["auth"] = project.Auth,
            ["examples"] = project.Examples,
            ["files"] = project.Files ? Condition.True : Condition.False,
            ["hasClient"] = project.HasClient ? Condition.True : Condition.False,
            ["database"] = project.Database,
            ["agentFiles"] = project.AgentFiles ? Condition.True : Condition.False,
            ["hasApi"] = project.HasApi ? Condition.True : Condition.False,
            ["ci"] = project.Ci ? Condition.True : Condition.False,
        };
        Detected(project, manifest, detected);

        Asked asked = AskItemNamedLast(
            manifest,
            "Presentation name",
            (answers, name) => PresentationNameProblem(manifest, project, answers, name),
            (answers, remembered) => ProposedPresentationName(manifest, answers, remembered),
            detected,
            $"Add it to {project.Name}");
        string name = asked.Name!;
        string presentation = $"{project.Name}.{name}";
        string folder = project.FolderOf(name);

        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        string[] join = ["run", "Tools/Tasks.cs", PresentationJoinTask, presentation];
        string[] scaffold = ["run", "Tools/Tasks.cs", PresentationScaffoldTask, presentation];
        if (_context.DryRun)
        {
            DryRun(manifest, name, options, [join, scaffold]);
            return;
        }

        Install(Conversation, _context.Engine, _context.Source, _context.Version, project.Root);
        Conversation.Perform($"Adding {name}", $"Added {name}", () => _context.Engine.Run(project.Root, [manifest.ShortName, "-n", name, .. options], [PresentationJoinTask, PresentationScaffoldTask]));

        string joinLine = TemplateEngine.CommandLine(_dotnet, join);
        string scaffoldLine = TemplateEngine.CommandLine(_dotnet, scaffold);
        bool joined = RunTask(project.Root, "Join", "Making the edits its README lists outside its folders", join);
        bool created = joined && RunTask(project.Root, "Scaffold", "Creating it with its framework's dotnet new template, which needs the network", scaffold);
        if (!joined)
        {
            Conversation.Reported("Scaffold skipped", false, "Skipped because the join step didn't go through.", [scaffoldLine]);
        }

        Equivalents("presentation", [manifest.ShortName, "-n", name, .. options], [joinLine, scaffoldLine]);
        string readme = Path.Combine(project.PresentationFolder, folder, "README.md");
        string shown = $"Src/Presentation/{folder}/README.md";
        string[] identityStep = project.Auth == "local" && manifest.Resolve(_frameworkSymbol, asked.Options) == "mcp"
            ? [TemplateEngine.CommandLine(_dotnet, ["run", "Tools/Tasks.cs", "migration-add", "AddAccessKeys", "--context", "auth"])]
            : [];
        if (created)
        {
            Conversation.Close(new Closing(
                null,
                identityStep,
                $"Commit Src/Presentation/{folder} and its tests: from now on the pages are this project's own. {shown} says how the project was created."));
            return;
        }

        string heading = joined ? PresentationCreateSection : JoiningSection;
        Conversation.Close(new Closing(shown, identityStep, MarkdownSection.Of(readme, heading) ?? $"Read {shown}.", StepsLeft: true));
    }

    private void DryRun(TemplateManifest manifest, string name, IReadOnlyList<string> options, IReadOnlyList<string[]> tasks)
    {
        string version = _context.Version ?? throw new LauncherException("This template source has no templateVersion, so there's no package version to install.");
        string install = _context.Source.LocalPath is { } path
            ? TemplateEngine.CommandLine("dotnet new install", [path])
            : TemplateEngine.CommandLine("dotnet new install", [$"{TemplateSource.PackageId}@{version}"]);
        string[] engine = [manifest.ShortName, "-n", name, .. options];
        Conversation.Note("Dry run: nothing is written. These are the commands it would run, in this order, the first one only when that template version is not installed yet.");
        _context.Console.Verbatim(install);
        _context.Console.Verbatim(TemplateEngine.CommandLine("dotnet new", _context.Arguments.Hive is { } hive ? [.. engine, "--debug:custom-hive", hive] : engine));
        foreach (string[] task in tasks)
        {
            _context.Console.Verbatim(TemplateEngine.CommandLine(_dotnet, task));
        }
    }

    private void Equivalents(string command, IReadOnlyList<string> dotnetNew, IReadOnlyList<string> then) =>
        Equivalents(Conversation, _context.Questionnaire.Asks, _context.Invocation, command, dotnetNew, _context.Arguments.TemplateSource, _context.Arguments.Hive, then: then);

    private void Detected(ProjectFacts project, TemplateManifest manifest, Dictionary<string, string> detected)
    {
        foreach (string symbol in detected.Keys.Where(symbol => manifest.Options.All(option => option.Symbol != symbol)).ToList())
        {
            detected.Remove(symbol);
        }

        string facts = string.Join(", ", manifest.Options
            .Where(option => detected.ContainsKey(option.Symbol))
            .Select(option => $"--{option.CliName} {detected[option.Symbol]}"));
        Conversation.Note($"Taken from {project.Name}: {facts}");

        if (manifest.Refusals.FirstOrDefault(refusal => manifest.ParametersBehind(refusal.When).All(detected.ContainsKey) && manifest.Holds(refusal.When, detected)) is { } refused)
        {
            throw new LauncherException($"{manifest.ShortName} refuses {project.Name}, whatever the answers: {refused.Explanation} Nothing was written.");
        }
    }

    private Asked AskItem(
        TemplateManifest manifest,
        string label,
        Func<string, string?> problem,
        IReadOnlyDictionary<string, string> detected,
        string subject)
    {
        Questionnaire questionnaire = _context.Questionnaire;
        Dictionary<string, string> answers = questionnaire.Answer(
            manifest,
            _context.Arguments.TemplateOptions,
            detected,
            _alwaysAsked,
            [new(_nameKey, (_, remembered) => questionnaire.Text("--name", label, _context.Arguments.Name, remembered ?? manifest.DefaultName, problem))],
            [Reviewed(Conversation, given => [KeyValuePair.Create(label, given.GetValueOrDefault(_nameKey) ?? string.Empty), .. Questionnaire.Recap(manifest, given)], _ => subject)]);

        return new Asked(answers.GetValueOrDefault(_nameKey), answers);
    }

    private Asked AskItemNamedLast(
        TemplateManifest manifest,
        string label,
        Func<IReadOnlyDictionary<string, string>, string, string?> problem,
        Func<IReadOnlyDictionary<string, string>, string?, string?> proposed,
        IReadOnlyDictionary<string, string> detected,
        string subject)
    {
        Questionnaire questionnaire = _context.Questionnaire;
        Dictionary<string, string> answers = questionnaire.Answer(
            manifest,
            _context.Arguments.TemplateOptions,
            detected,
            _alwaysAsked,
            [],
            [
                new(_nameKey, (given, remembered) => questionnaire.Text("--name", label, _context.Arguments.Name, proposed(given, remembered), name => problem(given, name))),
                Reviewed(Conversation, given => [.. Questionnaire.Recap(manifest, given), KeyValuePair.Create(label, given.GetValueOrDefault(_nameKey) ?? string.Empty)], _ => subject),
            ]);

        return new Asked(answers.GetValueOrDefault(_nameKey), answers);
    }

    private bool RunTask(string root, string title, string running, IReadOnlyList<string> arguments)
    {
        string command = TemplateEngine.CommandLine(_dotnet, arguments);
        ProcessResult result = Conversation.Spin(
            running,
            () => _context.Processes.Run(new ProcessCall(_dotnet, root, arguments, _nothing), Conversation.EchoesTheTemplateEngine ? _context.Console.Verbatim : null));
        bool done = result.ExitCode == 0;
        Conversation.Reported(title + (done ? " done" : " failed"), done, done ? null : ProcessFailure.Why(command, result, ProcessFailure.Journal(root, title.ToLowerInvariant(), command, result)), done ? [] : [command]);
        return done;
    }

    private static string? AppNameProblem(string name) =>
        AppName().IsMatch(name) ? null : "use PascalCase, like BackOffice. It becomes the app's folder, service and port variable.";

    private static string? PackageNameProblem(string name) =>
        PackageName().IsMatch(name) ? null : "use letters, digits and dashes, starting with a letter, like tokens.";

    private static string? ProposedPresentationName(TemplateManifest manifest, IReadOnlyDictionary<string, string> answers, string? remembered)
    {
        if (remembered is not null && !PresentationNames.Values.Contains(remembered))
        {
            return remembered;
        }

        return answers.GetValueOrDefault(_frameworkSymbol) is { } framework && PresentationNames.TryGetValue(framework, out string? name)
            ? name
            : manifest.DefaultName;
    }

    private static string? PresentationNameProblem(TemplateManifest manifest, ProjectFacts project, IReadOnlyDictionary<string, string> answers, string name)
    {
        if (!AppName().IsMatch(name))
        {
            return $"use PascalCase, like Portal. {project.Name}.<name> becomes the project's name and service, in Src/Presentation/{project.FolderOf("<name>")}.";
        }

        if (NameRefusal(manifest, answers, name) is { } refused)
        {
            return refused;
        }

        string folder = Path.Combine(project.PresentationFolder, project.FolderOf(name));
        return Directory.Exists(folder) ? "there's already a presentation project with that name." : null;
    }

    private sealed record Asked(string? Name, Dictionary<string, string> Options);

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex AppName();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]*$")]
    private static partial Regex PackageName();
}
