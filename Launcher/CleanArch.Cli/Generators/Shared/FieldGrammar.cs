using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace CleanArch.Cli.Generators.Shared;

internal static class FieldGrammar
{
    internal const int StringMaxLength = 400;

    internal const int TextDefaultLength = 2_000;

    internal const int TextMaxLength = 65_536;

    internal const int DecimalDefaultPrecision = 18;

    internal const int DecimalDefaultScale = 2;

    internal const int DecimalMaxPrecision = 28;

    internal const int NameMaxLength = 40;

    private static readonly string[] _reservedTypes =
    [
        "Guid", "DateTimeOffset", "DateOnly", "TimeSpan", "String", "Decimal", "Math", "DomainException",
        "ArgumentNullException", "UserId", "JsonRequired",
    ];

    private static readonly string[] _generatedSuffixes =
        ["MaxLength", "Precision", "Scale", "Minimum", "ExclusiveMinimum", "Maximum", "ExclusiveMaximum", "Field"];

    private static readonly string[] _generatedPrefixes = ["Require", "Valid"];

    private static readonly string[] _contextualKeywords = ["await", "nameof", "var", "value"];

    private static readonly Dictionary<string, (string Reason, string Stored, string Unstored)> _refusedTypes = new(StringComparer.Ordinal)
    {
        ["enum"] = (
            "enum is not a field type: an enum's values are a decision of the domain, and a stored enum changes "
                + "meaning when a member is reordered or renamed",
            "Declare a string(n) and check its values by hand, or write a value object",
            "Carry a string, whose values the aggregate checks"),
        ["DateTime"] = ("DateTime is not a field type: it carries no offset, so the instant it names depends on the server", "Use DateTimeOffset", "Use DateTimeOffset"),
        ["double"] = ("double is not a field type: it is not exact, and the providers store it differently", "Use decimal or decimal(p,s)", "Use decimal"),
        ["float"] = ("float is not a field type: it is not exact, and the providers store it differently", "Use decimal or decimal(p,s)", "Use decimal"),
        ["short"] = ("short is not a field type", "Use int, with bounds if the range matters", "Use int"),
        ["byte"] = ("byte is not a field type", "Use int, with bounds if the range matters", "Use int"),
        ["uint"] = ("uint is not a field type: no provider stores it alike", "Use long, with >=0", "Use long"),
        ["ulong"] = ("ulong is not a field type: no provider stores it alike", "Use long, with >=0, or decimal(20,0)", "Use long or decimal"),
        ["char"] = ("char is not a field type", "Use string(1)", "Use string"),
    };

    private static readonly Dictionary<string, FieldKind> _kinds = new(StringComparer.Ordinal)
    {
        ["string"] = FieldKind.String,
        ["text"] = FieldKind.Text,
        ["int"] = FieldKind.Int,
        ["long"] = FieldKind.Long,
        ["decimal"] = FieldKind.Decimal,
        ["bool"] = FieldKind.Bool,
        ["DateOnly"] = FieldKind.DateOnly,
        ["DateTimeOffset"] = FieldKind.DateTimeOffset,
        ["Guid"] = FieldKind.Guid,
    };

    private enum TokenKind
    {
        Name,
        Number,
        Symbol,
        Quoted,
        Unknown,
        End,
    }

    private sealed record Token(TokenKind Kind, string Text, int Position);

    internal static List<Field>? Parse(string text, FieldProfile profile, List<string> problems)
    {
        int before = problems.Count;
        var tokens = Tokenize(text);

        if (tokens.Count == 1)
        {
            problems.Add($"--fields is empty. It lists the fields, Name:type, comma separated, as in {profile.Example}.");

            return null;
        }

        var fields = new List<Field>();
        int at = 0;

        while (true)
        {
            if (ParseField(tokens, ref at, profile, problems) is not { } field)
            {
                return null;
            }

            fields.Add(field);

            if (tokens[at].Kind == TokenKind.End)
            {
                break;
            }

            if (tokens[at].Text != ",")
            {
                problems.Add(Expected(tokens[at], "a comma before the next field, or the end of the list"));

                return null;
            }

            at++;
        }

        problems.AddRange(NameProblems(fields, profile));

        return problems.Count == before ? fields : null;
    }

