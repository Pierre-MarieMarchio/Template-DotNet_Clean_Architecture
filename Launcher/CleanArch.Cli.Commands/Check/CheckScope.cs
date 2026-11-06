namespace CleanArch.Cli.Commands.Check;

internal sealed record CheckScope(bool BuildWide, IReadOnlyList<string> Projects);
