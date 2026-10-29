using CleanArch.Create;
using Spectre.Console;

IAnsiConsole console = Terminal.Open();
bool interactive = !Console.IsInputRedirected && console.Profile.Capabilities.Interactive;
var launcher = new Launcher(console, Environment.CurrentDirectory, interactive);
Console.CancelKeyPress += (_, stop) =>
{
    stop.Cancel = launcher.Interrupt();
    console.Cursor.Show();
};

return launcher.Run(args);
