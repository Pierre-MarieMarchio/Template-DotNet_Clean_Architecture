using System.CommandLine;
using System.Text.RegularExpressions;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Migration;

internal static partial class MigrationsCommand
{
    public const string Name = "migrations";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

    private sealed record AddOptions(Argument<string?> Migration, Option<bool> Check, Option<bool> NoBuild, Option<string?> Context);

    private static Command Create(CommandContext context) =>
        CommandGroup.Of(
            Name,
            "Add a migration to a persistence context, or bundle the migrations to run them on their own.",
            AddSubcommand(context),
            BundleSubcommand(context));

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
        Option<string?> contextOption = PersistenceContexts.ContextOption(context.Project);
        add.Options.Add(contextOption);
        if (context.Project is { } project)
        {
            add.SetAction(parseResult => RunAdd(project, context.Shell(parseResult), parseResult, new AddOptions(name, check, noBuild, contextOption)));
        }

        return add;
    }

    private static int RunAdd(LocatedProject project, Shell shell, ParseResult parseResult, AddOptions options)
    {
        bool check = parseResult.GetValue(options.Check);
        string? migration = parseResult.GetValue(options.Migration);
        string target = PersistenceContexts.Target(project, parseResult.GetValue(options.Context), out string? refusal);
        if (!check && string.IsNullOrWhiteSpace(migration))
        {
            refusal = "migrations add needs the migration's name, e.g. AddTodoItemPriority; only --check runs without one.";
        }

        refusal ??= PersistenceContexts.WithoutInitialMigration(project, $"{Tool.Name} {Name} add", [target]);
        if (refusal is not null)
        {
            return shell.Fail(refusal);
        }

        string[] noBuild = parseResult.GetValue(options.NoBuild) ? ["--no-build"] : [];
        return check
            ? shell.Run(Dotnet.Ef(project.Root, target, ["migrations", "has-pending-model-changes"], noBuild))
            : Add(project, shell, target, migration!, noBuild);
    }

    private static int Add(LocatedProject project, Shell shell, string target, string migration, string[] noBuild)
    {
        string folder = ProjectLayout.Migrations(project, target);
        string snapshotPath = Directory.EnumerateFiles(folder, "*ModelSnapshot.cs").Single();
        byte[] snapshot = File.ReadAllBytes(snapshotPath);
        HashSet<string> before = [.. Directory.EnumerateFiles(folder, "*.cs")];
        int exit = shell.Run(Dotnet.Ef(project.Root, target, ["migrations", "add", migration], noBuild));
        if (exit != ExitCode.Succeeded)
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
        return shell.Fail(
            $"The model of {Path.GetFileName(target)} has not changed since its last migration; nothing is written. "
            + "A new entity needs its schema constant, its set and its ApplyConfiguration call in the context (docs/guides/migrations.md).");
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
            bundle.SetAction(parseResult => RunBundle(project, context.Shell(parseResult), parseResult.GetRequiredValue(configuration)));
        }

        return bundle;
    }

    private static int RunBundle(LocatedProject project, Shell shell, string configuration)
    {
        if (PersistenceContexts.WithoutInitialMigration(project, $"{Tool.Name} {Name} bundle", ProjectLayout.Contexts(project)) is { } refusal)
        {
            return shell.Fail(refusal);
        }

        string auth = ProjectLayout.Auth(project);
        return ExitCode.FirstFailureOf(ProjectLayout.Contexts(project).Select(context => (Func<int>)(() => shell.Run(Dotnet.Ef(
            project.Root,
            context,
            ["migrations", "bundle"],
            "--configuration", configuration,
            "--self-contained",
            "--force",
            "--output", Path.Combine("artifacts", "migrate", context == auth ? "efbundle-identity" : "efbundle"))))));
    }
}
