namespace CleanArch.Cli.Generators.Shared;

internal sealed record GenerationContext(LocatedProject Project, bool DryRun, TextWriter Output, TextWriter Error, IExecutor Executor)
{
    internal string Root => Project.Root;

    internal string Product => Project.File.Name;
}
