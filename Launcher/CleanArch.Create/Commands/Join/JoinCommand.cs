using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using CleanArch.Tooling.Projects;

namespace CleanArch.Create.Commands.Join;

public abstract class JoinCommand : ILauncherCommand
{
    private static readonly IReadOnlyDictionary<string, Condition> _alwaysAsked = new Dictionary<string, Condition>();

    public abstract string Name { get; }

    public abstract IReadOnlyList<string> Summary { get; }

    protected abstract TemplateKind Kind { get; }

    protected abstract string Subject { get; }

    protected abstract string Purpose { get; }

    protected abstract string Item { get; }

    public abstract string? Offer(ProjectFacts project);

    public string Help(TemplateSource source) =>
        Usage() + Environment.NewLine + Environment.NewLine + LauncherUsage.TemplateOptions(source.Manifest(Kind), Subject, _alwaysAsked);

    public void Run(Session session)
    {
        if (session.Parsed.Setup)
        {
            throw new LauncherException($"--setup sets up a project new generates; {Name} takes no --setup.");
        }

        ProjectFacts project = session.Project ?? throw new LauncherException(
            $"{Name} only works inside a project made with this template, and there isn't one here or in any folder above (it looks for a .slnx or a .sln next to Tools/Tasks.cs).");
        Add(Additions(session), project);
    }

    protected abstract void Add(ItemAdditions additions, ProjectFacts project);

    private static ItemAdditions Additions(Session session) =>
        new(new AdditionContext(
            session.Questionnaire,
            session.Source,
            session.Dotnet,
            session.Machine.Processes,
            session.Machine.Console,
            new AdditionArguments(session.Parsed.Name, session.Parsed.TemplateOptions, session.Parsed.TemplateSource, session.Parsed.Hive),
            session.Version,
            LauncherCommands.InvocationWithArguments));

    private string Usage() =>
        $"""
        Usage: {LauncherCommands.InvocationWithArguments} {Name} [options] [--<template option> <value>]...

        {Name} {Purpose}

        Options
          -n, --name <name>            the {Item}'s name
          -y, --yes                    use the default for every option you don't pass
          --template-source <path>     use a checkout of the template or a .nupkg instead of the published package
          --hive <folder>              keep the template engine's settings in this folder (dotnet new --debug:custom-hive)
          -h, --help                   show this help
        """;
}
