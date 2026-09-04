# 0003 — Aggregate-oriented repository instead of a generic one

Status: Accepted

## Context

The template shipped a `BaseRepository<T>` with the usual generic surface —
`GetAllAsync`, `GetByIdAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, and an
`IQueryable<T>` accessor — implemented once and inherited by a repository per entity.
`TodoItem` and `Tag` each had their own repository, their own use cases and their own
controller, as if each were independently addressable.

Two defects followed directly from that design:

1. **It leaked `IQueryable<T>` to callers.** Query composition moved into the
   application layer, so EF Core's translation rules — and every change to them —
   became an application-layer concern. A query that worked became a runtime
   translation failure after a provider upgrade.
2. **It called `SaveChangesAsync` inside every method.** A use case that touched two
   things produced two independent transactions, with no way to roll the first back
   when the second failed.

And because items and tags were separately addressable, no single object could enforce a
rule spanning them. "Item titles are unique within a list" had nowhere to live.

## Decision

**One repository per aggregate root, with one method per thing a use case actually
needs.** `ITodoListRepository` has exactly three members:

```csharp
Task<TodoList?> GetAsync(Guid id, CancellationToken ct = default);
void Add(TodoList todoList);
void Remove(TodoList todoList);
```

No `IRepository<T>`, no `IQueryable` on the port, no `SaveChangesAsync` inside a
repository method. `GetAsync` loads the **complete** aggregate — list, items, tags.
Reads that do not need invariants go through the separate `ITodoListQueries` port.

## Consequences

- The port's surface is the aggregate's actual use, so a reviewer can see every way the
  write side touches the database by reading one interface.
- Invariants have exactly one home. `TodoList.AddItem` can check for a duplicate title
  and for the 500-item cap, because it has all the items in hand.
- Loading the whole aggregate on every write is a deliberate cost. It is also what makes
  `TodoList.MaxItems = 500` load-bearing rather than arbitrary: aggregate size is the
  hard bound on the cost of every single command.
- Every new aggregate needs its own port and implementation. There is no generic base to
  inherit, so there is a small amount of repetition per aggregate — three to five
  methods. That is the price, and it buys the two properties above.
- Because repositories only stage, the use case owns the commit (see
  [0004](0004-result-as-the-failure-channel.md) and `IUnitOfWork`). One use case, one
  transaction.
- The HTTP surface follows the aggregate boundary: `/api/v1/todo-lists/{id}/items/{itemId}`.
  There is deliberately no route that reaches an item without naming its list.

## Alternatives rejected

- **Generic `IRepository<T>`.** It can only offer what makes sense for every entity,
  which is CRUD — and CRUD is precisely what an aggregate exists to hide. It also
  invites a repository per entity, which is how items and tags became independently
  addressable in the first place.
- **`IQueryable<T>` on the port** (`ISpecification`-free variant). Convenient, and it
  moves provider semantics into the layer that is supposed to be provider-ignorant. The
  read-side port with SQL-projected DTOs gives the same flexibility with the
  translation risk kept in Infrastructure.
- **The specification pattern.** A real answer to the `IQueryable` leak, and a large
  amount of machinery. Two ports and explicit methods cover the sample's needs without
  a specification DSL to learn.
- **No repository at all — inject `DbContext` into use cases.** Honest, and used
  successfully by many teams. Rejected because it removes the seam that lets the
  application layer be tested without a database, and because it makes "who may call
  `SaveChanges`" unanswerable.

## Revisit when

An aggregate needs more than about six repository methods — that is a sign the aggregate
boundary is wrong, not that the repository needs a generic base class.
