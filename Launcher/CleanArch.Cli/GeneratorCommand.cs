using System.CommandLine;
using System.Reflection;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli;

internal static class GeneratorCommand
{
    public static string Version { get; } =
        typeof(GeneratorCommand).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? typeof(GeneratorCommand).Assembly.GetName().Version!.ToString();

    public static Option<bool> DryRun(string description) => new("--dry-run") { Description = description };

    public static Option<string?> Text(string name, string description, bool required = false) =>
        new(name) { Description = description, Required = required };

    public static IEnumerable<string> Forwarded(ParseResult parseResult, params Option<string?>[] options) =>
        options.SelectMany(option => parseResult.GetValue(option) is { } value ? [option.Name, value] : Array.Empty<string>());

    public static IEnumerable<string> Flag(ParseResult parseResult, Option<bool> option) =>
        parseResult.GetValue(option) ? [option.Name] : [];

    public static IEnumerable<string> Root(LocatedProject project) => ["--root", project.Root];

    public static Func<TextEdits.Command, int> Runner(ParseResult parseResult) =>
        command => ExternalCommand.RunAnnounced(
            new ExternalCall(command.FileName, command.Arguments, command.WorkingDirectory),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error);

    public static int ShowHelp(ParseResult parseResult)
    {
        parseResult.CommandResult.Command.Parse(["--help"]).Invoke(parseResult.InvocationConfiguration);
        return CleanArchCommand.Succeeded;
    }
}
