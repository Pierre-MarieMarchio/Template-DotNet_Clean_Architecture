using AppTemplate.Domain.Common.Tagging;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Events;
using AppTemplate.Domain.Features.TodoLists.Model.ValueObjects;

namespace AppTemplate.Domain.Features.TodoLists.Model.Entities;

public sealed class TodoList : AuditableAggregateRoot<Guid>, IOwnedAggregate
{
    public const int MaxItems = 500;

    private readonly List<TodoItem> _items = [];

    private TodoList(Guid id, UserId ownerId, TodoListName name) : base(id)
    {
        OwnerId = ownerId;
        Name = name;
    }

    public UserId OwnerId { get; private set; }

    public TodoListName Name { get; private set; }

    public IReadOnlyCollection<TodoItem> Items => _items.AsReadOnly();

    public static TodoList Create(UserId ownerId, string name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ownerId);

        var list = new TodoList(Guid.CreateVersion7(), ownerId, TodoListName.Create(name));
        list.RaiseDomainEvent(new TodoListCreatedDomainEvent(list.Id, ownerId, list.Name.Value, now));

        return list;
    }

    public static TodoList Rehydrate(Guid id, UserId ownerId, string name, IEnumerable<TodoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(ownerId);

        if (id == Guid.Empty)
        {
            throw new DomainException("A stored to-do list must have an id.");
        }

        var list = new TodoList(id, ownerId, TodoListName.Create(name));

        foreach (var item in items)
        {
            list.Accept(item);
        }

        return list;
    }

    public void Rename(string name) => Name = TodoListName.Create(name);

    public Guid AddItem(string title, string? description)
    {
        var item = new TodoItem(Guid.CreateVersion7(), Id, title, description);
        Accept(item);

        return item.Id;
    }

    public void RemoveItem(Guid itemId) => _items.Remove(RequireItem(itemId));

    public void CompleteItem(Guid itemId, DateTimeOffset completedAt)
    {
        var item = RequireItem(itemId);
        item.Complete(completedAt);
        RaiseDomainEvent(new TodoItemCompletedDomainEvent(Id, item.Id, item.Title.Value, completedAt));
    }

    public void ReopenItem(Guid itemId, DateTimeOffset reopenedAt)
    {
        var item = RequireItem(itemId);

        if (item.Reopen())
        {
            RaiseDomainEvent(new TodoItemReopenedDomainEvent(Id, item.Id, item.Title.Value, reopenedAt));
        }
    }

    public void UpdateItem(Guid itemId, string title, string? description)
    {
        var item = RequireItem(itemId);
        var newTitle = TodoItemTitle.Create(title);
        EnsureTitleIsFree(newTitle.Value, itemId);
        item.Update(newTitle, description);
    }

    public void SetItemTags(Guid itemId, IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        RequireItem(itemId).ReplaceTags(tags.Select(Tag.Create));
    }

    public void AddTagToItem(Guid itemId, string tag) => RequireItem(itemId).AddTag(Tag.Create(tag));

    public void RemoveTagFromItem(Guid itemId, string tag) => RequireItem(itemId).RemoveTag(Tag.Create(tag));

    private void Accept(TodoItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.TodoListId != Id)
        {
            throw new DomainException(
                $"Item '{item.Id}' belongs to list '{item.TodoListId}', not to list '{Id}'.");
        }

        EnsureTitleIsFree(item.Title.Value, item.Id);

        if (_items.Count >= MaxItems)
        {
            throw new DomainException($"A to-do list cannot hold more than {MaxItems} items.");
        }

        _items.Add(item);
    }

    private void EnsureTitleIsFree(string title, Guid exceptItemId)
    {
        if (_items.Exists(existing =>
            existing.Id != exceptItemId &&
            string.Equals(existing.Title.Value, title, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"This list already contains an item titled '{title}'.");
        }
    }

    private TodoItem RequireItem(Guid itemId) =>
        _items.Find(i => i.Id == itemId)
        ?? throw new DomainException($"This list contains no item with id '{itemId}'.");
}