    private static Field? ParseField(List<Token> tokens, ref int at, FieldProfile profile, List<string> problems)
    {
        var name = tokens[at];

        if (name.Kind != TokenKind.Name)
        {
            problems.Add(Expected(name, "a field name, PascalCase, as Title"));

            return null;
        }

        at++;

        if (tokens[at].Text != ":")
        {
            problems.Add(Expected(tokens[at], $"':' after the field name {Shown(name.Text)}, then its type"));

            return null;
        }

        at++;

        var typeToken = tokens[at];

        if (typeToken.Kind != TokenKind.Name)
        {
            problems.Add(Expected(typeToken, $"a type, one of {profile.Types}"));

            return null;
        }

        if (_kinds.TryGetValue(typeToken.Text, out FieldKind named) && !profile.Kinds.Contains(named))
        {
            problems.Add($"--fields, at character {typeToken.Position + 1}: {typeToken.Text} is not a type of {profile.Holder}, which carries {profile.Types}.");

            return null;
        }

        at++;

        if (!profile.Stored && tokens[at].Text == "(")
        {
            problems.Add(Unstored(tokens[at], profile, "length or precision"));

            return null;
        }

        int length = 0, precision = 0, scale = 0;
        FieldKind kind;

        switch (typeToken.Text)
        {
            case "string" when !profile.Stored:
                kind = FieldKind.String;
                break;

            case "string":
                if (tokens[at].Text != "(")
                {
                    problems.Add(
                        $"--fields, at character {tokens[at].Position + 1}: string needs its maximum length, as string(120), "
                        + "so that no column is unbounded; a long free text is text or text(n).");

                    return null;
                }

                if (Arguments(tokens, ref at, 1, problems) is not [var maximum])
                {
                    return null;
                }

                if (maximum < 1 || maximum > StringMaxLength)
                {
                    problems.Add(
                        $"--fields: {name.Text} is string({maximum}), and a string is 1 to {StringMaxLength} characters long: "
                        + $"{StringMaxLength} keeps every string column indexable, and so sortable, on the three providers "
                        + "(SQL Server's 1,700-byte index key holds an nvarchar(400) beside the owner and the id). A longer value is text(n).");
                }

                kind = FieldKind.String;
                length = maximum;
                break;

            case "text":
                length = TextDefaultLength;

                if (tokens[at].Text == "(")
                {
                    if (Arguments(tokens, ref at, 1, problems) is not [var limit])
                    {
                        return null;
                    }

                    length = limit;
                }

                if (length < 1 || length > TextMaxLength)
                {
                    problems.Add(
                        $"--fields: {name.Text} is text({length}), and a text is 1 to {TextMaxLength:N0} characters long: the API "
                        + $"refuses a body over RequestLimits:MaxRequestBodyBytes, {TextMaxLength:N0} bytes by default, so a longer "
                        + "field could not arrive in a request. A document is a file.");
                }

                kind = FieldKind.Text;
                break;

            case "decimal" when !profile.Stored:
                kind = FieldKind.Decimal;
                break;

            case "decimal":
                precision = DecimalDefaultPrecision;
                scale = DecimalDefaultScale;

                if (tokens[at].Text == "(")
                {
                    if (Arguments(tokens, ref at, 2, problems) is not [var digits, var decimals])
                    {
                        return null;
                    }

                    precision = digits;
                    scale = decimals;
                }

                if (precision < 1 || precision > DecimalMaxPrecision || scale < 0 || scale > precision)
                {
                    problems.Add(
                        $"--fields: {name.Text} is decimal({precision},{scale}), and a decimal(p,s) has 1 to {DecimalMaxPrecision} "
                        + $"digits in all, the most a .NET decimal holds exactly, of which 0 to p after the point.");
                }

                kind = FieldKind.Decimal;
                break;

            case "int":
                kind = FieldKind.Int;
                break;

            case "long":
                kind = FieldKind.Long;
                break;

            case "bool":
                kind = FieldKind.Bool;
                break;

            case "DateOnly":
                kind = FieldKind.DateOnly;
                break;

            case "DateTimeOffset":
                kind = FieldKind.DateTimeOffset;
                break;

            case "Guid":
                kind = FieldKind.Guid;
                break;

            default:
                problems.Add(_refusedTypes.TryGetValue(typeToken.Text, out var refusal)
                    ? $"--fields, at character {typeToken.Position + 1}: {refusal.Reason}. {(profile.Stored ? refusal.Stored : refusal.Unstored)}."
                    : Expected(typeToken, $"a type, one of {profile.Types}, written as listed, case included"));

                return null;
        }

        bool nullable = false;

        if (tokens[at].Text == "?" && !profile.Stored)
        {
            problems.Add(Unstored(tokens[at], profile, "missing value"));

            return null;
        }

        if (tokens[at].Text == "?")
        {
            nullable = true;
            at++;
        }

        FieldBound? lower = null, upper = null;

        while (tokens[at].Text is ">=" or "<=" or ">" or "<")
        {
            var operation = tokens[at];

            if (!profile.Stored)
            {
                problems.Add(Unstored(operation, profile, "bound"));

                return null;
            }

            if (kind is not (FieldKind.Int or FieldKind.Long or FieldKind.Decimal))
            {
                problems.Add(
                    $"--fields, at character {operation.Position + 1}: {name.Text} is {typeToken.Text}, and only int, long and decimal "
                    + "take a bound. A string or a text is bounded by its length; a date's rule is usually relative to the clock, "
                    + "\"in the future\", which belongs in the domain beside the clock, not in a constant.");

                return null;
            }

            at++;

            var number = tokens[at];

            if (number.Kind != TokenKind.Number)
            {
                problems.Add(Expected(number, $"a number after '{operation.Text}'"));

                return null;
            }

            at++;

            if (Bound(name.Text, kind, precision, scale, operation.Text, number, problems) is not { } bound)
            {
                continue;
            }

            if (bound.IsLower ? lower is not null : upper is not null)
            {
                problems.Add(
                    $"--fields, at character {operation.Position + 1}: {name.Text} already has {(bound.IsLower ? "a lower" : "an upper")} "
                    + "bound; a field takes at most one of each.");

                continue;
            }

            if (bound.IsLower)
            {
                lower = bound;
            }
            else
            {
                upper = bound;
            }
        }

        var field = new Field(name.Text, kind, nullable, length, precision, scale, lower, upper, name.Position);

        if (lower is not null || upper is not null)
        {
            problems.AddRange(RangeProblems(field));
        }

        if (tokens[at].Text != "=")
        {
            if (profile.TakesDefault && !nullable)
            {
                problems.Add(
                    $"--fields, at character {tokens[at].Position + 1}: {name.Text} cannot be null, and the rows the table already "
                    + $"holds need a value for it: give one its rules accept after '=', as {field.Declaration}={DefaultExample(field)}, "
                    + "or make it nullable with '?'.");
            }

            return field;
        }

        var equals = tokens[at];

        if (!profile.TakesDefault)
        {
            problems.Add(
                $"--fields, at character {equals.Position + 1}: a default, '=' and a value, is what new-field gives the rows a table "
                + "already holds when it adds a field to them; here the field has no such rows. Leave it out.");

            return null;
        }

        if (nullable)
        {
            problems.Add(
                $"--fields, at character {equals.Position + 1}: {name.Text} is nullable, so the rows the table already holds start "
                + "with null, which it accepts; a default is for a field that cannot be null. Leave the default out, or the '?'.");

            return null;
        }

        at++;

        if (DefaultOf(field, tokens[at], problems) is not { } value)
        {
            return null;
        }

        at++;

        var defaulted = field with { Default = value };

        problems.AddRange(DefaultProblems(defaulted));

        return defaulted;
    }

