using System.CommandLine;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.RemoveFeature;

internal static class RemoveFeatureCommand
{
    internal static Command Build(CommandContext context) => Build(context.Project, GeneratorCommand.Executor);

    internal static Command Build(LocatedProject? project, Func<ParseResult, IExecutor> executor)
    {
        var feature = new Command("feature", "Report what removing a feature involves; with --apply, remove it, edited or not, then build, add its migration and rewrite the OpenAPI reference.");
        Argument<string> features = new("Features") { Description = "The feature's folder, PascalCase and plural, e.g. Widgets." };
        Option<bool> apply = new("--apply")
        {
            Description = "Delete the feature's files and page and undo each recorded edit, refused while what it changes is not committed; without it, report and write nothing.",
        };
        feature.Arguments.Add(features);
        feature.Options.Add(apply);
        GeneratorCommand.SetGeneration(
            feature,
            project,
            parseResult => !parseResult.GetValue(apply),
            executor,
            (parseResult, context) => FeatureRemoval.Run(new RemoveFeatureRequest(parseResult.GetRequiredValue(features)), context));

        return feature;
    }
}
