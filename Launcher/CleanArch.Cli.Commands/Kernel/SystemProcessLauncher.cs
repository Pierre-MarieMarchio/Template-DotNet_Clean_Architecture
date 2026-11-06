using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CleanArch.Cli.Commands.Hosting;

namespace CleanArch.Cli.Commands.Kernel;

internal sealed class SystemProcessLauncher : IProcessLauncher
{
    public int Launch(ExternalCall call, TextWriter output, TextWriter error)
    {
        try
        {
            using Process process = Process.Start(StartInfoOf(call)) ?? throw new InvalidOperationException($"'{call.FileName}' started no process.");
            process.OutputDataReceived += (_, received) => Forward(received.Data, output);
            process.ErrorDataReceived += (_, received) => Forward(received.Data, error);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Win32Exception)
        {
            error.WriteLine($"'{call.FileName}' is not on PATH.");
            return CleanArchCommand.Failed;
        }
    }

    private static ProcessStartInfo StartInfoOf(ExternalCall call)
    {
        var start = new ProcessStartInfo(call.FileName)
        {
            WorkingDirectory = call.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in call.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (KeyValuePair<string, string> variable in call.Environment ?? ReadOnlyDictionary<string, string>.Empty)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable.Key)))
            {
                start.Environment[variable.Key] = variable.Value;
            }
        }

        return start;
    }

    private static void Forward(string? line, TextWriter writer)
    {
        if (line is not null)
        {
            writer.WriteLine(line);
        }
    }
}
