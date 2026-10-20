namespace AppTemplate.Application.Core.Common.Collections;

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
