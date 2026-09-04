# 0022 — No soft delete

Status: Accepted

## Context

`DeleteTodoList` and `RemoveTodoItem` remove rows. `TodoItemRecordConfiguration`
(`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/Configurations/TodoItemRecordConfiguration.cs`)
already configures `OnDelete(DeleteBehavior.Cascade)` from list to items, deliberately, so
a deleted list takes its items with it in one statement. The alternative on the table was
a soft-delete flag — `IsDeleted` or `DeletedAt` — with a restore endpoint, so that
"deleted" becomes a state rather than an absence.

## Decision

**`DELETE` removes rows. There is no soft-delete flag and no restore endpoint.**

## Consequences

- Soft delete puts an invisible predicate — "and it is not deleted" — on every read of
  every feature, forever, and the guarantee only holds if every query anybody ever writes
  includes it. One omission leaks a deleted row silently, and it leaks it to whoever was
  never supposed to see it again.
- This repository specifically has no single place to enforce that predicate once. Reads
  are hand-written projections per feature (`GetTodoListsUseCase`, `GetTodoListUseCase`,
  `GetTodoItemUseCase`), not one generic repository method every read funnels through —
  that is the point of [0003](0003-aggregate-oriented-repository.md) — so there is no
  choke point where a global filter could be applied once and proven complete.
- An EF Core global query filter would apply to the persistence records
  (`TodoListRecord`, `TodoItemRecord`), not to the domain. `TodoList`'s own invariants —
  unique item titles, the 500-item cap ([0003](0003-aggregate-oriented-repository.md)) —
  know nothing about a deleted flag, because
  [0011](0011-persistence-models-separate-from-the-domain.md) put exactly that kind of
  storage concern outside the domain on purpose. A "deleted" item would still occupy its
  title in the aggregate's uniqueness check, blocking a live item from reusing that name.
- There is deliberately no unique index on an item's `(TodoListId, Title)` — the domain's
  rule is case-insensitive and a B-tree unique index is not, so the constraint lives only
  in the aggregate. That makes the leak above worse rather than better: there is no
  database constraint to convert into a partial index that ignores deleted rows, so the
  only enforcement point is the very code that would have to start reasoning about a state
  it does not model.
- The existing cascade would also need rethinking: `OnDelete(DeleteBehavior.Cascade)` on a
  row that never truly disappears is a contradiction, so deleting a list would have to
  become an application-level walk over its items instead of one statement.

## Alternatives rejected

- **An `IsDeleted` flag with EF Core global query filters.** The filter lives on the
  persistence record, not on the aggregate, so it does not repair the invariant leak
  above, and it is exactly the "one omission leaks silently" risk described: a raw SQL
  query, a `Set<T>().IgnoreQueryFilters()` call, or a future read path that does not go
  through the filtered `DbSet` sees deleted rows again.
- **A `DeletedAt` timestamp instead of a boolean.** Same shape, same leak; a timestamp
  answers "when" but not "whether every reader checks it."
- **Move rows to an archive table inside the same transaction as the delete.** The
  closest defensible alternative — it keeps the live query shape honest, since a deleted
  row is actually gone from the tables every read queries. It was still not shipped
  because it needs a restore path, and a restore that has to re-run `TodoList`'s own
  invariants (does the title still not collide, is the list still under the item cap) is
  a feature in its own right, not a flag to toggle back.

## Revisit when

A user-visible "undo delete" is required, or a regulator requires retention of deleted
records. Either points at the archive-table approach above, built deliberately with its
own restore path and its own invariant re-checks — not at reintroducing a flag the read
side would have to honour everywhere.
