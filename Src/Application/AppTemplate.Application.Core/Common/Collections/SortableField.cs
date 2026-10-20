namespace AppTemplate.Application.Core.Common.Collections;

public sealed record SortableField
{
    private SortableField(string name, bool supportsKeyset)
    {
        Name = name;
        SupportsKeyset = supportsKeyset;
    }

    public string Name { get; }

    public bool SupportsKeyset { get; }

    public static SortableField Keyset(string name) => new(name, supportsKeyset: true);

    public static SortableField OffsetOnly(string name) => new(name, supportsKeyset: false);
}
