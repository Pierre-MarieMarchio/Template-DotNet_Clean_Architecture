using CleanArch.Create;
using Spectre.Console;

IAnsiConsole console = Terminal.Open();
bool interactive = !Console.IsInputRedirected && console.Profile.Capabilities.Interactive;
Console.CancelKeyPress += (_, _) => console.Cursor.Show();

return new Launcher(console, Environment.CurrentDirectory, interactive).Run(args);
