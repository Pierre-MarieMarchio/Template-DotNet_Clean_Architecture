namespace CleanArch.Cli.Generators.AddEvent;

internal sealed record AddEventRequest(string Features, string Name, string? Consumer, string? Unconsumed, string? Fields);