    private static string DefaultExample(Field field) => field.Kind switch
    {
        FieldKind.Int or FieldKind.Long or FieldKind.Decimal => SatisfyingValue(field).ToString(CultureInfo.InvariantCulture),
        FieldKind.Bool => "false",
        FieldKind.Text => "''",
        FieldKind.DateOnly => "'2026-01-01'",
        FieldKind.DateTimeOffset => "'2026-01-01T00:00:00Z'",
        FieldKind.Guid => "'00000000-0000-0000-0000-000000000001'",
        _ => "'none'",
    };

    private static decimal SatisfyingValue(Field field)
    {
        decimal step = field.Kind == FieldKind.Decimal ? Step(field.Scale) : 1m;

        return field.Bounds.All(bound => bound.Holds(0m)) ? 0m
            : field.Lower is { } lower && !lower.Holds(0m) ? (lower.IsInclusive ? lower.Value : lower.Value + step)
            : field.Upper!.IsInclusive ? field.Upper.Value : field.Upper.Value - step;
    }

    private static FieldDefault? DefaultOf(Field field, Token token, List<string> problems)
    {
        string where = $"--fields, at character {token.Position + 1}";

        switch (field.Kind)
        {
            case FieldKind.Int or FieldKind.Long or FieldKind.Decimal:
                if (token.Kind != TokenKind.Number
                    || !decimal.TryParse(token.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number))
                {
                    problems.Add(Expected(token, $"a number after '=', the default of {field.Name}"));

                    return null;
                }

                if (field.Kind != FieldKind.Decimal)
                {
                    string type = field.Kind == FieldKind.Int ? "int" : "long";
                    decimal minimum = field.Kind == FieldKind.Int ? int.MinValue : long.MinValue;
                    decimal maximum = field.Kind == FieldKind.Int ? int.MaxValue : long.MaxValue;

                    if (token.Text.Contains('.', StringComparison.Ordinal) || number < minimum || number > maximum)
                    {
                        problems.Add($"{where}: {Shown(token.Text)} is not a whole number an {type} holds.");

                        return null;
                    }

                    string whole = number.ToString(CultureInfo.InvariantCulture);

                    return new FieldDefault(whole, field.Kind == FieldKind.Long ? whole + "L" : whole);
                }

                if (Misfit(token.Text, number, field.Precision, field.Scale) is { } misfit)
                {
                    problems.Add($"{where}: {misfit}");

                    return null;
                }

                string exact = number.ToString(CultureInfo.InvariantCulture);

                return new FieldDefault(exact, exact + "m");

            case FieldKind.Bool:
                return token.Text is "true" or "false"
                    ? new FieldDefault(token.Text, token.Text)
                    : Refused(Expected(token, $"true or false after '=', the default of {field.Name}"));

            default:
                if (token.Kind != TokenKind.Quoted)
                {
                    return Refused(token.Text == "'"
                        ? $"{where}: a quote opens the default of {field.Name} and none closes it."
                        : Expected(token, $"a value between quotes after '=', as {DefaultExample(field)}, the default of {field.Name}"));
                }

                return QuotedDefault(field, token, where, problems);
        }

        FieldDefault? Refused(string problem)
        {
            problems.Add(problem);

            return null;
        }
    }

