using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Domain.Features.TodoLists.ValueObjects;

namespace AppTemplate.Domain.Features.TodoLists.Entities;

/// <summary>
/// A to-do list together with its items and their tags. The only aggregate root in the
/// module, and therefore the only type with a repository: items and tags have no independent
/// existence, and making them separately addressable would leave no single object able to
/// enforce a rule that spans them.
/// </summary>
public sealed class TodoList : AggregateRoot<Guid>, IAuditable, IVersioned
{
    /// <summary>
    /// A write loads the whole aggregate, so this cap is the only bound on the cost of every
    /// command. 500 stays far above any hand-maintained list; a workload needing more wants a
    /// different aggregate boundary, not a bigger constant.
    /// </summary>
    public const int MaxItems = 500;

    private readonly List<TodoItem> _items = [];

    private TodoList(Guid id, Guid ownerId, TodoListName name) : base(id)
    {
        OwnerId = ownerId;
        Name = name;
    }

    /// <summary>
    /// Assigned at creation and never changed: every authorisation check in the application
    /// layer rests on ownership, so a setter would make those checks racy.
    /// </summary>
    public Guid OwnerId { get; private set; }

    public TodoListName Name { get; private set; }

    public IReadOnlyCollection<TodoItem> Items => _items.AsReadOnly();

    /// <summary>
    /// Optimistic concurrency token: an opaque value the store owns, replaced by the store on
    /// every write. It lives on the root only, because the root is the consistency boundary: a
    /// concurrent edit to any item is a conflict on the list.
    /// </summary>
    public uint Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? LastModifiedAt { get; private set; }

    public Guid? LastModifiedBy { get; private set; }

    /// <param name="now">Injected rather than read from the clock, so the aggregate has
    /// no ambient dependency and its behaviour is reproducible in a test.</param>
    public static TodoList Create(Guid ownerId, string name, DateTimeOffset now)
    {
        if (ownerId == Guid.Empty)
        {
            throw new DomainException("A to-do list must have an owner.");
        }

        var list = new TodoList(Guid.CreateVersion7(), ownerId, TodoListName.Create(name));
        list.RaiseDomainEvent(new TodoListCreatedDomainEvent(list.Id, ownerId, list.Name.Value, now));

        return list;
    }

    /// <summary>
    /// Rebuilds an aggregate that already exists in a store, from the values that were stored.
    /// <para>
    /// This is the seam a persistence layer needs when it keeps a persistence model of its own:
    /// something has to turn a row back into an aggregate. Declared here, the signature is checked
    /// by the compiler, so a new piece of state cannot be added to the aggregate without this
    /// method being visited — where reflection over private members would fail at runtime on a
    /// rename instead.
    /// </para>
    /// <para>
    /// It is not <see cref="Create"/>. It raises no domain event — nothing happened, a fact is being
    /// recalled — and it accepts the id rather than minting one. Every stored value still passes the
    /// checks a live aggregate applies: the name through <see cref="TodoListName"/>, and each item
    /// through the same gate <see cref="AddItem"/> uses, so a row that violates an invariant is
    /// refused on the way in rather than becoming an aggregate that cannot honour its own rules.
    /// </para>
    /// <para>
    /// The version and the audit values are deliberately absent from this signature: they belong to
    /// the store, and the store writes them through <see cref="IVersioned"/> and
    /// <see cref="IAuditable"/> — the same two interfaces that keep them out of reach of application
    /// code.
    /// </para>
    /// </summary>
    /// <param name="id">The stored identifier. Never generated here.</param>
    /// <param name="ownerId">The stored owner.</param>
    /// <param name="name">The stored name, re-validated.</param>
    /// <param name="items">The stored items, in any order, each re-checked against the item set.</param>
    public static TodoList Rehydrate(Guid id, Guid ownerId, string name, IEnumerable<TodoItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (id == Guid.Empty)
        {
            throw new DomainException("A stored to-do list must have an id.");
        }

        if (ownerId == Guid.Empty)
        {
            throw new DomainException("A to-do list must have an owner.");
        }

        var list = new TodoList(id, ownerId, TodoListName.Create(name));

        foreach (var item in items)
        {
            list.Accept(item);
        }

        return list;
    }

