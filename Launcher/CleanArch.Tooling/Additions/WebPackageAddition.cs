using System.Text.RegularExpressions;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Projects;
using CleanArch.Tooling.Prompts;

namespace CleanArch.Tooling.Additions;

internal sealed partial class WebPackageAddition(AdditionFlow flow)
{
    private const string _command = "web-package";

    public void Add(ProjectFacts project)
    {
        if (!project.HasWorkspace)
        {
            throw new LauncherException($"{project.Name} has no web workspace yet. Add a web app first with web-app: it creates Src/Web.");
        }

        TemplateManifest manifest = flow.Context.Source.Manifest(TemplateKind.WebPackage);
        if (flow.Existing(name => PackageFolder(project, name), PackageName().IsMatch) is { } existing)
        {
            Complete(project, existing);
            return;
        }

        flow.Conversation.Begin($"Add a package to {project.Name}'s web workspace", flow.Context.Version);
        var detected = new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = project.Scope };
        flow.Detected(project, manifest, detected);

        AskedItem asked = flow.AskItem(manifest, "Package name", name => PackageNameProblem(project, name), detected, $"Add it to {project.Name}");
        string name = asked.Name;

        string[] engine = [manifest.ShortName, "-n", name, .. manifest.CommandArguments(asked.Options)];
        PlannedStep[] steps = Steps(project, name);
        if (flow.Context.DryRun)
        {
            flow.ShowDryRun(engine, steps);
            return;
        }

        flow.Generate(project, name, engine, [flow.Completion(_command)]);
        string rerun = flow.CompletedBy(_command, name);
        int done = flow.RunSteps(project.Root, steps, rerun);

        flow.Equivalents(_command, engine, [rerun]);
        Close(steps, done, rerun);
    }

    private void Complete(ProjectFacts project, string name)
    {
        flow.Conversation.Begin($"Complete Src/Web/packages/{name} in {project.Name}", flow.Context.Version);
        PlannedStep[] steps = Steps(project, name);
        string rerun = flow.CompletedBy(_command, name);
        if (flow.Complete(project.Root, $"Src/Web/packages/{name}", "recorded in cleanarch.json", steps, rerun) is { } done)
        {
            Close(steps, done, rerun);
        }
    }

    private PlannedStep[] Steps(ProjectFacts project, string name) =>
        [new("Join", "Recording it in cleanarch.json", flow.Steps.WebJoin(project.Root, name, new WebJoinOptions()))];

    private void Close(PlannedStep[] steps, int done, string rerun) =>
        flow.Conversation.Close(done == steps.Length ? null : new Closing(null, [], $"Record it with {rerun}.", StepsLeft: true));

    private string? PackageNameProblem(ProjectFacts project, string name)
    {
        if (!PackageName().IsMatch(name))
        {
            return "use letters, digits and dashes, starting with a letter, like tokens.";
        }

        return Directory.Exists(PackageFolder(project, name)) ? $"there's already a package with that name: {flow.CompletedBy(_command, name)} completes it." : null;
    }

    private static string PackageFolder(ProjectFacts project, string name) => Path.Combine(project.WebFolder, "packages", name);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9-]*$")]
    private static partial Regex PackageName();
}
