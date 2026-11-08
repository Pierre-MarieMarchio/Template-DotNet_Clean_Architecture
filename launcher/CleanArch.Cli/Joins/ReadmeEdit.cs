namespace CleanArch.Cli.Joins;

internal sealed record ReadmeEdit(string Readme, int Number, string Path, string Mode, IReadOnlyList<string> Anchor, IReadOnlyList<string> Lines)
{
    public override string ToString() => $"edit {Number} of {Readme}, {Mode} {Anchor.Count} line(s) of {Path}";
}
