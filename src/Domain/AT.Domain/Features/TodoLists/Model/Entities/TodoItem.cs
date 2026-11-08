using AppTemplate.Domain.Common.Tagging;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.ValueObjects;

namespace AppTemplate.Domain.Features.TodoLists.Model.Entities;

public sealed class TodoItem : Entity<Guid>
{
    public const int MaxDescriptionLength = 2000;

    public const int MaxTags = 20;

    private readonly TagSet _tags = new(MaxTags, "to-do item");

    internal TodoItem(Guid id, Guid todoListId, string title, string? description) : base(id)
    {
        TodoListId = todoListId;
        Title = TodoItemTitle.Create(title);
        Description = NormaliseDescription(description);
    }

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

    public Guid TodoListId { get; private set; }

    public TodoItemTitle Title { get; private set; }

    public string? Description { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public bool IsCompleted => CompletedAt is not null;

    public IReadOnlyCollection<Tag> Tags => _tags.Tags;

    internal void Update(TodoItemTitle title, string? description)
    {
        string? normalisedDescription = NormaliseDescription(description);
        Title = title;
        Description = normalisedDescription;
    }

    internal void Complete(DateTimeOffset completedAt)
    {
        if (IsCompleted)
        {
            throw new DomainException($"Item '{Title}' is already completed.");
        }

        if (completedAt == default)
        {
            throw new DomainException("A completion instant must be a real instant.");
        }

        CompletedAt = completedAt;
    }

    internal bool Reopen()
    {
        if (!IsCompleted)
        {
            return false;
        }

        CompletedAt = null;
        return true;
    }

    internal void AddTag(Tag tag) => _tags.Add(tag);

    internal void RemoveTag(Tag tag) => _tags.Remove(tag);

    internal void ReplaceTags(IEnumerable<Tag> tags) => _tags.Replace(tags);

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
