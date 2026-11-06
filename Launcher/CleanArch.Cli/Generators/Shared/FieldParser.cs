using System.Globalization;
using static CleanArch.Cli.Generators.Shared.FieldMessages;

namespace CleanArch.Cli.Generators.Shared;

internal sealed class FieldParser(List<FieldToken> tokens, FieldProfile profile, List<string> problems)
{
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

    private int _at;

    private FieldToken Current => tokens[_at];

    internal List<Field>? Fields()
    {
        var fields = new List<Field>();

        while (Field() is { } field)
        {
            fields.Add(field);

            if (Current.Kind == FieldTokenKind.End)
            {
                return fields;
            }

            if (!Accept(",", "a comma before the next field, or the end of the list"))
            {
                return null;
            }
        }

        return null;
    }

    private Field? Field()
    {
        var name = Current;

        if (name.Kind != FieldTokenKind.Name)
        {
            return Refused<Field>(Expected(name, "a field name, PascalCase, as Title"));
        }

        _at++;

        if (!Accept(":", $"':' after the field name {Shown(name.Text)}, then its type"))
        {
            return null;
        }

        var type = Current;

        if (Shape(name) is not { } shape || Nullable() is not { } nullable || Bounds(name, type, shape) is not { } bounds)
        {
            return null;
        }

        var field = new Field(name.Text, shape.Kind, nullable, shape.Length, shape.Precision, shape.Scale, bounds.Lower, bounds.Upper, name.Position);

        if (bounds.Lower is not null || bounds.Upper is not null)
        {
            problems.AddRange(RangeProblems(field));
        }

        return Defaulted(field);
    }

    private TypeShape? Shape(FieldToken name)
    {
        var type = Current;

        if (type.Kind != FieldTokenKind.Name)
        {
            return Refused<TypeShape>(Expected(type, $"a type, one of {profile.Types}"));
        }

        if (_kinds.TryGetValue(type.Text, out FieldKind named) && !profile.Kinds.Contains(named))
        {
            return Refused<TypeShape>($"{At(type)}: {type.Text} is not a type of {profile.Holder}, which carries {profile.Types}.");
        }

        _at++;

        if (!profile.Stored && Current.Text == "(")
        {
            return Refused<TypeShape>(Unstored(Current, profile, "length or precision"));
        }

        return type.Text switch
        {
            "string" when profile.Stored => StringShape(name),
            "text" => TextShape(name),
            "decimal" when profile.Stored => DecimalShape(name),
            _ when _kinds.TryGetValue(type.Text, out FieldKind kind) => new TypeShape(kind),
            _ => Refused<TypeShape>(_refusedTypes.TryGetValue(type.Text, out var refusal)
                ? $"{At(type)}: {refusal.Reason}. {RefusalAdvice(refusal)}."
                : Expected(type, $"a type, one of {profile.Types}, written as listed, case included")),
        };
    }

    private string RefusalAdvice((string Reason, string Stored, string Unstored) refusal) => profile.Stored ? refusal.Stored : refusal.Unstored;

    private TypeShape? StringShape(FieldToken name)
    {
        if (Current.Text != "(")
        {
            return Refused<TypeShape>(
                $"{At(Current)}: string needs its maximum length, as string(120), "
                + "so that no column is unbounded; a long free text is text or text(n).");
        }

        if (Arguments(1) is not [var maximum])
        {
            return null;
        }

        if (maximum < 1 || maximum > FieldGrammar.StringMaxLength)
        {
            problems.Add(
                $"--fields: {name.Text} is string({maximum}), and a string is 1 to {FieldGrammar.StringMaxLength} characters long: "
                + $"{FieldGrammar.StringMaxLength} keeps every string column indexable, and so sortable, on the three providers "
                + "(SQL Server's 1,700-byte index key holds an nvarchar(400) beside the owner and the id). A longer value is text(n).");
        }

        return new TypeShape(FieldKind.String, Length: maximum);
    }

