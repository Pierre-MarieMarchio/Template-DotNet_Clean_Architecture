namespace CleanArch.Cli.UpgradeReport;

internal sealed record Comparison(
    IReadOnlyList<Finding> Findings,
    SortedDictionary<string, List<string>> Bootstrap,
    IReadOnlyList<string> Scaffolds,
    IReadOnlyList<string> Own,
    int Unchanged,
    string? Provenance);
