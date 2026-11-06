using System.CommandLine;
using CleanArch.Cli.Generators.RemoveFeature;

namespace CleanArch.Cli;

internal static class RemoveCommand
{
    public const string Name = "remove";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Build);

    private static Command Build(CommandContext context)
    {
        var remove = new Command(Name, "Remove what add wrote, as its manifest records it.");
        remove.Add(RemoveFeatureCommand.Build(context));
        remove.SetAction(GeneratorCommand.ShowHelp);
        return remove;
    }
}