    private static FieldDefault? QuotedDefault(Field field, Token token, string where, List<string> problems)
    {
        string text = token.Text;

        switch (field.Kind)
        {
            case FieldKind.DateOnly:
                if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
                {
                    problems.Add($"{where}: {Shown(text)} is not a date written yyyy-MM-dd, as '2026-01-31'.");

                    return null;
                }

                return new FieldDefault($"'{date:yyyy-MM-dd}'", $"new DateOnly({date.Year}, {date.Month}, {date.Day})");

            case FieldKind.DateTimeOffset:
                if (!Regex.IsMatch(text, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2})?(Z|[+-]\d{2}:\d{2})$", RegexOptions.None, TimeSpan.FromSeconds(1))
                    || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset instant))
                {
                    problems.Add(
                        $"{where}: {Shown(text)} is not an instant written yyyy-MM-ddTHH:mm:ss with its offset, as '2026-01-31T09:00:00Z' "
                        + "or '2026-01-31T10:00:00+01:00'.");

                    return null;
                }

                var utc = instant.ToUniversalTime();

                return new FieldDefault(
                    $"'{utc:yyyy-MM-ddTHH:mm:ss}Z'",
                    $"new DateTimeOffset({utc.Year}, {utc.Month}, {utc.Day}, {utc.Hour}, {utc.Minute}, {utc.Second}, TimeSpan.Zero)");

