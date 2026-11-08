using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.AddUseCase;

internal sealed record Parameter(string Name, string Type, string Required)
{
    private static readonly Dictionary<string, KnownType> _knownTypes = new(StringComparer.Ordinal)
    {
        ["string"] = new(null, "string.Empty"),
        ["int"] = new("1", "0"),
        ["long"] = new("1L", "0L"),
        ["decimal"] = new("1m", "0m"),
        ["bool"] = new("true", null),
        ["Guid"] = new("Guid.CreateVersion7()", "Guid.Empty"),
        ["DateTimeOffset"] = new("new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)", "default"),
        ["DateOnly"] = new("new DateOnly(2026, 1, 1)", "default"),
    };

    private bool IsNullable => Type.EndsWith('?');

    private KnownType? Known => _knownTypes.GetValueOrDefault(IsNullable ? Type[..^1] : Type);

    internal bool IsKnown => Known is not null;

    internal string Valid => Known switch
    {
        null => "default!",
        { Valid: null } => $"\"{CSharpNames.Separated(Name, ' ')}\"",
        { Valid: { } valid } => valid,
    };

    internal string? Empty => IsNullable ? null : Known?.Empty;

    internal string Argument => CSharpNames.Keywords.Contains(CSharpNames.Lower(Name), StringComparer.Ordinal) ? "@" + CSharpNames.Lower(Name) : CSharpNames.Lower(Name);

    private sealed record KnownType(string? Valid, string? Empty);
}
