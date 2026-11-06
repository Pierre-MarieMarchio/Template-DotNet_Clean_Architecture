using System.Globalization;

namespace CleanArch.Cli.Generators.Shared;

internal static class FieldMessages
{
    internal static string At(FieldToken token) => $"--fields, at character {token.Position + 1}";

    internal static string Unstored(FieldToken token, FieldProfile profile, string part) =>
        $"{At(token)}: a field of {profile.Holder} takes no {part}. {profile.UnstoredReason}.";

    internal static string Expected(FieldToken token, string expected) =>
        $"{At(token)}: expected {expected}, and found "
        + (token.Kind == FieldTokenKind.End ? "the end of the list." : $"{Shown(token.Text)}.");

    internal static string Shown(string text) => text.Length <= 40 ? $"'{text}'" : $"'{text[..40]}...'";

    internal static string? NotWhole(FieldToken number, decimal value, FieldKind kind)
    {
        var range = WholeRange.Of(kind);

        return number.Text.Contains('.', StringComparison.Ordinal) || value < range.Minimum || value > range.Maximum
            ? $"{At(number)}: {Shown(number.Text)} is not a whole number an {range.Type} holds."
            : null;
    }

    internal static string? Misfit(string text, decimal value, int precision, int scale)
    {
        int decimals = text.Contains('.', StringComparison.Ordinal) ? text.Length - text.IndexOf('.', StringComparison.Ordinal) - 1 : 0;
        int digits = decimal.Truncate(Math.Abs(value)) == 0 ? 0 : decimal.Truncate(Math.Abs(value)).ToString(CultureInfo.InvariantCulture).Length;

        return decimals > scale || digits > precision - scale
            ? $"{Shown(text)} does not fit decimal({precision},{scale}), whose values have at most {precision - scale} digits before the point and {scale} after it."
            : null;
    }

    internal sealed record WholeRange(string Type, decimal Minimum, decimal Maximum)
    {
        internal static WholeRange Of(FieldKind kind) =>
            kind == FieldKind.Int ? new("int", int.MinValue, int.MaxValue) : new("long", long.MinValue, long.MaxValue);
    }
}
