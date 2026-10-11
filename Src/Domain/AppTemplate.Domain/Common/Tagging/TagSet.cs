using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Common.Tagging;

/// <summary>The tags one thing carries, under three rules: a tag is held once however often it is
/// sent, a cap bounds how many, and a replacement is total rather than a merge.</summary>
/// <remarks>Reached only through the owning aggregate, so the cap and its invariants are enforced
/// together; the cap and the subject are the aggregate's to choose, the rules are shared.</remarks>
public sealed class TagSet
{
    private readonly List<Tag> _tags = [];
    private readonly string _subject;

    /// <param name="capacity">The most tags the owning aggregate will carry; a bound, not advice —
    /// without one a request could feed an unbounded collection into a per-tag loop.</param>
    /// <param name="subject">How the owning aggregate names itself in a refusal — "to-do item";
    /// it reaches a caller, so it reads as prose, not as a type name.</param>
    public TagSet(int capacity, string subject)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        Capacity = capacity;
        _subject = subject;
    }

    /// <summary>The most tags this set will hold.</summary>
    public int Capacity { get; }

    /// <summary>What is held, in the order it was added.</summary>
    public IReadOnlyCollection<Tag> Tags => _tags.AsReadOnly();

    /// <summary>Adds a tag, or does nothing if it is already held.</summary>
    /// <param name="tag">The tag to hold.</param>
    /// <exception cref="DomainException">The set is full and <paramref name="tag"/> is not already in it.</exception>
    /// <remarks>A duplicate is a no-op rather than an error, so a retried request cannot fail
    /// spuriously; the cap is checked only for a tag that is actually new, for the same reason.</remarks>
    public void Add(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (_tags.Contains(tag))
        {
            return;
        }

        if (_tags.Count >= Capacity)
        {
            throw new DomainException($"A {_subject} cannot carry more than {Capacity} tags.");
        }

        _tags.Add(tag);
    }

    /// <summary>Removes a tag, or does nothing if it is absent, for the same reason as <see cref="Add"/>.</summary>
    /// <param name="tag">The tag to let go of.</param>
    public void Remove(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        _tags.Remove(tag);
    }

    /// <summary>Replaces everything held with <paramref name="wanted"/>: what is no longer there is
    /// removed, what is new is added.</summary>
    /// <param name="wanted">The set the caller wants to end up with.</param>
    /// <exception cref="DomainException"><paramref name="wanted"/> holds more tags than the cap allows.</exception>
    /// <remarks>Total replacement, not a merge, and removals happen first, so the cap applies to the
    /// result and not the union: twenty tags swapped for twenty others is not a request for forty.
    /// Duplicates collapse, because <see cref="Tag"/> normalises before it compares.</remarks>
    public void Replace(IEnumerable<Tag> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        var replacement = wanted.ToHashSet();

        // Checked before anything is removed, so a refused replacement leaves the set as it was.
        if (replacement.Count > Capacity)
        {
            throw new DomainException($"A {_subject} cannot carry more than {Capacity} tags.");
        }

        foreach (var gone in _tags.Where(held => !replacement.Contains(held)).ToArray())
        {
            _tags.Remove(gone);
        }

        foreach (var tag in replacement)
        {
            Add(tag);
        }
    }
}
