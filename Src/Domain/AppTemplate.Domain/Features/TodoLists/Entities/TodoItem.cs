using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;

namespace AppTemplate.Domain.Features.TodoLists.Entities;

/// <summary>
/// A single entry inside a <see cref="TodoList"/>. An entity but <b>not</b> an aggregate
/// root: it has no repository and is only reachable through its list. Every mutator is
/// <c>internal</c> so the root stays the only thing able to change it, which is what makes
/// the list-level invariants (unique titles, item cap) enforceable.
/// </summary>
public sealed class TodoItem : Entity<Guid>
{
    public const int MaxDescriptionLength = 2000;

    /// <summary>
    /// Bounds tag growth per item. Without it a single request could send an unbounded collection
    /// into a per-tag loop that is linear in the item's existing tags.
    /// </summary>
    public const int MaxTags = 20;

    private readonly List<Tag> _tags = [];

    internal TodoItem(Guid id, Guid todoListId, string title, string? description) : base(id)
    {
        TodoListId = todoListId;
        Title = TodoItemTitle.Create(title);
        Description = NormaliseDescription(description);
    }

    /// <summary>
    /// Rebuilds a stored item. Called only by <see cref="TodoList.Rehydrate"/>'s caller — the
    /// persistence layer — which is why it is <c>public</c> where every mutator is <c>internal</c>:
    /// reconstitution comes from outside the assembly, whereas change never does.
    /// <para>
    /// Every value goes through the same normalisation and the same value object a live item would,
    /// so a row that no longer satisfies an invariant is refused here rather than silently becoming
    /// an aggregate that cannot honour its own rules.
    /// </para>
    /// </summary>
    public static TodoItem Rehydrate(
        Guid id,
        Guid todoListId,
        string title,
        string? description,
        DateTimeOffset? completedAt,
        IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        if (id == Guid.Empty)
        {
            throw new DomainException("A stored to-do item must have an id.");
        }

        if (todoListId == Guid.Empty)
        {
            throw new DomainException("A stored to-do item must belong to a list.");
        }

        var item = new TodoItem(id, todoListId, title, description)
        {
            CompletedAt = completedAt,
        };

        foreach (string tag in tags)
        {
            item.AddTag(Tag.Create(tag));
        }

        return item;
    }

    /// <summary>A plain foreign key rather than a back-reference navigation, so the object
    /// graph stays a tree and cannot be cyclic.</summary>
    public Guid TodoListId { get; private set; }

    public TodoItemTitle Title { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Derived from <see cref="CompletedAt"/>, so the two can never disagree.</summary>
    public bool IsCompleted => CompletedAt is not null;

    public IReadOnlyCollection<Tag> Tags => _tags.AsReadOnly();

    internal void ChangeTitle(string title) => Title = TodoItemTitle.Create(title);

    internal void ChangeDescription(string? description) => Description = NormaliseDescription(description);

    internal void Complete(DateTimeOffset completedAt)
    {
        if (IsCompleted)
        {
            throw new DomainException($"Item '{Title}' is already completed.");
        }

        // The default instant is what an uninitialised caller passes, and accepting it would make
        // the item completed at 0001-01-01 — indistinguishable from a real value to every reader.
        if (completedAt == default)
        {
            throw new DomainException("A completion instant must be a real instant.");
        }

        CompletedAt = completedAt;
    }

    /// <summary>
    /// Idempotent, unlike <see cref="Complete"/>: reopening an already-open item is not a
    /// mistake worth rejecting, whereas completing a completed one usually signals a stale
    /// client racing another completion.
    /// </summary>
    /// <returns><see langword="true"/> if the item was completed and is now reopened.</returns>
    internal bool Reopen()
    {
        if (!IsCompleted)
        {
            return false;
        }

        CompletedAt = null;
        return true;
    }

    /// <summary>
    /// Adding a tag that is already present is a no-op rather than an error: the caller's
    /// intent is already satisfied, so failing would force every client to read-then-write to
    /// stay correct and would make a retried request fail spuriously.
    /// </summary>
    internal void AddTag(Tag tag)
    {
        if (_tags.Contains(tag))
        {
            return;
        }

        // The cap is checked only for a tag that is actually new, so re-sending an existing tag
        // stays a no-op on a full item rather than becoming a spurious failure.
        if (_tags.Count >= MaxTags)
        {
            throw new DomainException($"A to-do item cannot carry more than {MaxTags} tags.");
        }

        _tags.Add(tag);
    }

    /// <summary>Removing an absent tag is a no-op, for the same reason as <see cref="AddTag"/>.</summary>
    internal void RemoveTag(Tag tag) => _tags.Remove(tag);

    private static string? NormaliseDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        string trimmed = description.Trim();

        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new DomainException($"A to-do item description cannot exceed {MaxDescriptionLength} characters.");
        }

        return trimmed;
    }
}
