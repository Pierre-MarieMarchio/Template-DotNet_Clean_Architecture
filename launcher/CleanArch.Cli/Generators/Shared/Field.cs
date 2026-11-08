using Microsoft.CodeAnalysis;

namespace CleanArch.Cli.Generators.Shared;

internal enum FieldKind
{
    String,
    Text,
    Int,
    Long,
    Decimal,
    Bool,
    DateOnly,
    DateTimeOffset,
    Guid,
}

internal sealed record FieldBound(string Operator, decimal Value, string Literal)
{
    internal bool IsLower => Operator[0] == '>';

    internal bool IsInclusive => Operator.Length == 2;

    internal string Suffix => (IsInclusive ? string.Empty : "Exclusive") + (IsLower ? "Minimum" : "Maximum");

    internal string Wording => Operator switch
    {
        ">=" => "at least",
        ">" => "greater than",
        "<=" => "at most",
        _ => "less than",
    };

    internal bool Holds(decimal value) => Operator switch
    {
        ">=" => value >= Value,
        ">" => value > Value,
        "<=" => value <= Value,
        _ => value < Value,
    };
}

internal sealed record Field(
    string Name,
    FieldKind Kind,
    bool Nullable,
    int Length,
    int Precision,
    int Scale,
    FieldBound? Lower,
    FieldBound? Upper,
    int Position)
{
    internal IEnumerable<FieldBound> Bounds => new[] { Lower, Upper }.OfType<FieldBound>();

    internal string TypeText => Kind switch
    {
        FieldKind.String => Length == 0 ? "string" : $"string({Length})",
        FieldKind.Text => $"text({Length})",
        FieldKind.Decimal => Precision == 0 ? "decimal" : $"decimal({Precision},{Scale})",
        FieldKind.Int => "int",
        FieldKind.Long => "long",
        FieldKind.Bool => "bool",
        FieldKind.DateOnly => "DateOnly",
        FieldKind.DateTimeOffset => "DateTimeOffset",
        _ => "Guid",
    };

    internal string ClrType => Kind switch
    {
        FieldKind.String or FieldKind.Text => "string",
        FieldKind.Int => "int",
        FieldKind.Long => "long",
        FieldKind.Decimal => "decimal",
        FieldKind.Bool => "bool",
        FieldKind.DateOnly => "DateOnly",
        FieldKind.DateTimeOffset => "DateTimeOffset",
        _ => "Guid",
    };

    internal string Declaration =>
        $"{Name}:{TypeText}{(Nullable ? "?" : string.Empty)}{string.Concat(Bounds.Select(bound => bound.Operator + bound.Literal))}";

    internal FieldDefault? Default { get; init; }

    internal string Declared => Default is { } value ? $"{Declaration}={value.Literal}" : Declaration;
}

internal sealed record FieldDefault(string Literal, string Expression);

internal sealed record FieldProfile(
    string Holder,
    string Types,
    string Example,
    FieldKind[] Kinds,
    bool Stored,
    string[] ReservedMembers,
    string ReservedWording,
    bool ReservesGeneratedNames,
    string DuplicateReason,
    string UnstoredReason)
{
    internal bool TakesDefault { get; init; }

    internal static FieldProfile Feature { get; } = new(
        "a feature",
        "string(n), text, text(n), int, long, decimal, decimal(p,s), bool, DateOnly, DateTimeOffset, Guid",
        "--fields \"Title:string(120), Budget:decimal>=0\"",
        Enum.GetValues<FieldKind>(),
        Stored: true,
        [
            "Id", "OwnerId", "Version", "CreatedAt", "CreatedBy", "LastModifiedAt", "LastModifiedBy", "Precondition",
            "AnonymisedText", "DomainEvents", "ClearDomainEvents", "RaiseDomainEvent", "Create", "Rehydrate", "Update",
            "Anonymise", "EqualityContract", "Deconstruct", "PrintMembers", "Equals", "GetHashCode", "GetType", "ToString",
            "MemberwiseClone", "Finalize",
        ],
        "a column or a member the feature already declares (the id, the owner, the version, the audit stamps, or a method "
            + "of the aggregate or of a record)",
        ReservesGeneratedNames: true,
        "two columns a case-insensitive database cannot tell apart",
        string.Empty);

    internal static FieldProfile Event { get; } = new(
        "an event",
        "Guid, string, int, long, bool, decimal, DateOnly, DateTimeOffset",
        "--fields \"InvoiceId:Guid, Amount:decimal\"",
        [FieldKind.Guid, FieldKind.String, FieldKind.Int, FieldKind.Long, FieldKind.Bool, FieldKind.Decimal, FieldKind.DateOnly, FieldKind.DateTimeOffset],
        Stored: false,
        [
            "OccurredOn", "EqualityContract", "Deconstruct", "PrintMembers", "Equals", "GetHashCode", "GetType", "ToString",
            "MemberwiseClone", "Finalize",
        ],
        "a member the event declares itself (OccurredOn, or a method of a record)",
        ReservesGeneratedNames: false,
        "two properties a case-insensitive JSON reader cannot tell apart",
        "No column holds an event's fields, and the aggregate checks a value before it raises the event with it: a length, a "
            + "precision, a bound or a missing value is the aggregate's rule, not the event's");

    internal static FieldProfile AddedField { get; } = Feature with { TakesDefault = true };
}
