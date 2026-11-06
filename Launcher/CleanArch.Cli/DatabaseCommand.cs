using System.CommandLine;

namespace CleanArch.Cli;

internal static class DatabaseCommand
{
    public const string Name = "database";

    public static readonly CommandRegistration Registration = new(Refusal, Build);

    private static string? Refusal(CommandContext context) =>
        context.Project is null ? context.RefusedProjectFile?.Message ?? context.OutsideProject : null;

    private static Command Build(CommandContext context)
    {
        var database = new Command(Name, "Apply the project's migrations to its database, as dotnet ef does.");
        database.Add(UpdateCommand(context));
        database.SetAction(parseResult =>
        {
            parseResult.CommandResult.Command.Parse(["--help"]).Invoke(parseResult.InvocationConfiguration);
            return CleanArchCommand.Succeeded;
        });
        return database;
    }

    private static Command UpdateCommand(CommandContext context)
    {
        var update = new Command("update", "Apply the pending migrations of the business context, or of the identity context with --context auth.");
        Option<string?> contextOption = PersistenceContexts.ContextOption(context);
        update.Options.Add(contextOption);
        if (context.Project is { } project)
        {
            update.SetAction(parseResult => RunUpdate(project, parseResult, contextOption));
        }

        return update;
    }

    private static int RunUpdate(LocatedProject project, ParseResult parseResult, Option<string?> contextOption)
    {
        string target = PersistenceContexts.Target(project, parseResult.GetValue(contextOption), out string? refusal);
        if (refusal is not null)
        {
            parseResult.InvocationConfiguration.Error.WriteLine(refusal);
            return CleanArchCommand.Failed;
        }

        return ExternalCommand.Run(
            new ExternalCall("dotnet", ["ef", "database", "update", "--project", target, "--startup-project", target], project.Root),
            parseResult.InvocationConfiguration.Output,
            parseResult.InvocationConfiguration.Error);
    }
}
