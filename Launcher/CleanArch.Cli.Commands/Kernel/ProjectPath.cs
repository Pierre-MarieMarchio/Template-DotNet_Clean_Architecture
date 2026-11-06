namespace CleanArch.Cli.Commands.Kernel;

internal static class ProjectPath
{
    public static string Relative(string root, string path) => Shown(Path.GetRelativePath(root, path));

    public static string Shown(string relative) => relative.Replace('\\', '/');
}
