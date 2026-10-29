using System.Text;
using Spectre.Console;

namespace CleanArch.Create;

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
}
