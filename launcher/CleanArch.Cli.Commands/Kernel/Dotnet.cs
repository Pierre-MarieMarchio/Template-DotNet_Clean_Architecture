namespace CleanArch.Cli.Commands.Kernel;

internal static class Dotnet
{
    private static readonly string[] _styleRulesTheBuildLacks = ["IDE0003", "IDE0049"];

    public static ExternalCall Call(string workingDirectory, params IReadOnlyList<string> arguments) => new("dotnet", arguments, workingDirectory);

    public static ExternalCall RunFile(string workingDirectory, string file, params IReadOnlyList<string> arguments) =>
        Call(workingDirectory, ["run", file, .. arguments]);

    public static ExternalCall Build(string root, string target, string configuration, params IReadOnlyList<string> options) =>
        Call(root, ["build", target, "--configuration", configuration, .. options]);

    public static ExternalCall Test(string root, string target, string configuration, params IReadOnlyList<string> options) =>
        Call(root, ["test", "--solution", target, "--configuration", configuration, .. options]);

    public static ExternalCall StyleCheck(string root, string target, params IReadOnlyList<string> options) =>
        Call(root, ["format", target, "--diagnostics", .. _styleRulesTheBuildLacks, "--verify-no-changes", .. options]);

    public static ExternalCall Ef(string root, string context, IReadOnlyList<string> command, params IReadOnlyList<string> options) =>
        Call(root, ["ef", .. command, "--project", context, "--startup-project", context, .. options]);
}
