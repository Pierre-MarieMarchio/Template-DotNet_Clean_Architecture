using System.CommandLine;

namespace CleanArch.Cli;

internal static class Dotnet
{
    public static int Run(ParseResult parseResult, string workingDirectory, IReadOnlyList<string> arguments) =>
        ExternalCommand.Run(
            new ExternalCall("dotnet", arguments, workingDirectory),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error);
}
