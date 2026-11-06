namespace CleanArch.Cli.UpgradeReport;

internal sealed record UpgradeRequest(string? To, IReadOnlyList<string> Sources, string? Json, string? Keep);
