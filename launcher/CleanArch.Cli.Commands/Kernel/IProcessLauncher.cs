namespace CleanArch.Cli.Commands.Kernel;

internal interface IProcessLauncher
{
    int Launch(ExternalCall call, TextWriter output, TextWriter error);
}
