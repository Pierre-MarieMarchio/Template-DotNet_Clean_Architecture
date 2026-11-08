namespace CleanArch.Cli.UpgradeReport;

internal enum FindingKind
{
    Take,
    Merge,
    Added,
    Removed,
}

internal sealed record Finding(FindingKind Kind, string Path, string? Note, string? Older, string? Newer);
