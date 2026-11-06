using System.CommandLine;
using CleanArch.Cli.Generators.AddEvent;
using CleanArch.Cli.Generators.AddFeature;
using CleanArch.Cli.Generators.AddField;
using CleanArch.Cli.Generators.AddSetting;
using CleanArch.Cli.Generators.AddUseCase;

namespace CleanArch.Cli;

internal static class FeatureAdditions
{
    private static readonly Func<CommandContext, Command>[] _generators =
    [
        AddFeatureCommand.Build,
        AddFieldCommand.Build,
        AddUseCaseCommand.Build,
        AddSettingCommand.Build,
        AddEventCommand.Build,
    ];

    public static IEnumerable<Command> Commands(CommandContext context) => _generators.Select(build => build(context));
}
