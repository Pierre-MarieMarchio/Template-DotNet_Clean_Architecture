namespace CleanArch.Cli.Commands.Kernel;

internal sealed class Shell(IProcessLauncher processes, TextWriter output, TextWriter error)
{
    public TextWriter Output => output;

    public TextWriter Error => error;

    public int Run(ExternalCall call) => Run(call, output, error);

    public int Run(ExternalCall call, TextWriter callOutput, TextWriter callError)
    {
        output.WriteLine(call.Line);
        output.Flush();
        return processes.Launch(call, callOutput, callError);
    }
}
