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

    public CapturedRun Capture(ExternalCall call, bool echo = true)
    {
        using var captured = new StringWriter();
        TextWriter callOutput = echo ? new TeeWriter(output, captured) : captured;
        if (echo)
        {
            output.WriteLine(call.Line);
            output.Flush();
        }

        int exit = processes.Launch(call, callOutput, error);
        return new CapturedRun(exit, captured.ToString());
    }

    public int Fail(string message)
    {
        error.WriteLine(message);
        return ExitCode.Failed;
    }
}
