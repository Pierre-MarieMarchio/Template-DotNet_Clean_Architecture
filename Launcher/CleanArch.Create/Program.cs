using CleanArch.Create;
using Spectre.Console;

bool interactive = !Console.IsInputRedirected && AnsiConsole.Profile.Capabilities.Interactive;

return new Launcher(AnsiConsole.Console, Environment.CurrentDirectory, interactive).Run(args);
