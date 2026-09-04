using AppTemplate.Application.Common.Policies;
namespace AppTemplate.Application.Common.Collections;

/// <summary>
/// One entry of a feature's sortable whitelist: the name a caller may send, and whether a keyset
/// cursor may be built over it.
/// </summary>
/// <remarks>
/// There is no public constructor, but the two factories are public, and the distinction matters.
/// A <see cref="SortableField"/> is a <em>declaration</em>, not caller input: what makes the
/// whitelist safe is that the <see cref="ICollectionPolicy"/> a use case hands to
/// <see cref="SortOrder.Parse"/> is fixed at compile time, so building one of these grants nothing
/// to anybody. Keeping the factories reachable is what lets a fitness test declare a policy naming
/// a field no query translator handles, and so prove that the translator's <c>default</c> arm
/// really does throw — a guard nothing can construct a case for is a guard nobody has checked.
/// </remarks>
public sealed record SortableField
{
    private SortableField(string name, bool supportsKeyset)
    {
        Name = name;
        SupportsKeyset = supportsKeyset;
    }

    /// <summary>The caller-facing name, and the canonical spelling every layer switches on.</summary>
    public string Name { get; }

    /// <summary>
    /// Whether <c>paging=cursor</c> may order by this field. A field whose column is nullable must
    /// say <c>false</c>: a keyset comparison against <c>NULL</c> is neither true nor false, so the
    /// row that produced the cursor would be skipped rather than resumed from.
    /// </summary>
    public bool SupportsKeyset { get; }

    /// <summary>A field a caller may sort by and resume a keyset page from.</summary>
    public static SortableField Keyset(string name) => new(name, supportsKeyset: true);

    /// <summary>A field a caller may sort by under offset paging only.</summary>
    public static SortableField OffsetOnly(string name) => new(name, supportsKeyset: false);
}
