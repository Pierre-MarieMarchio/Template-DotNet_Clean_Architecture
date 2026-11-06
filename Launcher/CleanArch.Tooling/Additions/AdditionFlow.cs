using System.Collections.ObjectModel;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Output;
using CleanArch.Tooling.Processes;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;
using CleanArch.Tooling.Templates;

namespace CleanArch.Tooling.Additions;

internal sealed record AskedItem(string Name, Dictionary<string, string> Options);

internal sealed class AdditionFlow(AdditionContext context)
{
    public const string Dotnet = "dotnet";

    private const string _nameKey = "--name";

    public AdditionContext Context => context;

    public Conversation Conversation => context.Questionnaire.Conversation;

    public static string[] TaskRun(params string[] task) => ["run", "Tools/Tasks.cs", .. task];

    public void Detected(ProjectFacts project, TemplateManifest manifest, Dictionary<string, string> detected)
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

    public AskedItem AskItem(
        TemplateManifest manifest,
        string label,
        Func<string, string?> problem,
        IReadOnlyDictionary<string, string> detected,
        string subject)
    {
        Questionnaire questionnaire = context.Questionnaire;
        Dictionary<string, string> answers = questionnaire.Answer(
            manifest,
            context.Arguments.TemplateOptions,
            detected,
            ReadOnlyDictionary<string, Condition>.Empty,
            [new(_nameKey, (_, remembered) => questionnaire.Text("--name", label, context.Arguments.Name, remembered ?? manifest.DefaultName, problem))],
            [ItemAdditions.Reviewed(Conversation, given => [KeyValuePair.Create(label, given.GetValueOrDefault(_nameKey) ?? string.Empty), .. Questionnaire.Recap(manifest, given)], _ => subject)]);

        return new AskedItem(answers.GetValueOrDefault(_nameKey)!, answers);
    }

    public AskedItem AskItemNamedLast(
        TemplateManifest manifest,
        string label,
        Func<IReadOnlyDictionary<string, string>, string, string?> problem,
        Func<IReadOnlyDictionary<string, string>, string?, string?> proposed,
        IReadOnlyDictionary<string, string> detected,
        string subject)
    {
        Questionnaire questionnaire = context.Questionnaire;
        Dictionary<string, string> answers = questionnaire.Answer(
            manifest,
            context.Arguments.TemplateOptions,
            detected,
            ReadOnlyDictionary<string, Condition>.Empty,
            [],
            [
                new(_nameKey, (given, remembered) => questionnaire.Text("--name", label, context.Arguments.Name, proposed(given, remembered), name => problem(given, name))),
                ItemAdditions.Reviewed(Conversation, given => [.. Questionnaire.Recap(manifest, given), KeyValuePair.Create(label, given.GetValueOrDefault(_nameKey) ?? string.Empty)], _ => subject),
            ]);

        return new AskedItem(answers.GetValueOrDefault(_nameKey)!, answers);
    }

    public void ShowDryRun(IReadOnlyList<string> engine, IReadOnlyList<string[]> tasks)
    {
        string version = TemplateSource.Installable(context.Version);
        string install = context.Source.LocalPath is { } path
            ? TemplateEngine.CommandLine("dotnet new install", [path])
            : TemplateEngine.CommandLine("dotnet new install", [$"{TemplateSource.PackageId}@{version}"]);
        Conversation.Note("Dry run: nothing is written. These are the commands it would run, in this order, the first one only when that template version is not installed yet.");
        context.Console.Verbatim(install);
        context.Console.Verbatim(TemplateEngine.CommandLine("dotnet new", context.Arguments.Hive is { } hive ? [.. engine, "--debug:custom-hive", hive] : engine));
        foreach (string[] task in tasks)
        {
            context.Console.Verbatim(TemplateEngine.CommandLine(Dotnet, task));
        }
    }

    public void Generate(ProjectFacts project, string name, IReadOnlyList<string> engine, IReadOnlyCollection<string> performed)
    {
        ItemAdditions.Install(Conversation, context.Engine, context.Source, context.Version, project.Root);
        Conversation.Perform($"Adding {name}", $"Added {name}", () => context.Engine.Run(project.Root, engine, performed));
    }

    public bool RunTask(string root, string title, string running, IReadOnlyList<string> arguments)
    {
        string command = TemplateEngine.CommandLine(Dotnet, arguments);
        ProcessResult result = Conversation.Spin(
            running,
            () => context.Processes.Run(new ProcessCall(Dotnet, root, arguments, ReadOnlyDictionary<string, string>.Empty), Conversation.EchoesTheTemplateEngine ? context.Console.Verbatim : null));
        bool done = result.ExitCode == 0;
        Conversation.Reported(title + (done ? " done" : " failed"), done, done ? null : ProcessFailure.Why(command, result, ProcessFailure.Journal(root, title.ToLowerInvariant(), command, result)), done ? [] : [command]);
        return done;
    }

    public void Equivalents(string command, IReadOnlyList<string> dotnetNew, IReadOnlyList<string> then) =>
        ItemAdditions.Equivalents(Conversation, context.Questionnaire.Asks, context.Invocation, command, dotnetNew, context.Arguments.TemplateSource, context.Arguments.Hive, then: then);
}
