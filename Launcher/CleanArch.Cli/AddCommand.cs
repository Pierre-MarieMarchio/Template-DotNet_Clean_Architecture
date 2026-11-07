using System.CommandLine;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Output;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;
using Spectre.Console;

namespace CleanArch.Cli;

internal static class AddCommand
{
    public const string Name = "add";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Build);

    private static readonly IReadOnlyList<Addition> _additions =
    [
        new(
            "web-app",
            "Add a web app to this project. A Blazor WebAssembly app also gets the edits its README lists.",
            TemplateKind.WebApp,
            _ => null,
            (additions, facts) => additions.WebApp(facts)),
        new(
            "web-package",
            "Add a shared package to this project's web workspace.",
            TemplateKind.WebPackage,
            NoWorkspace,
            (additions, facts) => additions.WebPackage(facts)),
        new(
            "presentation",
            "Add a presentation project to this project, make the edits its README lists, then create its pages.",
            TemplateKind.Presentation,
            _ => null,
            (additions, facts) => additions.Presentation(facts)),
    ];

    private static string? NoWorkspace(LocatedProject project) =>
        project.File.Web is null
            ? $"This project has no add web-package command: its {ProjectLocator.FileName} records no web workspace, so there is no Src/Web to add a package to. Add a web app first with `{CleanArchCommand.Name} {Name} web-app`."
            : null;

    private static Command Build(CommandContext context)
    {
        var add = new Command(Name, "Add a feature, a field, a use case, a setting, a domain event, a web app, a web package or a presentation project to this project.");
        foreach (Command generator in FeatureAdditions.Commands(context))
        {
            add.Subcommands.Add(generator);
        }

        foreach (Addition addition in _additions)
        {
            Command command = BuildAddition(context, addition);
            if (context.Project is { } project && addition.Refusal(project) is { } refusal)
            {
                CleanArchCommand.Refuse(command, refusal);
            }

            add.Subcommands.Add(command);
        }

        return add;
    }

    private static Command BuildAddition(CommandContext context, Addition addition)
    {
        var command = new Command(addition.Command, addition.Description);
        var name = new Option<string>("--name", "-n") { Description = "The name of what to add." };
        var yes = new Option<bool>("--yes", "-y") { Description = "Use the default for every option you don't pass." };
        var dryRun = new Option<bool>("--dry-run") { Description = "Print the commands it would run and write nothing." };
        var templateSource = new Option<string>("--template-source") { Description = "Use a checkout of the template or a .nupkg instead of the published package." };
        var hive = new Option<string>("--hive") { Description = "Keep the template engine's settings in this folder (dotnet new --debug:custom-hive)." };
        command.Options.Add(name);
        command.Options.Add(yes);
        command.Options.Add(dryRun);
        command.Options.Add(templateSource);
        command.Options.Add(hive);

        TemplateManifest manifest = TemplateSource.Embedded(typeof(AddCommand).Assembly).Manifest(addition.Kind);
        List<(string CliName, Option<string[]> Option)> templateOptions = [.. manifest.Options.Select(option => (option.CliName, TemplateOption(option)))];
        foreach ((_, Option<string[]> option) in templateOptions)
        {
            command.Options.Add(option);
        }

        if (context.Project is { } project)
        {
            command.SetAction(parseResult => Run(context, project, addition, parseResult, name, yes, dryRun, templateSource, hive, templateOptions));
        }

        return command;
    }

    private static Option<string[]> TemplateOption(TemplateOption option)
    {
        string values = option.Kind switch
        {
            OptionKind.Choice => string.Join('|', option.Choices.Select(choice => choice.Value)),
            OptionKind.Bool => "true|false",
            _ => "<value>",
        };
        string repeated = option.AllowsMany ? ", one --" + option.CliName + " per value" : string.Empty;
        string fallback = option.DefaultValue is null ? string.Empty : $" (default: {option.DefaultValue})";
        return new Option<string[]>("--" + option.CliName)
        {
            Description = $"{option.Label}: {values}{repeated}{fallback}",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
    }

    private static int Run(
        CommandContext context,
        LocatedProject project,
        Addition addition,
        ParseResult parseResult,
        Option<string> name,
        Option<bool> yes,
        Option<bool> dryRun,
        Option<string> templateSource,
        Option<string> hive,
        IReadOnlyList<(string CliName, Option<string[]> Option)> templateOptions)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;
        IAnsiConsole console = Terminal.Over(output);
        bool interactive = ReferenceEquals(output, Console.Out) && !Console.IsInputRedirected && console.Profile.Capabilities.Interactive;
        try
        {
            string? sourcePath = parseResult.GetValue(templateSource);
            TemplateSource source = sourcePath is null
                ? TemplateSource.Embedded(typeof(AddCommand).Assembly)
                : TemplateSource.FromPath(Path.GetFullPath(sourcePath, context.WorkingDirectory));
            string? version = sourcePath is null ? project.File.Template.Version : source.Manifest(TemplateKind.Project).Version;
            string? hiveFolder = parseResult.GetValue(hive);
            var processes = new ExternalProcessRunner(context.Processes);
            var questionnaire = new Questionnaire(console, interactive, parseResult.GetValue(yes));
            var engine = new TemplateEngine(
                console,
                hiveFolder is null ? null : Path.GetFullPath(hiveFolder, context.WorkingDirectory),
                questionnaire.Conversation.EchoesTheTemplateEngine,
                processes);
            var additions = new ItemAdditions(new AdditionContext(
                questionnaire,
                source,
                engine,
                new ProjectItemSteps(processes),
                console,
                new AdditionArguments(parseResult.GetValue(name), GivenTemplateOptions(parseResult, templateOptions), sourcePath, hiveFolder),
                version,
                $"{CleanArchCommand.Name} {Name}",
                parseResult.GetValue(dryRun)));

            addition.Apply(additions, FactsOf(project));
            return CleanArchCommand.Succeeded;
        }
        catch (LauncherException refused)
        {
            error.WriteLine(refused.Message);
            return CleanArchCommand.Failed;
        }
    }

    private static Dictionary<string, string> GivenTemplateOptions(ParseResult parseResult, IReadOnlyList<(string CliName, Option<string[]> Option)> templateOptions)
    {
        var given = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string cliName, Option<string[]> option) in templateOptions)
        {
            if (parseResult.GetResult(option) is not null)
            {
                string[] values = parseResult.GetValue(option) ?? [];
                given[cliName] = values.Length == 0 ? Condition.True : string.Join(Condition.ValueSeparator, values);
            }
        }

        return given;
    }

    private static ProjectFacts FactsOf(LocatedProject project)
    {
        ProjectFile file = project.File;
        return new ProjectFacts(
            project.Root,
            file.Name,
            file.Web is not null,
            file.Web?.Scope ?? ProjectFacts.KebabName(file.Name),
            file.Options.Auth ?? "none",
            file.Options.Examples ?? "none",
            file.Options.Files == true,
            file.Client,
            file.Options.Database ?? "postgresql",
            file.Options.Ci == true,
            file.Options.AgentFiles == true,
            ProjectLayout.HoldsTheApi(project));
    }


    private sealed record Addition(string Command, string Description, TemplateKind Kind, Func<LocatedProject, string?> Refusal, Action<ItemAdditions, ProjectFacts> Apply);
}
