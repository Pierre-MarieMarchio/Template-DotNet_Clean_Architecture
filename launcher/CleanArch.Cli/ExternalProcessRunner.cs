using System.Text;
using CleanArch.Tooling.Processes;

namespace CleanArch.Cli;

internal sealed class ExternalProcessRunner(IProcessLauncher processes) : IProcessRunner
{
    public ProcessResult Run(ProcessCall command, Action<string>? line)
    {
        var captured = new StringBuilder();
        using var announce = line is null ? TextWriter.Null : new LineWriter(line, null);
        using var output = new LineWriter(line, captured);
        int exitCode = new Shell(processes, announce, output).Run(
            new ExternalCall(command.FileName, command.Arguments, command.WorkingDirectory, command.Environment),
            output,
            output);
        output.Flush();
        return new ProcessResult(exitCode, captured.ToString());
    }
}
