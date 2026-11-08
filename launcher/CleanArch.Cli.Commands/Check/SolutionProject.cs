namespace CleanArch.Cli.Commands.Check;

internal sealed record SolutionProject(string Path, string Folder, IReadOnlyList<string> References, bool DockerFreeTests, bool WindowsOnly = false)
{
    public bool NeedsDocker => Path.StartsWith("tests/", StringComparison.Ordinal) && !DockerFreeTests;
}
