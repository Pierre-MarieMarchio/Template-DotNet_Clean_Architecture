# 0011 — EF Core maps persistence models, not the domain entities

Status: Accepted

## Context

EF Core previously mapped `TodoList`, `TodoItem` and `Tag` directly. It worked, and it was
less code. What it cost was paid by the domain model rather than by the schema:

- `TodoListName` had to be a **complex property** rather than a value converter, because a
  converter makes the property opaque to LINQ and the read side projects `Name.Value` into a
  DTO in SQL. A mapping constraint decided how a value object was modelled.
- `Tag` had to be an **owned collection**, and `Items` needed
  `UsePropertyAccessMode(PropertyAccessMode.Field)` because the aggregate exposes a
  read-only view over a private list.
- `TodoItem.IsCompleted` needed an explicit `Ignore`, because a derived property would
  otherwise become a second stored copy of the same fact.
- Both entities carried a private parameterless constructor whose only reason to exist was
  EF's materialiser, and `uint Version` — a PostgreSQL `xmin` mapping — sat on the aggregate
  as a public property.

Every one of those is the storage shape reaching into the model. None of them is wrong on its
own; together they mean the aggregate is answerable to two authorities, and the second one
never shows up in a domain unit test.

## Decision

**Every domain entity that is stored gets a persistence twin, and a mapper converts between
them. EF maps the twin.**

| Domain | Persistence model |
|---|---|
| `TodoList` | `TodoListRecord` |
| `TodoItem` | `TodoItemRecord` |
| `Tag` | `TodoItemTagRecord` |

Only the to-do list aggregate is affected. `AppUser`, `AppRole` and `RefreshToken` are
framework persistence types with no domain counterpart and need no mapper — a "domain user"
would be a second model of the same thing with no behaviour of its own to justify it.

Three mechanisms make up the cost of the split, and each replaces something EF used to do:

- **`ITodoListMapper`** — the translation. `ToAggregate` and `ToNewRecord` are total;
  `WriteTo` is deliberately partial, assigning onto a *tracked* row and never touching the
  audit columns or the concurrency token, which have exactly one writer each and it is not
  the mapper.
- **`ITodoListTracker`** — the identity map and the write list, per request. EF's change
  tracker can no longer be either, because it is not tracking the aggregate.
- **`IAggregateFlusher` + `AggregateFlushSaveChangesInterceptor`** — the moment the aggregate's
  state is written onto its rows, before EF computes its diff, and the moment the store's own
  values are read back afterwards. Registered **first** in the interceptor pipeline, because
  audit stamping only acts on an entry that is already `Added` or `Modified`.

The domain gains one seam: `TodoList.Rehydrate` / `TodoItem.Rehydrate`, plus `IVersioned`
alongside the existing `IAuditable`. Both are explicit, compile-checked and named — the
alternative was reflection over private members, which is the mechanism this template was
rescued from, because a renamed property then fails at runtime instead of at compile time.

## Consequences

- **The domain model answers only to its own rules.** `AppTemplate.Domain` has no EF concepts left in
  it: no complex property, no owned collection, no ignored member, no field-access mode. The
  private parameterless constructors are now only used by `Rehydrate` in the same file.
- **The read side got simpler.** A projection reads ordinary columns instead of reaching
  through a value object, so it no longer depends on that value object staying expressible in
  LINQ.
- **Change detection is manual, and it is the main new risk.** It is handled by mapping onto
  the tracked rows and letting EF diff — never by rebuilding the row graph, which is exactly
  the defect this template was rescued from (an unconditional full-row `UPDATE` that flattened
  the audit columns). Child collections are reconciled by id: added, removed, or assigned onto
  in place, so an item nobody touched produces no statement.
- **Domain events had to move house.** They used to be drained from
  `ChangeTracker.Entries<IAggregateRoot>()`; no aggregate is tracked now, so that walk would
  find nothing, publish nothing and fail *silently*. They are drained from the feature's
  tracker through `IDomainEventSource` instead.
- **The concurrency token has to be carried by hand.** It travels row → aggregate on load,
  and the aggregate's version is pushed into the row's *original* value before a save, which
  is what EF puts in the `WHERE` clause. After a successful save the new token is read back,
  so a second write in the same request does not fail against a version it moved itself.
- **The mapper is a silent-data-loss risk, and it is the one thing here that no compiler
  checks.** A forgotten property throws nothing: the value simply comes back as its default.
  `TodoListMapperFidelityTests` enumerates the aggregate's state by reflection and fails when
  a property does not survive aggregate → record → aggregate, when the sample leaves a
  property at its type's default (so the comparison would be vacuous), or when an exclusion
  names a member that no longer exists. A deliberately forgetful mapper is run through the
  same harness to prove the harness can fail.
- **More code, and more of it in the least interesting place.** Three record types, three
  configurations, a mapper, a tracker, a flush interceptor and two test files, in exchange for
  a domain model with no persistence concerns in it. This is a real trade and the extra volume
  is real.
- **The identity boundary is now structural.** A `TodoList` cannot acquire a navigation
  property to `AppUser` even by accident, because the two are not in the same model and
  `AppTemplate.Domain` references neither EF Core nor ASP.NET Identity.

## Alternatives rejected

- **Map the domain entities directly** (what was there). Less code, and the storage shape
  keeps a vote on how the model is written. Reasonable for a project where the schema and the
  model will always agree; this template exists to show the other choice done properly.
- **A mapping library** (AutoMapper and friends). Configuration by convention makes the
  forgotten property *harder* to see, not easier: the failure mode moves from a missing line
  into a naming rule, and the reflection-driven fidelity test becomes much harder to write
  because there is no single method to hold responsible.
- **Source-generated mappers** (Mapperly and similar). Genuinely good at the total mappings
  and no help at all with the partial one — `WriteTo` exists precisely to *not* copy four
  columns, and reconciliation by id is not a shape a generator expresses.
- **Reflection over private setters.** Shortest path, and it fails at runtime on a rename.
  The whole point of `Rehydrate` is that the compiler visits it.
- **A domain model with public setters, so no seam is needed.** Gives up the encapsulation
  that made the aggregate worth having.

## Revisit when

The persistence model and the domain model have been identical, field for field, through
several schema changes in a row. That is evidence the second model is only ceremony, and the
mapper can be deleted in favour of direct mapping — with the fidelity test's failure history
as the record of what it caught while it existed.