            case FieldKind.Guid:
                if (!Guid.TryParseExact(text, "D", out Guid identifier))
                {
                    problems.Add($"{where}: {Shown(text)} is not a Guid written with its hyphens, as '3f2504e0-4f89-41d3-9a0c-0305e82c3301'.");

                    return null;
                }

                return new FieldDefault($"'{identifier:D}'", $"new Guid(\"{identifier:D}\")");

            default:
                return new FieldDefault($"'{text.Replace("'", "''", StringComparison.Ordinal)}'", Quote(text));
        }
    }

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

    private static IEnumerable<string> DefaultProblems(Field field)
    {
        string where = $"--fields: the default of {field.Name}, {field.Default!.Literal},";

        switch (field.Kind)
        {
            case FieldKind.Int or FieldKind.Long or FieldKind.Decimal:
                decimal value = decimal.Parse(field.Default.Literal, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

                foreach (var bound in field.Bounds.Where(bound => !bound.Holds(value)))
                {
                    yield return $"{where} breaks the rule the field checks: it must be {bound.Wording} {bound.Literal}.";
                }

                break;

            case FieldKind.String or FieldKind.Text:
                string text = Unquoted(field.Default.Literal);

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

                break;

            case FieldKind.DateOnly when field.Default.Literal == "'0001-01-01'":
            case FieldKind.DateTimeOffset when field.Default.Literal == "'0001-01-01T00:00:00Z'":
            case FieldKind.Guid when field.Default.Literal == $"'{Guid.Empty:D}'":
                yield return $"{where} is the empty value, which the field refuses: a required {field.ClrType} is never the default one.";

                break;
        }
    }

    private static string Unquoted(string literal) => literal[1..^1].Replace("''", "'", StringComparison.Ordinal);

    private static string? Misfit(string text, decimal value, int precision, int scale)
    {
        int decimals = text.Contains('.', StringComparison.Ordinal) ? text.Length - text.IndexOf('.', StringComparison.Ordinal) - 1 : 0;
        int digits = decimal.Truncate(Math.Abs(value)) == 0 ? 0 : decimal.Truncate(Math.Abs(value)).ToString(CultureInfo.InvariantCulture).Length;

        return decimals > scale || digits > precision - scale
            ? $"{Shown(text)} does not fit decimal({precision},{scale}), whose values have at most {precision - scale} digits before the point and {scale} after it."
            : null;
    }

    private static int[]? Arguments(List<Token> tokens, ref int at, int count, List<string> problems)
    {
        at++;
        var values = new List<int>();

        for (int index = 0; index < count; index++)
        {
            if (index > 0)
            {
                if (tokens[at].Text != ",")
                {
                    problems.Add(Expected(tokens[at], "',' between the precision and the scale"));

                    return null;
                }

                at++;
            }

            var number = tokens[at];

            if (number.Kind != TokenKind.Number
                || !int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                problems.Add(Expected(number, "a whole number"));

                return null;
            }

            values.Add(value);
            at++;
        }

        if (tokens[at].Text != ")")
        {
            problems.Add(Expected(tokens[at], "')'"));

            return null;
        }

        at++;

        return [.. values];
    }

    private static FieldBound? Bound(string name, FieldKind kind, int precision, int scale, string operation, Token number, List<string> problems)
    {
        string where = $"--fields, at character {number.Position + 1}";

        if (!decimal.TryParse(number.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
        {
            problems.Add($"{where}: {Shown(number.Text)} is not a number this grammar reads.");

            return null;
        }

        if (kind is FieldKind.Int or FieldKind.Long)
        {
            decimal minimum = kind == FieldKind.Int ? int.MinValue : long.MinValue;
            decimal maximum = kind == FieldKind.Int ? int.MaxValue : long.MaxValue;
            string type = kind == FieldKind.Int ? "int" : "long";

            if (number.Text.Contains('.', StringComparison.Ordinal) || value < minimum || value > maximum)
            {
                problems.Add($"{where}: {Shown(number.Text)} is not a whole number an {type} holds.");

                return null;
            }

            if ((operation == ">=" && value == minimum) || (operation == "<=" && value == maximum))
            {
                problems.Add($"{where}: {name}{operation}{number.Text} bounds nothing: every {type} satisfies it. Leave it out.");

                return null;
            }

            return new FieldBound(operation, value, value.ToString(CultureInfo.InvariantCulture));
        }

        if (Misfit(number.Text, value, precision, scale) is { } misfit)
        {
            problems.Add($"{where}: {misfit}");

            return null;
        }

        return new FieldBound(operation, value, value.ToString(CultureInfo.InvariantCulture));
    }

    private static IEnumerable<string> RangeProblems(Field field)
    {
        decimal step = field.Kind == FieldKind.Decimal ? Step(field.Scale) : 1m;
        decimal floor = field.Kind switch
        {
            FieldKind.Int => int.MinValue,
            FieldKind.Long => long.MinValue,
            _ => -Ceiling(field) + step,
        };
        decimal top = field.Kind switch
        {
            FieldKind.Int => int.MaxValue,
            FieldKind.Long => long.MaxValue,
            _ => Ceiling(field) - step,
        };

        decimal low = field.Lower is { } lower ? Math.Max(floor, lower.IsInclusive ? lower.Value : lower.Value + step) : floor;
        decimal high = field.Upper is { } upper ? Math.Min(top, upper.IsInclusive ? upper.Value : upper.Value - step) : top;

        if (low > high)
        {
            yield return $"--fields: no value satisfies the bounds of {field.Declaration}. Widen them.";
        }
    }

    internal static decimal Step(int scale)
    {
        decimal step = 1m;

        for (int index = 0; index < scale; index++)
        {
            step /= 10m;
        }

        return step;
    }

    internal static decimal Ceiling(Field field)
    {
        decimal ceiling = 1m;

        for (int index = 0; index < field.Precision - field.Scale; index++)
        {
            ceiling *= 10m;
        }

        return ceiling;
    }

    internal static List<string> NameProblems(IReadOnlyList<Field> fields, FieldProfile profile)
    {
        var problems = new List<string>();

        foreach (var field in fields)
        {
            string where = $"--fields, at character {field.Position + 1}";

            if (!CSharpNames.PascalCase.IsMatch(field.Name))
            {
                problems.Add($"{where}: {Shown(field.Name)} is not a field name: PascalCase, letters and digits, as DueDate.");

                continue;
            }

            if (field.Name.Length > NameMaxLength)
            {
                problems.Add($"{where}: {field.Name} is longer than {NameMaxLength} characters, which makes names some tools cannot hold. Shorten it.");
            }

            string parameter = CSharpNames.Lower(field.Name);

            if (CSharpNames.Keywords.Contains(parameter, StringComparer.Ordinal) || _contextualKeywords.Contains(parameter, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{where}: {field.Name} becomes '{parameter}' once its first letter is lowered, which is how the generated "
                    + "parameters are named, and C# reserves that word there. Choose another name.");
            }

            if (profile.ReservedMembers.Contains(field.Name, StringComparer.Ordinal))
            {
                problems.Add($"{where}: {field.Name} is {profile.ReservedWording}. Choose another name.");
            }

            if (_reservedTypes.Contains(field.Name, StringComparer.Ordinal))
            {
                problems.Add(
                    $"{where}: {field.Name} names a type the generated code calls by name, which a property of that name would hide. "
                    + "Choose another name.");
            }

            foreach (var other in fields.Where(other => profile.ReservesGeneratedNames && !ReferenceEquals(other, field)))
            {
                if (_generatedSuffixes.Any(suffix => field.Name == other.Name + suffix)
                    || _generatedPrefixes.Any(prefix => field.Name == prefix + other.Name))
                {
                    problems.Add(
                        $"{where}: {field.Name} is the name the generated code gives a constant, a method or a local of {other.Name}. "
                        + "Choose another name.");
                }
            }
        }

        foreach (var group in fields.GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            problems.Add(
                $"--fields names {string.Join(" and ", group.Select(field => field.Name))}, the same name but for its case: "
                + $"{profile.DuplicateReason}. Keep one.");
        }

        return problems;
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int position = 0;

        while (position < text.Length)
        {
            char current = text[position];

            if (char.IsWhiteSpace(current))
            {
                position++;

                continue;
            }

            int start = position;

            if (char.IsAsciiLetter(current) || current == '_')
            {
                while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
                {
                    position++;
                }

                tokens.Add(new Token(TokenKind.Name, text[start..position], start));
            }
            else if (char.IsAsciiDigit(current) || (current == '-' && position + 1 < text.Length && char.IsAsciiDigit(text[position + 1])))
            {
                position++;

                while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] == '.'))
                {
                    position++;
                }

                tokens.Add(new Token(TokenKind.Number, text[start..position], start));
            }
            else if (current is '>' or '<')
            {
                position += position + 1 < text.Length && text[position + 1] == '=' ? 2 : 1;
                tokens.Add(new Token(TokenKind.Symbol, text[start..position], start));
            }
            else if (current is ':' or ',' or '(' or ')' or '?' or '=')
            {
                position++;
                tokens.Add(new Token(TokenKind.Symbol, current.ToString(), start));
            }
            else if (current == '\'' && Closing(text, position) is var closing and > 0)
            {
                position = closing + 1;
                tokens.Add(new Token(TokenKind.Quoted, text[(start + 1)..closing].Replace("''", "'", StringComparison.Ordinal), start));
            }
            else
            {
                position++;
                tokens.Add(new Token(TokenKind.Unknown, current.ToString(), start));
            }
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, text.Length));

        return tokens;
    }

    private static int Closing(string text, int opening)
    {
        for (int index = opening + 1; index < text.Length; index++)
        {
            if (text[index] != '\'')
            {
                continue;
            }

            if (index + 1 < text.Length && text[index + 1] == '\'')
            {
                index++;

                continue;
            }

            return index;
        }

        return -1;
    }

    private static string Unstored(Token token, FieldProfile profile, string part) =>
        $"--fields, at character {token.Position + 1}: a field of {profile.Holder} takes no {part}. {profile.UnstoredReason}.";

    private static string Expected(Token token, string expected) =>
        $"--fields, at character {token.Position + 1}: expected {expected}, and found "
        + (token.Kind == TokenKind.End ? "the end of the list." : $"{Shown(token.Text)}.");

    private static string Shown(string text) => text.Length <= 40 ? $"'{text}'" : $"'{text[..40]}...'";
}
