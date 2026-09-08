using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Common.Tagging;

/// <summary>
/// The tags one thing carries, and the three rules that govern them: a tag is held once however
/// many times it is sent, a cap bounds how many one thing may carry, and a replacement is total
/// rather than a merge.
/// </summary>
/// <remarks>
/// <para>
/// Owned by an aggregate rather than exposed to callers: every method here is reached through the
/// aggregate that holds the set, which is what keeps the cap and the aggregate's own invariants
/// enforced together.
/// </para>
/// <para>
/// The cap and the subject are the aggregate's to choose, because how many tags a thing may carry
/// is a decision about that thing. The rules are not: they are the same for everything this
/// application tags, which is why they are written once here.
/// </para>
/// </remarks>
public sealed class TagSet
{
    private readonly List<Tag> _tags = [];
    private readonly string _subject;

    /// <param name="capacity">
    /// The most tags the owning aggregate will carry. A bound is required rather than advisable:
    /// without one a single request could send an unbounded collection into a per-tag loop that is
    /// linear in what is already held.
    /// </param>
    /// <param name="subject">
    /// How the owning aggregate names itself in a refusal — "to-do item", "stored file". It reaches
    /// a caller, so it reads as prose rather than as a type name.
    /// </param>
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

    /// <summary>
    /// Adds a tag, or does nothing if it is already held.
    /// </summary>
    /// <param name="tag">The tag to hold.</param>
    /// <exception cref="DomainException">
    /// The set is full and <paramref name="tag"/> is not already in it.
    /// </exception>
    /// <remarks>
    /// A tag already present is a no-op rather than an error: the caller's intent is already
    /// satisfied, so failing would force every client to read-then-write to stay correct and would
    /// make a retried request fail spuriously. The cap is checked only for a tag that is actually
    /// new, for the same reason — re-sending an existing tag stays a no-op on a full set.
    /// </remarks>
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

    /// <summary>
    /// Replaces everything held with <paramref name="wanted"/>: what is no longer there is removed,
    /// what is new is added.
    /// </summary>
    /// <param name="wanted">The set the caller wants to end up with.</param>
    /// <exception cref="DomainException"><paramref name="wanted"/> holds more tags than the cap allows.</exception>
    /// <remarks>
    /// Total replacement rather than a merge, and the removals happen first, so the cap applies to
    /// the result rather than to the union: swapping twenty tags for twenty others is not a request
    /// for forty. Duplicates within <paramref name="wanted"/> collapse, because <see cref="Tag"/>
    /// normalises before it compares.
    /// </remarks>
    public void Replace(IEnumerable<Tag> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        var replacement = wanted.ToHashSet();

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
