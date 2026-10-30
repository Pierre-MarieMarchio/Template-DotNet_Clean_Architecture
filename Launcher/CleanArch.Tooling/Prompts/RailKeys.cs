using Spectre.Console;

namespace CleanArch.Tooling.Prompts;

public static class RailKeys
{
    public const string Back = " · esc back";
    public const string BackOrLeft = " · ←/esc back";

    public static ConsoleKeyInfo Read(IAnsiConsole console) =>
        console.Input.ReadKey(intercept: true)
            ?? throw new LauncherException("The terminal closed before the question was answered.");
}
