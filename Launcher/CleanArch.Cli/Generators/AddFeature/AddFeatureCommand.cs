using System.CommandLine;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddFeature;

internal static class AddFeatureCommand
{
    internal static Command Build(CommandContext context)
    {
        Command command = Build(context.Project, GeneratorCommand.Executor, GeneratorCommand.Version);

        GeneratorCommand.RefuseWithoutAuthentication(context, command);

        return command;
    }

    internal static Command Build(LocatedProject? project, Func<ParseResult, IExecutor> executor, string version)
    {
        var feature = new Command("feature", "Write a feature in every layer, its manifest and its page, compose it, then build, add its migration and rewrite the OpenAPI reference.");
        Argument<string> features = new("Features") { Description = "The feature's folder, PascalCase and plural, e.g. Widgets." };
        Argument<string> type = new("Feature") { Description = "The feature's type, PascalCase and singular, e.g. Widget." };
        Option<string> access = GeneratorCommand.RequiredText("--access", "owner: every operation is scoped to the caller's id. shared: every authenticated caller sees the same records.");
        Option<string?> deletion = GeneratorCommand.Text("--deletion", "With --access owner, required: delete removes the rows of an account deleted, anonymise rewrites what identifies its owner.");
        Option<string?> operations = GeneratorCommand.Text("--operations", "Of get, list, create, update and delete, comma separated; all five by default. create needs get.");
        Option<string?> model = GeneratorCommand.Text("--model", "record (default): a service over a store. aggregate: one use case per operation over a domain aggregate.");
        Option<string?> concurrency = GeneratorCommand.Text("--concurrency", "none (default): last write wins. etag: a persisted version, ETag and If-Match; aggregate needs it.");
        Option<string?> fields = GeneratorCommand.Text("--fields", "The fields, Name:type, comma separated, e.g. \"Title:string(120), Notes:text?, Budget:decimal>=0\"; Name and Description by default.");
        Option<bool> dryRun = GeneratorCommand.DryRun("Print the files and the edits, and write and run nothing.");
        feature.Arguments.Add(features);
        feature.Arguments.Add(type);
        feature.Options.Add(access);
        feature.Options.Add(deletion);
        feature.Options.Add(operations);
        feature.Options.Add(model);
        feature.Options.Add(concurrency);
        feature.Options.Add(fields);
        feature.Options.Add(dryRun);
        GeneratorCommand.SetGeneration(
            feature,
            project,
            parseResult => parseResult.GetValue(dryRun),
            executor,
            (parseResult, context) => NewFeature.Run(new AddFeatureRequest(parseResult.GetRequiredValue(features), parseResult.GetRequiredValue(type), parseResult.GetRequiredValue(access), parseResult.GetValue(deletion), parseResult.GetValue(operations), parseResult.GetValue(model), parseResult.GetValue(concurrency), parseResult.GetValue(fields)), context, version));

        return feature;
    }
}
