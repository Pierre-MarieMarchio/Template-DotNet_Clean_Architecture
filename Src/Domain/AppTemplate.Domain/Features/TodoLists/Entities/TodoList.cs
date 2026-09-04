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
    /// This is the seam a persistence layer needs once it stops mapping the domain type itself and
    /// keeps a persistence model of its own: something has to turn a row back into an aggregate, and
    /// the alternative is reflection over private members — which is precisely the mechanism this
    /// template was rescued from, because a renamed property then fails at runtime instead of at
    /// compile time. Declared here, the signature is checked by the compiler and a new piece of
    /// state cannot be added to the aggregate without this method being visited.
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

    public void ReopenItem(Guid itemId) => RequireItem(itemId).Reopen();

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
        if (item is null)
        {
            throw new DomainException("A to-do list cannot hold a null item.");
        }

        if (item.TodoListId != Id)
        {
            throw new DomainException(
                $"Item '{item.Id}' belongs to list '{item.TodoListId}', not to list '{Id}'.");
        }

        // Case-insensitive: "Buy milk" and "buy milk" are the same entry to whoever reads them.
        // The titles are already trimmed, because they come from a TodoItemTitle.
        if (_items.Exists(
            existing => string.Equals(existing.Title.Value, item.Title.Value, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"This list already contains an item titled '{item.Title.Value}'.");
        }

        if (_items.Count >= MaxItems)
        {
            throw new DomainException($"A to-do list cannot hold more than {MaxItems} items.");
        }

        _items.Add(item);
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
