using System.CommandLine;

namespace CleanArch.Cli.Commands.Kernel;

internal static class PersistenceContexts
{
    public static Option<string?> ContextOption(LocatedProject? project) =>
        new("--context")
        {
            Description = "The persistence context to target: auth for the identity context, which only a project with local accounts holds; the business context when left out.",
            Hidden = project is not null && !KeepsAccounts(project),
        };

    public static string Target(LocatedProject project, string? requested, out string? refusal)
    {
        refusal = requested switch
        {
            null => null,
            "auth" when KeepsAccounts(project) => null,
            "auth" => "--context auth targets the identity context, which only a project generated with --auth local holds; "
                + "this one keeps no accounts of its own. Leave --context out for the business context.",
            _ => $"'{requested}' is not a context of this project. Leave --context out for the business context, or give auth for the identity context.",
        };
        return requested == "auth" && refusal is null ? ProjectLayout.Auth(project) : ProjectLayout.Persistence(project);
    }

    public static bool HasInitialMigration(LocatedProject project, string context)
    {
        string folder = ProjectLayout.Migrations(project, context);
        return Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*ModelSnapshot.cs").Any();
    }

    public static string? WithoutInitialMigration(LocatedProject project, string invocation, IEnumerable<string> contexts)
    {
        string[] missing = [.. contexts.Where(context => !HasInitialMigration(project, context)).Select(Path.GetFileName).OfType<string>()];
        return missing.Length == 0
            ? null
            : $"'{invocation}' needs the initial migration of {string.Join(" and ", missing)}, and there is none yet. "
            + $"Run `{Tool.Name} bootstrap`, which creates it, then commit it (docs/guides/migrations.md).";
    }

    private static bool KeepsAccounts(LocatedProject project) => project.File.Options.Auth == "local";
}
