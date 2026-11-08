namespace CleanArch.Cli.Generators.AddUseCase;

internal sealed record AddUseCaseRequest(string Features, string Name, string Kind, string? Input);
