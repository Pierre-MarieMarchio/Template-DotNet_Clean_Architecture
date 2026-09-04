namespace AppTemplate.Application.Common.Collections;

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

    public string Field { get; }

    public SortDirection Direction { get; }

    internal static SortTerm Of(string field, SortDirection direction) => new(field, direction);
}
