namespace CleanArch.Cli.Commands.Kernel;

internal static class Refusals
{
    public static string? WithoutApi(LocatedProject project, string command) =>
        ProjectLayout.HoldsTheApi(project)
            ? null
            : $"This project has no {command} command: its {ProjectLocator.FileName} records no presentation of framework api.";
}
