namespace CleanArch.Create.Commands;

public static class LauncherCommands
{
    public const string Invocation = "dnx CleanArch.Create";

    public const string InvocationWithArguments = Invocation + " " + Arguments.Separator;

    public const string ToolInvocation = "dotnet cleanarch";

    public static readonly IReadOnlyList<string> Additions = ["web-app", "web-package", "presentation"];
}
