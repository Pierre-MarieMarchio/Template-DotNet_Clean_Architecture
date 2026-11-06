using System.CommandLine;
using System.Text.RegularExpressions;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Hosting;

internal sealed partial record LocalCommand(string Name, string Source)
{
    public const string Folder = "Tools";

    private const string _helpStaysBuiltIn = "help";

    public static IReadOnlyList<LocalCommand> In(LocatedProject? project)
    {
        if (project is null || !Directory.Exists(Path.Combine(project.Root, Folder)))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(Path.Combine(project.Root, Folder), "*.cs")
            .Select(source => new LocalCommand(Path.GetFileNameWithoutExtension(source), source))
            .Where(local => CommandName().IsMatch(local.Name) && local.Name != _helpStaysBuiltIn)
            .OrderBy(local => local.Name, StringComparer.Ordinal)];
    }

    public static string FileOf(string name) => $"{Folder}/{name}.cs";

    public int Run(CommandContext context, IEnumerable<string> arguments, TextWriter output, TextWriter error)
    {
        string file = Path.GetRelativePath(context.WorkingDirectory, Source).Replace('\\', '/');
        string[] forwarded = [.. arguments];
        string[] dotnetRun = forwarded.Length == 0 ? ["run", file] : ["run", file, "--", .. forwarded];
        return context.Shell(output, error).Run(Dotnet.Call(context.WorkingDirectory, dotnetRun));
    }

    [GeneratedRegex("^[a-z][a-z0-9]*(-[a-z0-9]+)*$")]
    private static partial Regex CommandName();
}

internal sealed class LocalCommandLine : Command
{
    public LocalCommandLine(LocalCommand local, CommandContext context, bool replacesBuiltIn)
        : base(local.Name, replacesBuiltIn
            ? $"{LocalCommand.FileOf(local.Name)}, this project's own command, run in place of the built-in one."
            : $"{LocalCommand.FileOf(local.Name)}, this project's own command.")
    {
        ReplacesBuiltIn = replacesBuiltIn;
        TreatUnmatchedTokensAsErrors = false;
        SetAction(parseResult => local.Run(
            context,
            parseResult.UnmatchedTokens,
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error));
    }

    public bool ReplacesBuiltIn { get; }
}
