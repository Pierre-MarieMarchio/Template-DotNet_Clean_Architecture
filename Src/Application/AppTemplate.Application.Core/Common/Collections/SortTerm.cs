namespace AppTemplate.Application.Core.Common.Collections;

/// <summary>One field to order by, and which way.</summary>
/// <remarks>
/// Constructed only by <see cref="SortOrder.Parse"/>, once a caller's field name has cleared the
/// whitelist: <see cref="Field"/> is therefore always the whitelist's own spelling, never the
/// caller's casing, so every downstream switch can compare it ordinally against a constant.
/// </remarks>
public sealed record SortTerm
{
    private SortTerm(string field, SortDirection direction)
    {
        Field = field;
        Direction = direction;
    }

    /// <summary>The whitelist's own spelling, never the caller's casing.</summary>
    public string Field { get; }

    /// <summary>Ascending unless the caller spelled a direction token after the field.</summary>
    public SortDirection Direction { get; }

    internal static SortTerm Of(string field, SortDirection direction) => new(field, direction);
}
