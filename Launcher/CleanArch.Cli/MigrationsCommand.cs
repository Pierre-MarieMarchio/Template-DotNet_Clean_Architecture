using System.CommandLine;

namespace CleanArch.Cli;

internal static class MigrationsCommand
{
    public const string Name = "migrations";

    public static readonly CommandRegistration Registration = new(Refusal, Build);

    private static string? Refusal(CommandContext context) =>
        context.Project is null ? context.RefusedProjectFile?.Message ?? context.OutsideProject : null;

    private static Command Build(CommandContext context)
    {
        var migrations = new Command(Name, "Add a migration to a persistence context, or bundle the migrations to run them on their own.");
        migrations.Add(AddSubcommand(context));
        migrations.Add(BundleSubcommand(context));
        migrations.SetAction(parseResult =>
        {
            parseResult.CommandResult.Command.Parse(["--help"]).Invoke(parseResult.InvocationConfiguration);
            return CleanArchCommand.Succeeded;
        });
        return migrations;
    }

    private static Command AddSubcommand(CommandContext context)
    {
        var add = new Command("add", "Add a migration to the business context, or to the identity context with --context auth.");
        Argument<string> name = new("name") { Description = "The migration's name, as dotnet ef names it, e.g. AddTodoItemPriority." };
        add.Arguments.Add(name);
        Option<bool> check = new("--check")
        {
            Description = "Fail when the context's model holds a change no migration holds yet, instead of writing the migration.",
        };
        add.Options.Add(check);
        Option<string?> contextOption = PersistenceContexts.ContextOption(context);
        add.Options.Add(contextOption);
        if (context.Project is { } project)
        {
            add.SetAction(parseResult => RunAdd(project, parseResult, name, check, contextOption));
        }

        return add;
    }

    private static int RunAdd(LocatedProject project, ParseResult parseResult, Argument<string> name, Option<bool> check, Option<string?> contextOption)
    {
        string target = PersistenceContexts.Target(project, parseResult.GetValue(contextOption), out string? refusal);
        if (refusal is not null)
        {
            parseResult.InvocationConfiguration.Error.WriteLine(refusal);
            return CleanArchCommand.Failed;
        }

        string migration = parseResult.GetRequiredValue(name);
        IReadOnlyList<string> arguments = parseResult.GetValue(check)
            ? ["ef", "migrations", "has-pending-model-changes", "--project", target, "--startup-project", target]
            : ["ef", "migrations", "add", migration, "--project", target, "--startup-project", target];
        return ExternalCommand.Run(new ExternalCall("dotnet", arguments, project.Root), parseResult.InvocationConfiguration.Output, parseResult.InvocationConfiguration.Error);
    }

    private static Command BundleSubcommand(CommandContext context)
    {
        var bundle = new Command("bundle", "Bundle each context's migrations into a self-contained executable under artifacts/migrate.");
        if (context.Project is { } project)
        {
            bundle.SetAction(parseResult => RunBundle(project, parseResult));
        }

        return bundle;
    }

    private static int RunBundle(LocatedProject project, ParseResult parseResult)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;
        string auth = ProjectLayout.Auth(project);
        foreach (string context in ProjectLayout.Contexts(project))
        {
            string executable = context == auth ? "efbundle-identity" : "efbundle";
            int exit = ExternalCommand.Run(
                new ExternalCall(
                    "dotnet",
                    [
                        "ef", "migrations", "bundle",
                        "--project", context,
                        "--startup-project", context,
                        "--configuration", "Debug",
                        "--self-contained",
                        "--force",
                        "--output", Path.Combine("artifacts", "migrate", executable),
                    ],
                    project.Root),
                output,
                error);
            if (exit != CleanArchCommand.Succeeded)
            {
                return exit;
            }
        }

        return CleanArchCommand.Succeeded;
    }
}
