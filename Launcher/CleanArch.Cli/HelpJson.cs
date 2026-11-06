using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CleanArch.Cli;

internal static class HelpJson
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int Write(CommandContext context, ParseResult parseResult)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;
        if (context.RefusedProjectFile is { } refused)
        {
            error.WriteLine(refused.Message);
            return CleanArchCommand.Failed;
        }

        if (context.Project is not { } project)
        {
            error.WriteLine(context.OutsideProject);
            return CleanArchCommand.Failed;
        }

        Command root = parseResult.RootCommandResult.Command;
        var document = new HelpDocument(
            CleanArchCommand.Name,
            ProjectSectionOf(project),
            [.. root.Options.Where(option => !option.Hidden).Select(OptionSectionOf)],
            [.. root.Subcommands.Where(command => !command.Hidden).Select(CommandSectionOf)]);
        output.WriteLine(JsonSerializer.Serialize(document, _json));
        return CleanArchCommand.Succeeded;
    }

    private static ProjectSection ProjectSectionOf(LocatedProject project)
    {
        ProjectFile file = project.File;
        var options = new Dictionary<string, JsonNode?>();
        if (file.Options.Auth is { } auth)
        {
            options["auth"] = JsonValue.Create(auth);
        }

        if (file.Options.Database is { } database)
        {
            options["database"] = JsonValue.Create(database);
        }

        if (file.Options.Examples is { } examples)
        {
            options["examples"] = JsonValue.Create(examples);
        }

        if (file.Options.Files is true)
        {
            options["files"] = JsonValue.Create(true);
        }

        if (file.Options.Ci is true)
        {
            options["ci"] = JsonValue.Create(true);
        }

        if (file.Options.Sonar is true)
        {
            options["sonar"] = JsonValue.Create(true);
        }

        if (file.Options.AgentFiles is true)
        {
            options["agentFiles"] = JsonValue.Create(true);
        }

        if (file.Options.Aspire is true)
        {
            options["aspire"] = JsonValue.Create(true);
        }

        return new ProjectSection(
            project.Root,
            file.Name,
            file.Template,
            options,
            file.Client ? true : null,
            file.Web,
            file.Presentation);
    }

    private static OptionSection OptionSectionOf(Option option) => new(option.Name, option.Description);

    private static CommandSection CommandSectionOf(Command command) => new(
        command.Name,
        command.Description,
        [.. command.Options.Where(option => !option.Hidden).Select(OptionSectionOf)],
        [.. command.Arguments.Select(argument => new ArgumentSection(argument.Name, argument.Description))],
        [.. command.Subcommands.Where(subcommand => !subcommand.Hidden).Select(CommandSectionOf)],
        command is LocalCommandLine ? LocalCommand.FileOf(command.Name) : null,
        command is LocalCommandLine { ReplacesBuiltIn: true } ? true : null);
}

internal sealed record HelpDocument(
    string Command,
    ProjectSection Project,
    IReadOnlyList<OptionSection> Options,
    IReadOnlyList<CommandSection> Commands,
    string? Local = null,
    bool? ReplacesBuiltIn = null);

internal sealed record ProjectSection(
    string Root,
    string Name,
    TemplateReference Template,
    IReadOnlyDictionary<string, JsonNode?> Options,
    bool? Client,
    WebWorkspace? Web,
    IReadOnlyList<PresentationProject>? Presentation);

internal sealed record OptionSection(string Name, string? Description);

internal sealed record ArgumentSection(string Name, string? Description);

internal sealed record CommandSection(
    string Name,
    string? Description,
    IReadOnlyList<OptionSection> Options,
    IReadOnlyList<ArgumentSection> Arguments,
    IReadOnlyList<CommandSection> Commands,
    string? Local = null,
    bool? ReplacesBuiltIn = null);
