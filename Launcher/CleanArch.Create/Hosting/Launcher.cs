using CleanArch.Create.Commands;
using CleanArch.Create.Setup;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Output;
using CleanArch.Tooling.Processes;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;
using Spectre.Console;

namespace CleanArch.Create.Hosting;

public sealed class Launcher
{
    public const int StoppedExitCode = 130;

    private readonly Machine _machine;
    private readonly bool _interactive;

    public Launcher(IAnsiConsole console, string workingDirectory, bool interactive)
        : this(console, workingDirectory, interactive, new ProcessRunner())
    {
    }

    public Launcher(IAnsiConsole console, string workingDirectory, bool interactive, IProcessRunner processes)
    {
        _machine = new Machine(console, workingDirectory, processes, new Interruption());
        _interactive = interactive;
    }

    public bool Interrupt() => _machine.Interruption.Interrupt();

    public int Run(IReadOnlyList<string> arguments)
    {
        Conversation conversation = new PlainConversation(_machine.Console);
        try
        {
            Arguments parsed = Arguments.Parse(arguments);
            TemplateSource source = parsed.TemplateSource is { } path
                ? TemplateSource.FromPath(Path.GetFullPath(path, _machine.WorkingDirectory))
                : TemplateSource.Embedded(typeof(Launcher).Assembly);
            if (parsed.Help)
            {
                _machine.Console.Verbatim(LauncherCommands.Named(parsed.Command).Help(source));
                return 0;
            }

            var questionnaire = new Questionnaire(_machine.Console, _interactive, parsed.Yes);
            conversation = questionnaire.Conversation;
            Session session = Open(parsed, source, questionnaire);
            Chosen(session).Run(session);
            return _machine.Interruption.Stopped ? StoppedExitCode : 0;
        }
        catch (LauncherException error)
        {
            conversation.Fail(error.Message);
            return 1;
        }
    }

    private Session Open(Arguments parsed, TemplateSource source, Questionnaire questionnaire)
    {
        var dotnet = new TemplateEngine(
            _machine.Console,
            parsed.Hive is { } hive ? Path.GetFullPath(hive, _machine.WorkingDirectory) : null,
            questionnaire.Conversation.EchoesTheTemplateEngine,
            _machine.Processes);
        string? version = source.Manifest(TemplateKind.Project).Version;
        return new Session(_machine, parsed, source, questionnaire, dotnet, version, GeneratedProject.Find(_machine.WorkingDirectory));
    }

    private static ILauncherCommand Chosen(Session session)
    {
        if (session.Parsed.Command is { } named)
        {
            return LauncherCommands.Named(named);
        }

        if (session.Project is not { } project || !session.Questionnaire.Asks)
        {
            return LauncherCommands.Default;
        }

        OptionChoice[] offered =
        [
            .. LauncherCommands.InsideAProject.Append(LauncherCommands.Default)
                .Select(command => (command.Name, Description: command.Offer(project)))
                .Where(command => command.Description is not null)
                .Select(command => new OptionChoice(command.Name, command.Description!)),
        ];
        session.Conversation.Begin(project.Name, session.Version);
        return LauncherCommands.Named(session.Conversation.Choose(
            "What do you want to do?",
            $"You're inside {project.Name}, a project made with this template.",
            offered,
            null));
    }
}
