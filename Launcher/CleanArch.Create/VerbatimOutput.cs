using Spectre.Console;

namespace CleanArch.Create;

public static class VerbatimOutput
{
    public static void Verbatim(this IAnsiConsole console, string text) => console.Profile.Out.Writer.WriteLine(text);
}
