using Spectre.Console;

namespace CleanArch.Tooling.Prompts;

public sealed class RailSpinner : Spinner
{
    public override TimeSpan Interval => TimeSpan.FromMilliseconds(100);

    public override bool IsUnicode => true;

    public override IReadOnlyList<string> Frames => ["┤", "┘", "┴", "└", "├", "┌", "┬", "┐"];
}
