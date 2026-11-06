using CleanArch.Create.Setup;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Markdown;
using CleanArch.Tooling.Output;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;

namespace CleanArch.Create.Commands.NewProject;

public sealed class NewCommand : ILauncherCommand
{
    public static readonly IReadOnlyDictionary<string, Condition> ProjectAskedWhen = new Dictionary<string, Condition>(StringComparer.Ordinal)
    {
        ["webName"] = Condition.Parse("(web != \"none\")"),
        ["webAudience"] = Condition.Parse("(web != \"none\")"),
        ["webRendering"] = Condition.Parse("(web != \"none\")"),
    };

    public string Name => "new";

    public IReadOnlyList<string> Summary => ["create a project (what runs when you're not inside one)"];

    public string Help(TemplateSource source) =>
        LauncherUsage.Text() + Environment.NewLine + Environment.NewLine + LauncherUsage.TemplateOptions(source.Manifest(TemplateKind.Project), "project", ProjectAskedWhen);

    public string? Offer(ProjectFacts project) => "start a new project somewhere else";

    public void Run(Session session)
    {
        TemplateManifest manifest = session.Source.Manifest(TemplateKind.Project);
        session.Conversation.Begin("Create a project", session.Version);
        NewAnswers asked = NewQuestions.Ask(session, manifest, ProjectAskedWhen);

        string workingDirectory = session.Machine.WorkingDirectory;
        string target = Path.GetFullPath(asked.Output, workingDirectory);
        IReadOnlyList<string> options = manifest.CommandArguments(asked.Options);
        ItemAdditions.Install(session.Conversation, session.Dotnet, session.Source, session.Version, workingDirectory);
        Generate(session, [manifest.ShortName, "-n", asked.Name, "-o", target, .. options, .. ExistingFiles.Forced(asked.ExistingFiles)], asked, target);
        List<SetupOutcome> outcomes = SetUp(session, target, asked.Setup);

        ItemAdditions.Equivalents(
            session.Conversation,
            session.Questionnaire.Asks,
            LauncherCommands.InvocationWithArguments,
            Name,
            [manifest.ShortName, "-n", asked.Name, "-o", asked.Output, .. options],
            session.Parsed.TemplateSource,
            session.Parsed.Hive,
            asked.Setup.Count == ProjectSetup.Steps.Count);
        Close(session, target, asked.Output, outcomes);
    }

    private static void Generate(Session session, IReadOnlyList<string> arguments, NewAnswers asked, string target)
    {
        if (asked.ExistingFiles == ExistingFiles.Remove)
        {
            session.Conversation.Perform($"Removing the files in {target}", $"Removed the files in {target}", () => ExistingFiles.Empty(target));
        }

        session.Conversation.Perform($"Generating {asked.Name}", $"Generated {asked.Name}", () => session.Dotnet.Run(
                session.Machine.WorkingDirectory,
                arguments,
                asked.Setup.Contains(ProjectSetup.Bootstrap.Title) ? [BootstrapStep.Task] : null));
    }

    private static void Close(Session session, string target, string output, List<SetupOutcome> outcomes)
    {
        string[] left = [.. ProjectSetup.Left(outcomes)];
        string? section = Without(
            MarkdownSection.Of(Path.Combine(target, "README.md"), "## Run it"),
            [.. outcomes.Where(outcome => outcome.Status == SetupStatus.Done && outcome.Step.Replaces is not null).Select(outcome => outcome.Step.Replaces!), .. left]);
        string[] moveInto = SameFolder(target, session.Machine.WorkingDirectory) ? [] : [TemplateEngine.CommandLine("cd", [output])];
        session.Conversation.Close(new Closing(
            null,
            [.. moveInto, .. left],
            section ?? "Read the project's README.md.",
            outcomes.Any(outcome => outcome.Status != SetupStatus.Done)));
    }

    private static List<SetupOutcome> SetUp(Session session, string target, IReadOnlyList<string> chosen)
    {
        Machine machine = session.Machine;
        var setup = new ProjectSetup(
            machine.Processes,
            target,
            TemplateSource.Installable(session.Version),
            session.Conversation.EchoesTheTemplateEngine ? machine.Console.Verbatim : null);
        var outcomes = new List<SetupOutcome>();
        machine.Interruption.WhileSettingUp(() =>
        {
            foreach (SetupStep step in ProjectSetup.Steps.Where(step => chosen.Contains(step.Title)))
            {
                SetupOutcome outcome = machine.Interruption.Stopped ? setup.Stopped(step) : session.Conversation.Spin(step.Running, () => setup.Run(step, outcomes));
                session.Conversation.Reported(outcome.Text, outcome.Status == SetupStatus.Done, outcome.Why, outcome.Rerun);
                outcomes.Add(outcome);
            }
        });

        return outcomes;
    }

    private static bool SameFolder(string one, string other) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(one)) == Path.TrimEndingDirectorySeparator(Path.GetFullPath(other));

    private static string? Without(string? section, IReadOnlyCollection<string> commands) =>
        section is null
            ? null
            : string.Join('\n', section.Split('\n').Where(line => !commands.Any(command => line.TrimStart().StartsWith(command, StringComparison.Ordinal))));
}
