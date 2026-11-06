using System.ComponentModel;
using System.Diagnostics;

namespace CleanArch.Cli;

internal sealed record ExternalCall(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory, IReadOnlyDictionary<string, string>? Environment = null, string? DirectoryNote = null);

internal static class ExternalCommand
{
    internal static Func<ExternalCall, TextWriter, TextWriter, int>? Handler { get; set; }

    public static int Run(ExternalCall call, TextWriter output, TextWriter error) =>
        Run(call, output, output, error);

    public static int Run(ExternalCall call, TextWriter announce, TextWriter output, TextWriter error)
    {
        announce.WriteLine(LineOf(call));
        announce.Flush();
        if (Handler is { } handler)
        {
            return handler(call, output, error);
        }

        return RunProcess(call, output, error);
    }

    internal static string LineOf(ExternalCall call)
    {
        string line = string.Concat(call.Environment?.Select(variable => $"{variable.Key}={variable.Value} ") ?? [])
            + string.Join(' ', new[] { call.FileName }.Concat(call.Arguments.Select(Quoted)));
        return call.DirectoryNote is { } note ? $"{line}   (in {note})" : line;
    }

    private static string Quoted(string argument) =>
        argument.Length == 0 || argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;

    private static int RunProcess(ExternalCall call, TextWriter output, TextWriter error)
    {
        try
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

            if (call.Environment is { } environment)
            {
                foreach (KeyValuePair<string, string> variable in environment)
                {
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable.Key)))
                    {
                        start.Environment[variable.Key] = variable.Value;
                    }
                }
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException($"'{call.FileName}' started no process.");
            process.OutputDataReceived += (_, received) =>
            {
                if (received.Data is not null)
                {
                    output.WriteLine(received.Data);
                }
            };
            process.ErrorDataReceived += (_, received) =>
            {
                if (received.Data is not null)
                {
                    error.WriteLine(received.Data);
                }
            };
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
}
