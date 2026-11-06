using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static CleanArch.Cli.Generators.Shared.FieldMessages;

namespace CleanArch.Cli.Generators.Shared;

internal static class FieldDefaults
{
    private const NumberStyles _signedDecimal = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    internal static string Example(Field field) => field.Kind switch
    {
        FieldKind.Int or FieldKind.Long or FieldKind.Decimal => SatisfyingValue(field).ToString(CultureInfo.InvariantCulture),
        FieldKind.Bool => "false",
        FieldKind.Text => "''",
        FieldKind.DateOnly => "'2026-01-01'",
        FieldKind.DateTimeOffset => "'2026-01-01T00:00:00Z'",
        FieldKind.Guid => "'00000000-0000-0000-0000-000000000001'",
        _ => "'none'",
    };

    internal static FieldDefault? Of(Field field, FieldToken token, List<string> problems) => field.Kind switch
    {
        FieldKind.Int or FieldKind.Long or FieldKind.Decimal => Numeric(field, token, problems),
        FieldKind.Bool => token.Text is "true" or "false"
            ? new FieldDefault(token.Text, token.Text)
            : Refused(problems, Expected(token, $"true or false after '=', the default of {field.Name}")),
        _ when token.Kind == FieldTokenKind.Quoted => Quoted(field, token, problems),
        _ => Refused(problems, token.Text == "'"
            ? $"{At(token)}: a quote opens the default of {field.Name} and none closes it."
            : Expected(token, $"a value between quotes after '=', as {Example(field)}, the default of {field.Name}")),
    };

    internal static IEnumerable<string> Problems(Field field)
    {
        string where = $"--fields: the default of {field.Name}, {field.Default!.Literal},";

        return field.Kind switch
        {
            FieldKind.Int or FieldKind.Long or FieldKind.Decimal => BoundProblems(field, where),
            FieldKind.String or FieldKind.Text => TextProblems(field, where),
            _ when IsEmptyValue(field) => [$"{where} is the empty value, which the field refuses: a required {field.ClrType} is never the default one."],
            _ => [],
        };
    }

    private static decimal SatisfyingValue(Field field)
    {
        decimal step = field.Kind == FieldKind.Decimal ? FieldGrammar.Step(field.Scale) : 1m;

        if (field.Bounds.All(bound => bound.Holds(0m)))
        {
            return 0m;
        }

        return field.Lower is { } lower && !lower.Holds(0m) ? Closest(lower, step) : Closest(field.Upper!, -step);
    }

    private static decimal Closest(FieldBound bound, decimal step) => bound.IsInclusive ? bound.Value : bound.Value + step;

    private static FieldDefault? Numeric(Field field, FieldToken token, List<string> problems)
    {
        if (token.Kind != FieldTokenKind.Number || !decimal.TryParse(token.Text, _signedDecimal, CultureInfo.InvariantCulture, out decimal number))
        {
            return Refused(problems, Expected(token, $"a number after '=', the default of {field.Name}"));
        }

        if (field.Kind != FieldKind.Decimal)
        {
            string whole = number.ToString(CultureInfo.InvariantCulture);
            string expression = field.Kind == FieldKind.Long ? whole + "L" : whole;

            return NotWhole(token, number, field.Kind) is { } problem ? Refused(problems, problem) : new FieldDefault(whole, expression);
        }

        string exact = number.ToString(CultureInfo.InvariantCulture);

        return Misfit(token.Text, number, field.Precision, field.Scale) is { } misfit
            ? Refused(problems, $"{At(token)}: {misfit}")
            : new FieldDefault(exact, exact + "m");
    }

    private static FieldDefault? Quoted(Field field, FieldToken token, List<string> problems) => field.Kind switch
    {
        FieldKind.DateOnly => DateDefault(token, problems),
        FieldKind.DateTimeOffset => InstantDefault(token, problems),
        FieldKind.Guid => GuidDefault(token, problems),
        _ => new FieldDefault($"'{token.Text.Replace("'", "''", StringComparison.Ordinal)}'", Quote(token.Text)),
    };

    private static FieldDefault? DateDefault(FieldToken token, List<string> problems) =>
        DateOnly.TryParseExact(token.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
            ? new FieldDefault($"'{date:yyyy-MM-dd}'", $"new DateOnly({date.Year}, {date.Month}, {date.Day})")
            : Refused(problems, $"{At(token)}: {Shown(token.Text)} is not a date written yyyy-MM-dd, as '2026-01-31'.");

    private static FieldDefault? InstantDefault(FieldToken token, List<string> problems)
    {
        string text = token.Text;

        if (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?(Z|[+-]\d{2}:\d{2})$", RegexOptions.None, TimeSpan.FromSeconds(1))
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset instant))
        {
            return Refused(
                problems,
                $"{At(token)}: {Shown(text)} is not an instant written yyyy-MM-ddTHH:mm:ss with its offset, as '2026-01-31T09:00:00Z' "
                + "or '2026-01-31T10:00:00+01:00'.");
        }

        var utc = instant.ToUniversalTime();

        return new FieldDefault(
            $"'{utc:yyyy-MM-ddTHH:mm:ss}Z'",
            $"new DateTimeOffset({utc.Year}, {utc.Month}, {utc.Day}, {utc.Hour}, {utc.Minute}, {utc.Second}, TimeSpan.Zero)");
    }

    private static FieldDefault? GuidDefault(FieldToken token, List<string> problems) =>
        Guid.TryParseExact(token.Text, "D", out Guid identifier)
            ? new FieldDefault($"'{identifier:D}'", $"new Guid(\"{identifier:D}\")")
            : Refused(problems, $"{At(token)}: {Shown(token.Text)} is not a Guid written with its hyphens, as '3f2504e0-4f89-41d3-9a0c-0305e82c3301'.");

    private static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");

        foreach (char character in text)
        {
            quoted.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(character) || char.IsSurrogate(character) => $"\\u{(int)character:x4}",
                _ => character.ToString(),
            });
        }

        return quoted.Append('"').ToString();
    }

    private static IEnumerable<string> BoundProblems(Field field, string where)
    {
        decimal value = decimal.Parse(field.Default!.Literal, _signedDecimal, CultureInfo.InvariantCulture);

        return field.Bounds
            .Where(bound => !bound.Holds(value))
            .Select(bound => $"{where} breaks the rule the field checks: it must be {bound.Wording} {bound.Literal}.");
    }

    private static IEnumerable<string> TextProblems(Field field, string where)
    {
        string text = field.Default!.Literal[1..^1].Replace("''", "'", StringComparison.Ordinal);

        if (field.Kind == FieldKind.String && text.Trim().Length == 0)
        {
            yield return $"{where} is blank, which the field refuses: a required string holds at least one character once trimmed.";
        }
        else if (field.Kind == FieldKind.String && text != text.Trim())
        {
            yield return $"{where} starts or ends with a space, which every write trims away: write it trimmed.";
        }

        if (text.Length > field.Length)
        {
            yield return $"{where} is {text.Length:N0} characters long, and the field holds {field.Length:N0} at most.";
        }
    }

    private static bool IsEmptyValue(Field field) => field.Kind switch
    {
        FieldKind.DateOnly => field.Default!.Literal == "'0001-01-01'",
        FieldKind.DateTimeOffset => field.Default!.Literal == "'0001-01-01T00:00:00Z'",
        FieldKind.Guid => field.Default!.Literal == $"'{Guid.Empty:D}'",
        _ => false,
    };

    private static FieldDefault? Refused(List<string> problems, string problem)
    {
        problems.Add(problem);

        return null;
    }
}
