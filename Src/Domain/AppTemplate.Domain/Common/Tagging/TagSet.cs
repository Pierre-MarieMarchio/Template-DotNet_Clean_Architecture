using AppTemplate.Domain.Core.Common.Exceptions;

namespace AppTemplate.Domain.Common.Tagging;

public sealed class TagSet
{
    private readonly List<Tag> _tags = [];
    private readonly string _subject;

    public TagSet(int capacity, string subject)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        Capacity = capacity;
        _subject = subject;
    }

    public int Capacity { get; }

    public IReadOnlyCollection<Tag> Tags => _tags.AsReadOnly();

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

    public void Remove(Tag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        _tags.Remove(tag);
    }

    public void Replace(IEnumerable<Tag> wanted)
    {
        ArgumentNullException.ThrowIfNull(wanted);

        var replacement = wanted.ToHashSet();

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
