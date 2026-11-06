using CleanArch.Cli.Commands.Bootstrap;
using CleanArch.Cli.Commands.Build;
using CleanArch.Cli.Commands.Compose;
using CleanArch.Cli.Commands.Coverage;
using CleanArch.Cli.Commands.Database;
using CleanArch.Cli.Commands.EndToEnd;
using CleanArch.Cli.Commands.Format;
using CleanArch.Cli.Commands.Migration;
using CleanArch.Cli.Commands.OpenApi;
using CleanArch.Cli.Commands.Run;
using CleanArch.Cli.Commands.Sonar;
using CleanArch.Cli.Commands.Test;
using CleanArch.Cli.Commands.Verify;

namespace CleanArch.Cli;

internal static class BuiltInCommands
{
    public static int Run(IReadOnlyList<string> arguments, string workingDirectory, TextWriter output, TextWriter error) =>
        Run(arguments, workingDirectory, output, error, ExternalCommand.Launcher);

    public static int Run(IReadOnlyList<string> arguments, string workingDirectory, TextWriter output, TextWriter error, IProcessLauncher processes) =>
        CleanArchCommand.Run(arguments, workingDirectory, output, error, All, processes);

    public static IReadOnlyList<CommandRegistration> All { get; } =
    [
        HelpCommand.Registration,
        FormatCommand.Registration,
        BuildCommand.Registration,
        TestCommand.Registration,
        CoverageCommand.Registration,
        VerifyCommand.Registration,
        AddCommand.Registration,
        RemoveCommand.Registration,
        MigrationsCommand.Registration,
        DatabaseCommand.Registration,
        OpenApiCommand.Registration,
        BootstrapCommand.Registration,
        RunCommand.Registration,
        ComposeCommand.Registration,
        EndToEndCommand.Registration,
        SonarCommand.Registration,
        AdoptCommand.Registration,
        EjectCommand.Registration,
    ];
}
