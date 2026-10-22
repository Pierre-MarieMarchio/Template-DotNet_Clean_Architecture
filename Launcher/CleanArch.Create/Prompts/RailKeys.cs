using Spectre.Console;

namespace CleanArch.Create.Prompts;

public static class RailKeys
{
    public static ConsoleKeyInfo Read(IAnsiConsole console) =>
        console.Input.ReadKey(intercept: true)
            ?? throw new LauncherException("The terminal closed before the question was answered.");
}
