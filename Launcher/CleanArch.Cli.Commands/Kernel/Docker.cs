namespace CleanArch.Cli.Commands.Kernel;

internal static class Docker
{
    public static ExternalCall Compose(string root, params IReadOnlyList<string> arguments) => new("docker", ["compose", .. arguments], root);
}
