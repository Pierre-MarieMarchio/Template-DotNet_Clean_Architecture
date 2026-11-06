namespace CleanArch.Cli;

internal static class BuiltInCommands
{
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
