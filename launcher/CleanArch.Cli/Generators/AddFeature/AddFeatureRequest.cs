namespace CleanArch.Cli.Generators.AddFeature;

internal sealed record AddFeatureRequest(string Features, string Type, string Access, string? Deletion, string? Operations, string? Model, string? Concurrency, string? Fields);
