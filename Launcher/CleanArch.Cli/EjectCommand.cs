using System.CommandLine;
using System.Reflection;
using System.Text;

namespace CleanArch.Cli;

internal static class EjectCommand
{
    public const string Name = "eject";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private static readonly string[] _staysTheTools = [nameof(HelpCommand), nameof(EjectCommand), nameof(AdoptCommand), nameof(AddCommand), nameof(RemoveCommand)];

    public static string CommandLineVersion { get; } =
        typeof(EjectCommand).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(metadata => metadata.Key == "System.CommandLine").Value
        ?? throw new InvalidOperationException("The tool records no version of System.CommandLine.");

    public static IEnumerable<CommandRegistration> Ejectable() =>
        BuiltInCommands.All.Where(registration => !_staysTheTools.Contains(CommandSource.TypeOf(registration), StringComparer.Ordinal));

    private static IEnumerable<CommandRegistration> Applicable(CommandContext context) =>
        Ejectable().Where(registration => registration.Refusal(context) is null);

    private static Command Create(CommandContext context)
    {
        var command = new Command(Name, "Copy a built-in command's source into Tools/, where it becomes this project's own command.");
        string[] applicable = [.. Applicable(context).Select(registration => registration.Build(context).Name)];
        Argument<string> ejected = new("command") { Description = $"The built-in command to copy: {string.Join(", ", applicable)}." };
        ejected.CompletionSources.Add(applicable);
        Option<bool> dryRun = new("--dry-run") { Description = "Print what the command would write, and write nothing." };
        command.Arguments.Add(ejected);
        command.Options.Add(dryRun);
        command.SetAction(parseResult => Run(context, parseResult, ejected, dryRun));
        return command;
    }

    private static int Run(CommandContext context, ParseResult parseResult, Argument<string> ejected, Option<bool> dryRun)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;
        LocatedProject project = context.Project!;
        string name = parseResult.GetRequiredValue(ejected);
        if (BuiltInCommands.All.FirstOrDefault(candidate => candidate.Build(context).Name == name) is not { } registration)
        {
            error.WriteLine($"{name} is no built-in command.");
            return CleanArchCommand.Failed;
        }

        if (!Ejectable().Contains(registration))
        {
            error.WriteLine($"{name} stays the tool's: help says which commands are this project's own, eject makes them, and adopt runs before the tool knows the project.");
            return CleanArchCommand.Failed;
        }

        if (registration.Refusal(context) is { } refusal)
        {
            error.WriteLine($"{name} does not apply to this project, so there is nothing to eject: {refusal}");
            return CleanArchCommand.Failed;
        }

        string relative = LocalCommand.FileOf(name);
        string path = Path.Combine(project.Root, relative);
        if (File.Exists(path))
        {
            error.WriteLine($"{relative} exists: {name} is this project's own command already.");
            return CleanArchCommand.Failed;
        }

        string type = CommandSource.TypeOf(registration);
        string app = CommandSource.FileBasedApp(name, type, CommandLineVersion);
        string sources = string.Join(", ", CommandSource.FilesOf(type));
        if (parseResult.GetValue(dryRun))
        {
            output.WriteLine($"Would write {relative}, {LinesOf(app)} lines from the tool's {sources}.");
            return CleanArchCommand.Succeeded;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, app, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        output.WriteLine($"Wrote {relative}, {LinesOf(app)} lines from the tool's {sources}.");
        output.WriteLine($"{CleanArchCommand.Name} {name} now runs it, as dotnet run {relative} does, and no longer follows the tool's releases.");
        return CleanArchCommand.Succeeded;
    }

    private static int LinesOf(string text) => text.Count(character => character == '\n');
}
