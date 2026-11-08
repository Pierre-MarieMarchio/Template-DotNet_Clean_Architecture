using System.Text;
using Spectre.Console;

namespace CleanArch.Tooling.Output;

public static class Terminal
{
    public const int UnwrappedWidth = 4096;

    public static IAnsiConsole Open()
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        IAnsiConsole console = AnsiConsole.Console;
        if (Console.IsOutputRedirected)
        {
            console.Profile.Width = UnwrappedWidth;
        }

        return console;
    }

    public static IAnsiConsole Over(TextWriter output)
    {
        if (ReferenceEquals(output, Console.Out))
        {
            return Open();
        }

        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output),
            Interactive = InteractionSupport.No,
        });
        console.Profile.Width = UnwrappedWidth;
        return console;
    }
}