    public void Rename(string name) => Name = TodoListName.Create(name);

    /// <returns>The id of the item that was added.</returns>
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

    /// <param name="reopenedAt">Injected rather than read from the clock, for the same reason
    /// <see cref="CompleteItem"/> takes <c>completedAt</c>: the event's instant must be
    /// reproducible in a test, not stamped by an ambient <c>DateTime.UtcNow</c>.</param>
    public void ReopenItem(Guid itemId, DateTimeOffset reopenedAt)
    {
        var item = RequireItem(itemId);

        if (item.Reopen())
        {
            RaiseDomainEvent(new TodoItemReopenedDomainEvent(Id, item.Id, item.Title.Value, reopenedAt));
        }
    }

    /// <summary>Renames and/or redescribes an existing item in one step, so a caller does not
    /// need two round trips — and two version bumps — to change both fields.</summary>
    public void UpdateItem(Guid itemId, string title, string? description)
    {
        var item = RequireItem(itemId);
        EnsureTitleIsFree(title, itemId);
        item.ChangeTitle(title);
        item.ChangeDescription(description);
    }

    /// <summary>Total replacement, not a merge: the caller sends the tag set it wants the item
    /// to end up with, so this removes what is no longer present and adds what is new.</summary>
    public void SetItemTags(Guid itemId, IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var item = RequireItem(itemId);
        var wanted = tags.Select(Tag.Create).ToHashSet();

        foreach (var existing in item.Tags.Where(existing => !wanted.Contains(existing)).ToArray())
        {
            item.RemoveTag(existing);
        }

        foreach (var tag in wanted)
        {
            item.AddTag(tag);
        }
    }

    public void AddTagToItem(Guid itemId, string tag) => RequireItem(itemId).AddTag(Tag.Create(tag));

    public void RemoveTagFromItem(Guid itemId, string tag) => RequireItem(itemId).RemoveTag(Tag.Create(tag));

    void IAuditable.SetCreated(DateTimeOffset at, Guid? by)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    void IAuditable.SetLastModified(DateTimeOffset at, Guid? by)
    {
        LastModifiedAt = at;
        LastModifiedBy = by;
    }

    void IVersioned.SetVersion(uint version) => Version = version;

    /// <summary>
    /// The single gate into the item set, used by both <see cref="AddItem"/> and
    /// <see cref="Rehydrate"/>. A stored row and a fresh command are checked by the same code, so
    /// a load cannot assemble an aggregate that the same aggregate's own methods would refuse.
    /// </summary>
    private void Accept(TodoItem item)
    {
        // Not a DomainException: a null item is a broken call, not a caller driving the aggregate
        // into a forbidden state, and the two deserve different answers. Nothing a request can send
        // reaches this line — AddItem passes an item it just constructed, and Rehydrate's only
        // caller is TodoListMapper, which builds the list itself out of TodoItem.Rehydrate results.
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

    /// <summary>
    /// Case-insensitive: "Buy milk" and "buy milk" are the same entry to whoever reads them.
    /// <paramref name="exceptItemId"/> excludes the item being checked from the comparison, so
    /// renaming an item to the title it already has does not collide with itself.
    /// </summary>
    private void EnsureTitleIsFree(string title, Guid exceptItemId)
    {
        if (_items.Exists(existing =>
            existing.Id != exceptItemId &&
            string.Equals(existing.Title.Value, title, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"This list already contains an item titled '{title}'.");
        }
    }

    /// <summary>
    /// Every item operation goes through here, so an unknown id can never silently do nothing.
    /// Callers treating a missing item as an expected outcome look in <see cref="Items"/> first
    /// and return a "not found" result of their own.
    /// </summary>
    private TodoItem RequireItem(Guid itemId) =>
        _items.Find(i => i.Id == itemId)
        ?? throw new DomainException($"This list contains no item with id '{itemId}'.");
}
