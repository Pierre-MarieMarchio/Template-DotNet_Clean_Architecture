namespace CleanArch.Cli.UpgradeReport;

internal sealed record Report(
    string Name,
    string From,
    string To,
    IReadOnlyList<string> Decisions,
    Comparison Comparison,
    IReadOnlyList<string> Diffs,
    string Changelog,
    string? Json,
    string? Kept);
