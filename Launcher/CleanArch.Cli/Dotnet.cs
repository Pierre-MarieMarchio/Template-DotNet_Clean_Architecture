using System.CommandLine;
using System.ComponentModel;
using System.Diagnostics;

namespace CleanArch.Cli;

internal static class Dotnet
{
    public static int Run(ParseResult parseResult, string workingDirectory, IReadOnlyList<string> arguments)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;
        output.WriteLine(LineOf(arguments));
        output.Flush();

        ProcessStartInfo startInfo = new()
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("'dotnet' started no process.");
        }
        catch (Win32Exception)
        {
            error.WriteLine("'dotnet' is not on PATH.");
            return CleanArchCommand.Failed;
        }

        using (process)
        {
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            output.Write(standardOutput.GetAwaiter().GetResult());
            error.Write(standardError.GetAwaiter().GetResult());
            return process.ExitCode;
        }
    }

    public static string LineOf(IReadOnlyList<string> arguments) =>
        $"dotnet {string.Join(' ', arguments.Select(Displayed))}";

    private static string Displayed(string argument) =>
        argument.Contains(' ') ? $"\"{argument}\"" : argument;
}
