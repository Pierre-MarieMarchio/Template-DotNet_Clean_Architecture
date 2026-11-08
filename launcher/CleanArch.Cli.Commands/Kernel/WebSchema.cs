namespace CleanArch.Cli.Commands.Kernel;

internal static class WebSchema
{
    public static bool IsGenerated(LocatedProject project) => File.Exists(Path.Combine(project.Root, ProjectLayout.WebSchemaScript));

    public static ExternalCall Check(LocatedProject project, string reference) =>
        Pnpm.In(project, ProjectLayout.ApiClientPackage, "exec", "node", "scripts/generate.mjs", "--check", reference);
}
