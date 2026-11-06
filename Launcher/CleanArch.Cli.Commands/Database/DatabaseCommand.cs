using System.CommandLine;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Database;

internal static class DatabaseCommand
{
    public const string Name = "database";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private static Command Create(CommandContext context) =>
        CommandGroup.Of(Name, "Apply the project's migrations to its database, as dotnet ef does.", UpdateSubcommand(context));

    private static Command UpdateSubcommand(CommandContext context)
    {
        var update = new Command("update", "Apply the pending migrations of the business context, or of the identity context with --context auth.");
        Option<string?> contextOption = PersistenceContexts.ContextOption(context.Project);
        update.Options.Add(contextOption);
        if (context.Project is { } project)
        {
            update.SetAction(parseResult => RunUpdate(project, context.Shell(parseResult), parseResult.GetValue(contextOption)));
        }

        return update;
    }

    private static int RunUpdate(LocatedProject project, Shell shell, string? requested)
    {
        string target = PersistenceContexts.Target(project, requested, out string? refusal);
        refusal ??= PersistenceContexts.WithoutInitialMigration(project, $"{Tool.Name} {Name} update", [target]);
        return refusal is null
            ? shell.Run(Dotnet.Ef(project.Root, target, ["database", "update"]))
            : shell.Fail(refusal);
    }
}
