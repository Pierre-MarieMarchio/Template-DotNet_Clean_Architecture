using System.CommandLine;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddField;

internal static class AddFieldCommand
{
    internal static Command Build(CommandContext context)
    {
        Command command = Build(context.Project, GeneratorCommand.Executor);

        GeneratorCommand.RefuseWithoutAuthentication(context, command);

        return command;
    }

    internal static Command Build(LocatedProject? project, Func<ParseResult, IExecutor> executor)
    {
        var field = new Command("field", "Add fields to a feature add feature wrote with --fields, then build and add their migration with each default.");
        Argument<string> features = new("Features") { Description = "A feature add feature wrote with --fields, which its manifest records." };
        Argument<string> declarations = new("fields") { Description = "The fields, in the grammar of --fields; one that cannot be null takes the default the rows stored receive after '=', e.g. \"Code:string(20)='none', Notes:text?\"." };
        Option<bool> dryRun = GeneratorCommand.DryRun("Print what changes, and write and run nothing.");
        field.Arguments.Add(features);
        field.Arguments.Add(declarations);
        field.Options.Add(dryRun);
        GeneratorCommand.SetGeneration(
            field,
            project,
            parseResult => parseResult.GetValue(dryRun),
            executor,
            (parseResult, context) => NewField.Run(new AddFieldRequest(parseResult.GetRequiredValue(features), parseResult.GetRequiredValue(declarations)), context));

        return field;
    }
}
