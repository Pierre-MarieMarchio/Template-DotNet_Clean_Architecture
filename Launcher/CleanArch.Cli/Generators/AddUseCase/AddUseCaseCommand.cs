using System.CommandLine;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddUseCase;

internal static class AddUseCaseCommand
{
    internal static Command Build(CommandContext context) => Build(context.Project, GeneratorCommand.Executor);

    internal static Command Build(LocatedProject? project, Func<ParseResult, IExecutor> executor)
    {
        var useCase = new Command("use-case", "Add one operation to a feature of the rich shape: its message, handler, validator and tests, and its action where the API serves the feature.");
        Argument<string> features = new("Features") { Description = "The folder of an existing feature of the rich shape, e.g. Invoices." };
        Argument<string> name = new("UseCase") { Description = "The operation, PascalCase, a verb and its subject, e.g. ApproveInvoice." };
        Option<string> kind = GeneratorCommand.RequiredText("--kind", "command: changes state and answers a Result. query: reads, and answers a Result of a DTO named for it.");
        Option<string?> input = GeneratorCommand.Text("--input", "What the message carries: id (default), none, or \"[id, ]Name:Type, Other:Type\".");
        Option<bool> dryRun = GeneratorCommand.DryRun("Print the files and the edit, and write and run nothing.");
        useCase.Arguments.Add(features);
        useCase.Arguments.Add(name);
        useCase.Options.Add(kind);
        useCase.Options.Add(input);
        useCase.Options.Add(dryRun);
        GeneratorCommand.SetGeneration(
            useCase,
            project,
            parseResult => parseResult.GetValue(dryRun),
            executor,
            (parseResult, context) => NewUseCase.Run(new AddUseCaseRequest(parseResult.GetRequiredValue(features), parseResult.GetRequiredValue(name), parseResult.GetRequiredValue(kind), parseResult.GetValue(input)), context));

        return useCase;
    }
}
