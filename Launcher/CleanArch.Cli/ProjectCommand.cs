namespace CleanArch.Cli;

internal static class ProjectCommand
{
    public static string? Refusal(CommandContext context) =>
        context.Project is null ? context.RefusedProjectFile?.Message ?? context.OutsideProject : null;
}
