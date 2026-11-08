namespace CleanArch.Cli.UpgradeReport;

internal sealed record Replay(
    string Template,
    string Name,
    IReadOnlyList<(string Symbol, string Value)> Values,
    IReadOnlyList<(string Symbol, string Value)> ProjectOptions,
    string Folder);
