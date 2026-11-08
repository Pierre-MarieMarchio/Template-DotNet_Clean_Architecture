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

internal sealed record PlannedStep(string Title, string Running, ItemStep Step);

internal sealed class AdditionFlow(AdditionContext context)
{
    public const string Dotnet = "dotnet";

    private const string _nameKey = "--name";

    public AdditionContext Context => context;

    public Conversation Conversation => context.Questionnaire.Conversation;

    public IItemSteps Steps => context.Steps;

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

    public string Invocation => $"{context.Tool} add";

    public void ShowDryRun(IReadOnlyList<string> engine, IReadOnlyList<PlannedStep> steps)
    {
        string version = TemplateSource.Installable(context.Version);
        string install = context.Source.LocalPath is { } path
            ? TemplateEngine.CommandLine("dotnet new install", [path])
            : TemplateEngine.CommandLine("dotnet new install", [$"{TemplateSource.PackageId}@{version}"]);
        Conversation.Note("Dry run: nothing is written. These are the commands it would run, in this order, the first one only when that template version is not installed yet, then the steps it takes itself.");
        context.Console.Verbatim(install);
        context.Console.Verbatim(TemplateEngine.CommandLine("dotnet new", context.Arguments.Hive is { } hive ? [.. engine, "--debug:custom-hive", hive] : engine));
        ShowSteps(steps);
    }

    public void Generate(ProjectFacts project, string name, IReadOnlyList<string> engine, IReadOnlyCollection<string> performed)
    {
        ItemAdditions.Install(Conversation, context.Engine, context.Source, context.Version, project.Root);
        Conversation.Perform($"Adding {name}", $"Added {name}", () => context.Engine.Run(project.Root, engine, performed));
    }

    public string Completion(string command) => $"{Invocation} {command} --name";

    public string CompletedBy(string command, string name) => $"{Completion(command)} {name}";

    public Dictionary<string, string> GivenOptions(TemplateManifest manifest) =>
        manifest.Options
            .Where(option => context.Arguments.TemplateOptions.ContainsKey(option.CliName))
            .ToDictionary(option => option.Symbol, option => context.Arguments.TemplateOptions[option.CliName], StringComparer.Ordinal);

    public string? Existing(Func<string, string> folderOf, Func<string, bool> valid) =>
        context.Arguments.Name is { } name && valid(name) && Directory.Exists(folderOf(name)) ? name : null;

    public int? Complete(string root, string shown, string complete, IReadOnlyList<PlannedStep> steps, string rerun)
    {
        PlannedStep[] pending = [.. steps.Where(planned => !planned.Step.IsDone())];
        if (pending.Length == 0)
        {
            throw new LauncherException($"{shown} is {complete} already: there is nothing to complete. Nothing was written.");
        }

        if (context.DryRun)
        {
            Conversation.Note($"Dry run: nothing is written. {shown} exists, so these are the steps it would take to complete it, in this order.");
            ShowSteps(pending);
            return null;
        }

        return RunSteps(root, steps, rerun);
    }

    public int RunSteps(string root, IReadOnlyList<PlannedStep> steps, string rerun)
    {
        for (int index = 0; index < steps.Count; index++)
        {
            if (steps[index].Step.IsDone())
            {
                Conversation.Note($"{steps[index].Title}: done already.");
                continue;
            }

            if (!RunStep(root, steps[index], rerun))
            {
                foreach (PlannedStep skipped in steps.Skip(index + 1))
                {
                    Conversation.Reported($"{skipped.Title} skipped", false, $"Skipped because the {steps[index].Title.ToLowerInvariant()} step didn't go through.", [rerun]);
                }

                return index;
            }
        }

        return steps.Count;
    }

    public void Equivalents(string command, IReadOnlyList<string> dotnetNew, IReadOnlyList<string> then) =>
        ItemAdditions.Equivalents(Conversation, context.Questionnaire.Asks, Invocation, command, dotnetNew, context.Arguments.TemplateSource, context.Arguments.Hive, then: then);

    private void ShowSteps(IEnumerable<PlannedStep> steps)
    {
        foreach (PlannedStep planned in steps)
        {
            context.Console.Verbatim($"{planned.Title}: {planned.Running}");
        }
    }

    private bool RunStep(string root, PlannedStep planned, string rerun)
    {
        string title = planned.Title;
        string step = $"The {title.ToLowerInvariant()} step";
        ProcessResult result = Conversation.Spin(planned.Running, () => planned.Step.Run(Conversation.EchoesTheTemplateEngine ? context.Console.Verbatim : null));
        bool done = result.ExitCode == 0;
        Conversation.Reported(title + (done ? " done" : " failed"), done, done ? null : ProcessFailure.Why(step, result, ProcessFailure.Journal(root, title.ToLowerInvariant(), step, result)), done ? [] : [rerun]);
        return done;
    }
}
