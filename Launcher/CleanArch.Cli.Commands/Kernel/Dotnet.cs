namespace CleanArch.Cli.Commands.Kernel;

internal static class Dotnet
{
    public static int Run(Shell shell, string workingDirectory, IReadOnlyList<string> arguments) =>
        shell.Run(new ExternalCall("dotnet", arguments, workingDirectory));
}
