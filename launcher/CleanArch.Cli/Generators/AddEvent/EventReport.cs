using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddEvent;

internal static class EventReport
{
    internal static void Write(TextWriter output, EventSpec spec, IReadOnlyList<Emission> emissions, IEnumerable<string> edited, bool dryRun)
    {
        output.WriteLine(dryRun ? "Dry run: nothing was written." : $"Wrote the event {spec.Name} of {spec.Features}.");
        output.WriteLine();
        output.WriteLine($"{(dryRun ? "Would write" : "Wrote")} {emissions.Count} file(s):");

        foreach (var emission in emissions)
        {
            output.WriteLine($"  {emission.Path}");
        }

        output.WriteLine();
        output.WriteLine($"{(dryRun ? "Would edit" : "Edited")}:");

        foreach (string path in edited.Order(StringComparer.Ordinal))
        {
            output.WriteLine($"  {path}");
        }

        output.WriteLine();
        output.WriteLine("What is left to you:");
        output.WriteLine($"  - Raise it: call RaiseDomainEvent(new {spec.Name}(...)) in the aggregate method that makes the fact true; nothing publishes an event no aggregate raises.");
        output.WriteLine("  - Test that method: the event is among the aggregate's DomainEvents after the call, with the values it was given.");

        if (spec.ConsumerFeatures is not null)
        {
            output.WriteLine($"  - Write {spec.ConsumerName}: it throws NotImplementedException until you do, and the dispatcher would log that and drop every event. So its generated test TheReaction_IsWritten fails, and verify and CI stay red until you replace it with the tests of the reaction.");
            output.WriteLine("    It re-derives its precondition when it runs, so running twice is running once (ADR 0002); a fact whose loss would matter has a sweep.");
            output.WriteLine($"    {spec.ConsumerFeatures} reaches {spec.Features} only through its events: use {spec.EventNamespace} and nothing else of it.");
        }
        else
        {
            output.WriteLine("  - The reason nothing listens is in DomainEventTests beside the name; when a consumer is written later, delete that entry (NoEventIsListedAsUnconsumed_WhileSomethingConsumesIt fails until you do).");
        }
    }
}
