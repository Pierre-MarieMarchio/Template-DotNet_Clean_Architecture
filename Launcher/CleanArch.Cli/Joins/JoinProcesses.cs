namespace CleanArch.Cli.Joins;

internal sealed class JoinProcesses(IProcessLauncher processes, TextWriter output)
{
    public TextWriter Output => output;

    public CapturedRun Quietly(ExternalCall call) => Run(call, null);

    public CapturedRun Echoed(ExternalCall call) => Run(call, output);

    private CapturedRun Run(ExternalCall call, TextWriter? echo)
    {
        using var captured = new StringWriter();
        TextWriter sink = captured;
        if (echo is not null)
        {
            echo.WriteLine(call.Line);
            echo.Flush();
            sink = new TeeWriter(echo, captured);
        }

        int exit = processes.Launch(call, sink, sink);
        return new CapturedRun(exit, captured.ToString());
    }
}
