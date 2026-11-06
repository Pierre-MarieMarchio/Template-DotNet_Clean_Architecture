using System.CommandLine;

namespace CleanArch.Cli;

internal static class PersistenceContexts
{
    public static Option<string?> ContextOption(CommandContext context)
    {
        var option = new Option<string?>("--context")
        {
            Description = "The persistence context to target: auth for the identity context, which only a project with local accounts holds; the business context when left out.",
        };
        if (context.Project is { } project && project.File.Options.Auth != "local")
        {
            option.Hidden = true;
        }

        return option;
    }

    public static string Target(LocatedProject project, string? requested, out string? refusal)
    {
        if (requested is null)
        {
            refusal = null;
            return ProjectLayout.Persistence(project);
        }

        if (requested == "auth")
        {
            refusal = project.File.Options.Auth == "local"
                ? null
                : "--context auth targets the identity context, which only a project generated with --auth local holds; "
                + "this one keeps no accounts of its own. Leave --context out for the business context.";
            return refusal is null ? ProjectLayout.Auth(project) : ProjectLayout.Persistence(project);
        }

        refusal = $"'{requested}' is not a context of this project. Leave --context out for the business context, or give auth for the identity context.";
        return ProjectLayout.Persistence(project);
    }

}
