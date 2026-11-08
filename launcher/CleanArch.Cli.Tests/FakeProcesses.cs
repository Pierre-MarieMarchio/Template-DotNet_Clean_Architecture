namespace CleanArch.Cli.Tests;

internal sealed class FakeProcesses(Func<ExternalCall, TextWriter, TextWriter, int> launch) : IProcessLauncher
{
    public FakeProcesses()
        : this((_, _, _) => CleanArchCommand.Succeeded)
    {
    }

    public int Launch(ExternalCall call, TextWriter output, TextWriter error) => launch(call, output, error);
}
