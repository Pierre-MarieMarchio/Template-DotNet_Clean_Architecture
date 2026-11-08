using static CleanArch.Cli.Generators.Shared.FieldMessages;

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

    internal static List<Field>? Parse(string text, FieldProfile profile, List<string> problems)
    {
        int before = problems.Count;
        var tokens = FieldTokens.Of(text);

        if (tokens.Count == 1)
        {
            problems.Add($"--fields is empty. It lists the fields, Name:type, comma separated, as in {profile.Example}.");

            return null;
        }

        if (new FieldParser(tokens, profile, problems).Fields() is not { } fields)
        {
            return null;
        }

        problems.AddRange(NameProblems(fields, profile));

        return problems.Count == before ? fields : null;
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
            problems.AddRange(NameProblems(field, fields, profile));
        }

        foreach (var group in fields.GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            problems.Add(
                $"--fields names {string.Join(" and ", group.Select(field => field.Name))}, the same name but for its case: "
                + $"{profile.DuplicateReason}. Keep one.");
        }

        return problems;
    }

    private static IEnumerable<string> NameProblems(Field field, IReadOnlyList<Field> fields, FieldProfile profile)
    {
        string where = $"--fields, at character {field.Position + 1}";

        if (!CSharpNames.PascalCase.IsMatch(field.Name))
        {
            return [$"{where}: {Shown(field.Name)} is not a field name: PascalCase, letters and digits, as DueDate."];
        }

        return [.. ReservedProblems(field, profile, where), .. GeneratedNameProblems(field, fields, profile, where)];
    }

    private static IEnumerable<string> ReservedProblems(Field field, FieldProfile profile, string where)
    {
        if (field.Name.Length > NameMaxLength)
        {
            yield return $"{where}: {field.Name} is longer than {NameMaxLength} characters, which makes names some tools cannot hold. Shorten it.";
        }

        string parameter = CSharpNames.Lower(field.Name);

        if (CSharpNames.Keywords.Contains(parameter, StringComparer.Ordinal) || _contextualKeywords.Contains(parameter, StringComparer.Ordinal))
        {
            yield return
                $"{where}: {field.Name} becomes '{parameter}' once its first letter is lowered, which is how the generated "
                + "parameters are named, and C# reserves that word there. Choose another name.";
        }

        if (profile.ReservedMembers.Contains(field.Name, StringComparer.Ordinal))
        {
            yield return $"{where}: {field.Name} is {profile.ReservedWording}. Choose another name.";
        }

        if (_reservedTypes.Contains(field.Name, StringComparer.Ordinal))
        {
            yield return
                $"{where}: {field.Name} names a type the generated code calls by name, which a property of that name would hide. "
                + "Choose another name.";
        }
    }

    private static IEnumerable<string> GeneratedNameProblems(Field field, IReadOnlyList<Field> fields, FieldProfile profile, string where) =>
        fields
            .Where(other => profile.ReservesGeneratedNames && !ReferenceEquals(other, field) && IsGeneratedNameOf(field, other))
            .Select(other =>
                $"{where}: {field.Name} is the name the generated code gives a constant, a method or a local of {other.Name}. "
                + "Choose another name.");

    private static bool IsGeneratedNameOf(Field field, Field other) =>
        _generatedSuffixes.Any(suffix => field.Name == other.Name + suffix)
        || _generatedPrefixes.Any(prefix => field.Name == prefix + other.Name);
}
