namespace CleanArch.Cli.Generators.Shared;

internal sealed record GenerationContext(string Root, bool DryRun, TextWriter Output, TextWriter Error);
