using System.CommandLine;
using System.Text.RegularExpressions;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Migration;

internal static partial class MigrationsCommand
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
        Argument<string?> name = new("name")
        {
            Description = "The migration's name, as dotnet ef names it, e.g. AddTodoItemPriority; --check needs none.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        add.Arguments.Add(name);
        Option<bool> check = new("--check")
        {
            Description = "Fail when the context's model holds a change no migration holds yet, instead of writing the migration.",
        };
        add.Options.Add(check);
        Option<bool> noBuild = new("--no-build")
        {
            Description = "Read the model from the context's last build, as dotnet ef --no-build does.",
        };
        add.Options.Add(noBuild);
        Option<string?> contextOption = PersistenceContexts.ContextOption(context);
        add.Options.Add(contextOption);
        if (context.Project is { } project)
        {
            add.SetAction(parseResult => RunAdd(project, context.Shell(parseResult), parseResult, new AddOptions(name, check, noBuild, contextOption)));
        }

        return add;
    }

    private sealed record AddOptions(Argument<string?> Name, Option<bool> Check, Option<bool> NoBuild, Option<string?> Context);

    private static int RunAdd(LocatedProject project, Shell shell, ParseResult parseResult, AddOptions options)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        bool check = parseResult.GetValue(options.Check);
        string? migration = parseResult.GetValue(options.Name);
        string target = PersistenceContexts.Target(project, parseResult.GetValue(options.Context), out string? refusal);
        if (!check && string.IsNullOrWhiteSpace(migration))
        {
            refusal = "migrations add needs the migration's name, e.g. AddTodoItemPriority; only --check runs without one.";
        }

        refusal ??= PersistenceContexts.WithoutInitialMigration(project, $"{Name} add", [target]);
        if (refusal is not null)
        {
            error.WriteLine(refusal);
            return CleanArchCommand.Failed;
        }

        string[] noBuild = parseResult.GetValue(options.NoBuild) ? ["--no-build"] : [];
        if (check)
        {
            return shell.Run(new ExternalCall("dotnet", ["ef", "migrations", "has-pending-model-changes", "--project", target, "--startup-project", target, .. noBuild], project.Root));
        }

        string folder = ProjectLayout.Migrations(project, target);
        string snapshotPath = Directory.EnumerateFiles(folder, "*ModelSnapshot.cs").Single();
        byte[] snapshot = File.ReadAllBytes(snapshotPath);
        HashSet<string> before = [.. Directory.EnumerateFiles(folder, "*.cs")];
        int exit = shell.Run(new ExternalCall("dotnet", ["ef", "migrations", "add", migration!, "--project", target, "--startup-project", target, .. noBuild], project.Root));
        if (exit != CleanArchCommand.Succeeded)
        {
            return exit;
        }

        string[] written = [.. Directory.EnumerateFiles(folder, "*.cs").Where(file => !before.Contains(file))];
        string? writtenMigration = written.SingleOrDefault(file => !file.EndsWith(".Designer.cs", StringComparison.Ordinal));
        if (writtenMigration is null || !IsEmptyMigration(File.ReadAllText(writtenMigration)))
        {
            return exit;
        }

        foreach (string file in written)
        {
            File.Delete(file);
        }

        File.WriteAllBytes(snapshotPath, snapshot);
        error.WriteLine(
            $"The model of {Path.GetFileName(target)} has not changed since its last migration; nothing is written. "
            + "A new entity needs its schema constant, its set and its ApplyConfiguration call in the context (docs/guides/migrations.md).");
        return CleanArchCommand.Failed;
    }

    internal static bool IsEmptyMigration(string source) => EmptyMigrationMethod().Count(source) == 2;

    [GeneratedRegex(@"void\s+(?:Up|Down)\s*\(\s*MigrationBuilder\s+\w+\s*\)\s*\{\s*\}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex EmptyMigrationMethod();

    private static Command BundleSubcommand(CommandContext context)
    {
        var bundle = new Command("bundle", "Bundle each context's migrations into a self-contained executable under artifacts/migrate.");
        Option<string> configuration = BuildSettings.AddConfiguration(bundle);
        if (context.Project is { } project)
        {
            bundle.SetAction(parseResult => RunBundle(project, context.Shell(parseResult), parseResult, configuration));
        }

        return bundle;
    }

    private static int RunBundle(LocatedProject project, Shell shell, ParseResult parseResult, Option<string> configuration)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        if (PersistenceContexts.WithoutInitialMigration(project, $"{Name} bundle", ProjectLayout.Contexts(project)) is { } refusal)
        {
            error.WriteLine(refusal);
            return CleanArchCommand.Failed;
        }

        string auth = ProjectLayout.Auth(project);
        foreach (string context in ProjectLayout.Contexts(project))
        {
            string executable = context == auth ? "efbundle-identity" : "efbundle";
            int exit = shell.Run(new ExternalCall(
                    "dotnet",
                    [
                        "ef", "migrations", "bundle",
                        "--project", context,
                        "--startup-project", context,
                        "--configuration", parseResult.GetRequiredValue(configuration),
                        "--self-contained",
                        "--force",
                        "--output", Path.Combine("artifacts", "migrate", executable),
                    ],
                    project.Root));
            if (exit != CleanArchCommand.Succeeded)
            {
                return exit;
            }
        }

        return CleanArchCommand.Succeeded;
    }
}
