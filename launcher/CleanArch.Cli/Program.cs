using CleanArch.Cli;

return BuiltInCommands.Run(args, Environment.CurrentDirectory, Console.Out, Console.Error, new SystemProcessLauncher());
