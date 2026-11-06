using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Hosting;

internal static class ProjectCommand
{
    public static string? Refusal(CommandContext context) =>
        context.Project is null ? context.RefusedProjectFile?.Message ?? context.OutsideProject : null;

    public static Func<CommandContext, string?> Requiring(Func<LocatedProject, string?> refusal) =>
        context => context.Project is { } project ? refusal(project) : Refusal(context);
}
