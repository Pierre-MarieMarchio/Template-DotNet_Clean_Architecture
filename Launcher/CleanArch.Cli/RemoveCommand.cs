using System.CommandLine;
using CleanArch.Cli.Generators;

namespace CleanArch.Cli;

internal static class RemoveCommand
{
    public const string Name = "remove";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Build);

    private static Command Build(CommandContext context)
    {
        var remove = new Command(Name, "Remove what add wrote, as its manifest records it.");
        remove.Add(FeatureCommand(context));
        remove.SetAction(GeneratorCommand.ShowHelp);
        return remove;
    }

    private static Command FeatureCommand(CommandContext context)
    {
        var feature = new Command("feature", "Report what removing a feature involves; with --apply, remove it, edited or not, then build, add its migration and rewrite the OpenAPI reference.");
        Argument<string> features = new("Features") { Description = "The feature's folder, PascalCase and plural, e.g. Widgets." };
        Option<bool> apply = new("--apply")
        {
            Description = "Delete the feature's files and page and undo each recorded edit, refused while what it changes is not committed; without it, report and write nothing.",
        };
        feature.Arguments.Add(features);
        feature.Options.Add(apply);
        if (context.Project is { } project)
        {
            feature.SetAction(parseResult => RemoveFeature.Run(
                [
                    parseResult.GetRequiredValue(features),
                    .. GeneratorCommand.Flag(parseResult, apply),
                    .. GeneratorCommand.Root(project),
                ],
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                GeneratorCommand.Runner(parseResult)));
        }

        return feature;
    }
}
