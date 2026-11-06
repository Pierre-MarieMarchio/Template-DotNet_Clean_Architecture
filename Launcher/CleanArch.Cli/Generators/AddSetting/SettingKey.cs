using System.Globalization;

namespace CleanArch.Cli.Generators.AddSetting;

internal sealed record SettingKey(string Name, KeyKind Kind, long Min, long Max, string? Default)
{
    internal bool IsValidated => Kind != KeyKind.Bool;

    internal bool IsRequired => Kind == KeyKind.Secret || (Kind == KeyKind.Text && Default is null);

    internal string Type => Kind switch
    {
        KeyKind.Int => "int",
        KeyKind.Long => "long",
        KeyKind.Duration => "timespan",
        KeyKind.Bool => "bool",
        _ => "string",
    };

    internal string ClrType => Kind switch
    {
        KeyKind.Int => "int",
        KeyKind.Long => "long",
        KeyKind.Duration => "TimeSpan",
        KeyKind.Bool => "bool",
        _ => "string",
    };

    internal string Bound(long value) => Kind == KeyKind.Duration ? Span(value).ToString("c", CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);

    internal string Literal(long value) => Kind switch
    {
        KeyKind.Duration => SpanExpression(value),
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    internal string DefaultLiteral => Kind switch
    {
        KeyKind.Text => $"\"{Default}\"",
        KeyKind.Secret => "string.Empty",
        KeyKind.Bool => Default!,
        _ => Literal(long.Parse(Default!, CultureInfo.InvariantCulture)),
    };

    internal string JsonValue => Kind switch
    {
        KeyKind.Secret => "\"\"",
        KeyKind.Text => $"\"{Default}\"",
        KeyKind.Duration => "\"" + Span(long.Parse(Default!, CultureInfo.InvariantCulture)).ToString("c", CultureInfo.InvariantCulture) + "\"",
        _ => Default!,
    };

    internal string DocumentedDefault => Kind switch
    {
        KeyKind.Secret => "`\"\"`",
        KeyKind.Text => Default is null ? "—" : $"`{Default}`",
        KeyKind.Duration => $"`{Span(long.Parse(Default!, CultureInfo.InvariantCulture)).ToString("c", CultureInfo.InvariantCulture)}`",
        _ => $"`{Default}`",
    };

    internal static TimeSpan Span(long seconds) => TimeSpan.FromSeconds(seconds);

    internal static string SpanExpression(long seconds) => seconds switch
    {
        _ when seconds % 86_400 == 0 => $"TimeSpan.FromDays({seconds / 86_400})",
        _ when seconds % 3_600 == 0 => $"TimeSpan.FromHours({seconds / 3_600})",
        _ when seconds % 60 == 0 => $"TimeSpan.FromMinutes({seconds / 60})",
        _ => $"TimeSpan.FromSeconds({seconds})",
    };
}
