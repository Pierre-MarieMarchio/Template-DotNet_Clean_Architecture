using System.ComponentModel;
using System.Diagnostics;

namespace CleanArch.Cli;

internal sealed record ExternalCall(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory);

internal static class ExternalCommand
{
    internal static Func<ExternalCall, TextWriter, TextWriter, int>? Handler { get; set; }

    public static int Run(ExternalCall call, TextWriter output, TextWriter error)
    {
        output.WriteLine(LineOf(call));
        output.Flush();
        if (Handler is { } handler)
        {
            return handler(call, output, error);
        }

        return RunProcess(call, error);
    }

    internal static string LineOf(ExternalCall call) =>
        string.Join(' ', new[] { call.FileName }.Concat(call.Arguments.Select(Quoted)));

    private static string Quoted(string argument) =>
        argument.Length == 0 || argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;

    private static int RunProcess(ExternalCall call, TextWriter error)
    {
        try
        {
            var start = new ProcessStartInfo(call.FileName)
            {
                WorkingDirectory = call.WorkingDirectory,
                UseShellExecute = false,
            };
            foreach (string argument in call.Arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException($"'{call.FileName}' started no process.");
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
