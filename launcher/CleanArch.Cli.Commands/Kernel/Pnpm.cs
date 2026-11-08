namespace CleanArch.Cli.Commands.Kernel;

internal static class Pnpm
{
    private static readonly string _fileName = OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";

    public static ExternalCall Call(string workingDirectory, params IReadOnlyList<string> arguments) => new(_fileName, arguments, workingDirectory);

    public static ExternalCall In(LocatedProject project, string folder, params IReadOnlyList<string> arguments) =>
        Call(Path.Combine(project.Root, folder), arguments) with { DirectoryNote = ProjectPath.Shown(folder) };
}
