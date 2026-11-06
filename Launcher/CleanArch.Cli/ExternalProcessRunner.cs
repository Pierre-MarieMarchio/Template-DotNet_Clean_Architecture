using System.Text;
using CleanArch.Tooling.Processes;

namespace CleanArch.Cli;

internal sealed class ExternalProcessRunner : IProcessRunner
{
    public ProcessResult Run(ProcessCall command, Action<string>? line)
    {
        var captured = new StringBuilder();
        using var announce = line is null ? TextWriter.Null : new LineWriter(line, null);
        using var output = new LineWriter(line, captured);
        int exitCode = new Shell(ExternalCommand.Launcher, announce, output).Run(
            new ExternalCall(command.FileName, command.Arguments, command.WorkingDirectory, command.Environment),
            output,
            output);
        output.Flush();
        return new ProcessResult(exitCode, captured.ToString());
    }

    private sealed class LineWriter(Action<string>? relay, StringBuilder? captured) : TextWriter
    {
        private readonly StringBuilder _line = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                Complete();
            }
            else if (value != '\r')
            {
                _line.Append(value);
            }
        }

        public override void Flush()
        {
            if (_line.Length > 0)
            {
                Complete();
            }
        }

        private void Complete()
        {
            string text = _line.ToString();
            _line.Clear();
            captured?.AppendLine(text);
            relay?.Invoke(text);
        }
    }
}
