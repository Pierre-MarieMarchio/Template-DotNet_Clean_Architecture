using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CleanArch.Create;

public sealed record ProcessCall(string FileName, string WorkingDirectory, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment);

public sealed record ProcessResult(int ExitCode, string Output);

public interface IProcessRunner
{
    ProcessResult Run(ProcessCall command, Action<string>? line);
}

public sealed class ProcessRunner : IProcessRunner
{
    private readonly Lock _output = new();

    public ProcessResult Run(ProcessCall command, Action<string>? line)
    {
        var start = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach ((string name, string value) in command.Environment)
        {
            start.Environment[name] = value;
        }

        foreach (string argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        var captured = new StringBuilder();
        using var process = new Process { StartInfo = start };
        DataReceivedEventHandler collect = (_, received) =>
        {
            if (received.Data is null)
            {
                return;
            }

            lock (_output)
            {
                captured.AppendLine(received.Data);
                line?.Invoke(received.Data);
            }
        };
        process.OutputDataReceived += collect;
        process.ErrorDataReceived += collect;

        try
        {
            process.Start();
        }
        catch (Win32Exception)
        {
            throw new LauncherException($"{command.FileName} could not be started: it must be on the PATH.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, captured.ToString());
    }
}
