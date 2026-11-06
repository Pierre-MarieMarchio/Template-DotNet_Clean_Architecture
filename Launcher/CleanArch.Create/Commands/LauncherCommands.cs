using CleanArch.Create.Commands.Join;
using CleanArch.Create.Commands.NewProject;

namespace CleanArch.Create.Commands;

public static class LauncherCommands
{
    public const string Invocation = "dnx CleanArch.Create";

    public const string InvocationWithArguments = Invocation + " " + Arguments.Separator;

    public static readonly ILauncherCommand Default = new NewCommand();

    public static readonly IReadOnlyList<ILauncherCommand> InsideAProject = [new WebAppCommand(), new WebPackageCommand(), new PresentationCommand()];

    public static readonly IReadOnlyList<ILauncherCommand> All = [Default, .. InsideAProject];

    public static readonly IReadOnlyList<string> Names = [.. All.Select(command => command.Name)];

    public static ILauncherCommand Named(string? name) =>
        All.FirstOrDefault(command => command.Name == name) ?? Default;
}