    private TypeShape? TextShape(FieldToken name)
    {
        int length = FieldGrammar.TextDefaultLength;

        if (Current.Text == "(")
        {
            if (Arguments(1) is not [var limit])
            {
                return null;
            }

            length = limit;
        }

        if (length < 1 || length > FieldGrammar.TextMaxLength)
        {
            problems.Add(
                $"--fields: {name.Text} is text({length}), and a text is 1 to {FieldGrammar.TextMaxLength:N0} characters long: the API "
                + $"refuses a body over RequestLimits:MaxRequestBodyBytes, {FieldGrammar.TextMaxLength:N0} bytes by default, so a longer "
                + "field could not arrive in a request. A document is a file.");
        }

        return new TypeShape(FieldKind.Text, Length: length);
    }

    private TypeShape? DecimalShape(FieldToken name)
    {
        int precision = FieldGrammar.DecimalDefaultPrecision;
        int scale = FieldGrammar.DecimalDefaultScale;

        if (Current.Text == "(")
        {
            if (Arguments(2) is not [var digits, var decimals])
            {
                return null;
            }

            precision = digits;
            scale = decimals;
        }

        if (precision < 1 || precision > FieldGrammar.DecimalMaxPrecision || scale < 0 || scale > precision)
        {
            problems.Add(
                $"--fields: {name.Text} is decimal({precision},{scale}), and a decimal(p,s) has 1 to {FieldGrammar.DecimalMaxPrecision} "
                + $"digits in all, the most a .NET decimal holds exactly, of which 0 to p after the point.");
        }

        return new TypeShape(FieldKind.Decimal, Precision: precision, Scale: scale);
    }

    private int[]? Arguments(int count)
    {
        _at++;

        var values = new List<int>();

        while (values.Count < count)
        {
            if (values.Count > 0 && !Accept(",", "',' between the precision and the scale"))
            {
                return null;
            }

            if (WholeNumber() is not { } value)
            {
                return null;
            }

            values.Add(value);
        }

        return Accept(")", "')'") ? [.. values] : null;
    }

