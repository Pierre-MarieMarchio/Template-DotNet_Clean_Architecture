using System.CommandLine;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddEvent;

internal static class AddEventCommand
{
    internal static Command Build(CommandContext context) => Build(context.Project, GeneratorCommand.Executor);

    internal static Command Build(LocatedProject? project, Func<ParseResult, IExecutor> executor)
    {
        var domainEvent = new Command("event", "Write a domain event a feature of the rich shape raises, with its consumer or its place on the list of events nobody consumes, and their tests.");
        Argument<string> features = new("Features") { Description = "The folder of an existing feature of the rich shape, which raises the event, e.g. Invoices." };
        Argument<string> name = new("Event") { Description = "PascalCase, past tense, ending in DomainEvent, e.g. InvoicePaidDomainEvent." };
        Option<string?> consumer = GeneratorCommand.Text("--consumer", "The feature that reacts, and what it does, <Features>:<Reaction>, e.g. Reminders:CancelReminders.");
        Option<string?> unconsumed = GeneratorCommand.Text("--unconsumed", "Instead of a consumer, the reason nothing listens; one of the two is required.");
        Option<string?> fields = GeneratorCommand.Text("--fields", "What the event carries besides OccurredOn, Name:Type, comma separated; the id of the feature's aggregate by default.");
        Option<bool> dryRun = GeneratorCommand.DryRun("Print what would be written, and write nothing.");
        domainEvent.Arguments.Add(features);
        domainEvent.Arguments.Add(name);
        domainEvent.Options.Add(consumer);
        domainEvent.Options.Add(unconsumed);
        domainEvent.Options.Add(fields);
        domainEvent.Options.Add(dryRun);
        GeneratorCommand.SetGeneration(
            domainEvent,
            project,
            parseResult => parseResult.GetValue(dryRun),
            executor,
            (parseResult, context) => NewEvent.Run(new AddEventRequest(parseResult.GetRequiredValue(features), parseResult.GetRequiredValue(name), parseResult.GetValue(consumer), parseResult.GetValue(unconsumed), parseResult.GetValue(fields)), context));

        return domainEvent;
    }
}