    private int? WholeNumber()
    {
        var number = Current;

        if (number.Kind != FieldTokenKind.Number || !int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
        {
            problems.Add(Expected(number, "a whole number"));

            return null;
        }

        _at++;

        return value;
    }

    private bool? Nullable()
    {
        if (Current.Text != "?")
        {
            return false;
        }

        if (!profile.Stored)
        {
            problems.Add(Unstored(Current, profile, "missing value"));

            return null;
        }

        _at++;

        return true;
    }

    private FieldBounds? Bounds(FieldToken name, FieldToken type, TypeShape shape)
    {
        var bounds = new FieldBounds();

        while (Current.Text is ">=" or "<=" or ">" or "<")
        {
            var operation = Current;

            if (BoundRefusal(operation, name, type, shape.Kind) is { } refusal)
            {
                return Refused<FieldBounds>(refusal);
            }

            _at++;

            var number = Current;

            if (number.Kind != FieldTokenKind.Number)
            {
                return Refused<FieldBounds>(Expected(number, $"a number after '{operation.Text}'"));
            }

            _at++;

            if (Bound(name.Text, shape, operation.Text, number) is { } bound && !bounds.TryAdd(bound))
            {
                problems.Add($"{At(operation)}: {name.Text} already has {(bound.IsLower ? "a lower" : "an upper")} bound; a field takes at most one of each.");
            }
        }

        return bounds;
    }

    private string? BoundRefusal(FieldToken operation, FieldToken name, FieldToken type, FieldKind kind)
    {
        if (!profile.Stored)
        {
            return Unstored(operation, profile, "bound");
        }

        return kind is FieldKind.Int or FieldKind.Long or FieldKind.Decimal
            ? null
            : $"{At(operation)}: {name.Text} is {type.Text}, and only int, long and decimal "
                + "take a bound. A string or a text is bounded by its length; a date's rule is usually relative to the clock, "
                + "\"in the future\", which belongs in the domain beside the clock, not in a constant.";
    }

    private FieldBound? Bound(string name, TypeShape shape, string operation, FieldToken number)
    {
        if (!decimal.TryParse(number.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
        {
            return Refused<FieldBound>($"{At(number)}: {Shown(number.Text)} is not a number this grammar reads.");
        }

        string? problem = shape.Kind is FieldKind.Int or FieldKind.Long
            ? WholeBoundProblem(name, shape.Kind, operation, number, value)
            : DecimalBoundProblem(shape, number, value);

        return problem is null ? new FieldBound(operation, value, value.ToString(CultureInfo.InvariantCulture)) : Refused<FieldBound>(problem);
    }

    private static string? WholeBoundProblem(string name, FieldKind kind, string operation, FieldToken number, decimal value)
    {
        if (NotWhole(number, value, kind) is { } notWhole)
        {
            return notWhole;
        }

        var range = WholeRange.Of(kind);

        return (operation == ">=" && value == range.Minimum) || (operation == "<=" && value == range.Maximum)
            ? $"{At(number)}: {name}{operation}{number.Text} bounds nothing: every {range.Type} satisfies it. Leave it out."
            : null;
    }

    private static string? DecimalBoundProblem(TypeShape shape, FieldToken number, decimal value) =>
        Misfit(number.Text, value, shape.Precision, shape.Scale) is { } misfit ? $"{At(number)}: {misfit}" : null;

    private static IEnumerable<string> RangeProblems(Field field)
    {
        decimal step = field.Kind == FieldKind.Decimal ? FieldGrammar.Step(field.Scale) : 1m;
        decimal floor = field.Kind switch
        {
            FieldKind.Int => int.MinValue,
            FieldKind.Long => long.MinValue,
            _ => -FieldGrammar.Ceiling(field) + step,
        };
        decimal top = field.Kind switch
        {
            FieldKind.Int => int.MaxValue,
            FieldKind.Long => long.MaxValue,
            _ => FieldGrammar.Ceiling(field) - step,
        };

        decimal low = field.Lower is { } lower ? Math.Max(floor, Reached(lower, step)) : floor;
        decimal high = field.Upper is { } upper ? Math.Min(top, Reached(upper, -step)) : top;

        if (low > high)
        {
            yield return $"--fields: no value satisfies the bounds of {field.Declaration}. Widen them.";
        }
    }

    private static decimal Reached(FieldBound bound, decimal step) => bound.IsInclusive ? bound.Value : bound.Value + step;

    private Field? Defaulted(Field field)
    {
        if (Current.Text != "=")
        {
            if (profile.TakesDefault && !field.Nullable)
            {
                problems.Add(
                    $"{At(Current)}: {field.Name} cannot be null, and the rows the table already "
                    + $"holds need a value for it: give one its rules accept after '=', as {field.Declaration}={FieldDefaults.Example(field)}, "
                    + "or make it nullable with '?'.");
            }

            return field;
        }

        if (DefaultRefusal(field) is { } refusal)
        {
            return Refused<Field>(refusal);
        }

        _at++;

        if (FieldDefaults.Of(field, Current, problems) is not { } value)
        {
            return null;
        }

        _at++;

        var defaulted = field with { Default = value };

        problems.AddRange(FieldDefaults.Problems(defaulted));

        return defaulted;
    }

    private string? DefaultRefusal(Field field)
    {
        if (!profile.TakesDefault)
        {
            return $"{At(Current)}: a default, '=' and a value, is what new-field gives the rows a table "
                + "already holds when it adds a field to them; here the field has no such rows. Leave it out.";
        }

        return field.Nullable
            ? $"{At(Current)}: {field.Name} is nullable, so the rows the table already holds start "
                + "with null, which it accepts; a default is for a field that cannot be null. Leave the default out, or the '?'."
            : null;
    }

    private bool Accept(string symbol, string expected)
    {
        if (Current.Text != symbol)
        {
            problems.Add(Expected(Current, expected));

            return false;
        }

        _at++;

        return true;
    }

    private T? Refused<T>(string problem)
        where T : class
    {
        problems.Add(problem);

        return null;
    }

    private sealed record TypeShape(FieldKind Kind, int Length = 0, int Precision = 0, int Scale = 0);

    private sealed class FieldBounds
    {
        internal FieldBound? Lower { get; private set; }

        internal FieldBound? Upper { get; private set; }

        internal bool TryAdd(FieldBound bound)
        {
            if (bound.IsLower ? Lower is not null : Upper is not null)
            {
                return false;
            }

            if (bound.IsLower)
            {
                Lower = bound;
            }
            else
            {
                Upper = bound;
            }

            return true;
        }
    }
}
