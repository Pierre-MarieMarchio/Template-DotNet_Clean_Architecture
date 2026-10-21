# 0005. The domain is not mapped by EF

Status: Accepted

## Context

EF Core can map domain types directly, and most projects let it. The price is paid by the domain:
constructors and setters shaped for the materialiser, navigation properties where the model wants
none, and a storage optimisation that cannot be made without changing the model. A storage
representation may legitimately differ from the domain, and an aggregate should answer to its own
rules only.

## Decision

EF maps row records, never domain types. An aggregate is persisted through three pieces:

- **Rows.** One record per table, settable properties, no invariants.
- **A mapper.** Total from rows to the aggregate. Onto a tracked row it writes the domain's columns
  only and leaves the version and the audit stamps alone, so EF's own diff decides what is written.
- **A tracker.** One object per request, registered under every contract that needs it: the
  identity map, the list of aggregates to write, the source of their events.

Three `SaveChanges` interceptors run in this order: the flush writes each tracked aggregate onto its
rows, the audit interceptor stamps what that wrote, and the dispatcher publishes events after the
commit. Because they are interceptors rather than steps of the unit of work, any `SaveChanges`,
the synchronous overload included, flushes and publishes.

Rehydration goes through the aggregate's own factories and checks structure only: a state and its
dates agree, children belong to their root. Policy limits such as caps and lengths apply to new
writes, so lowering one never makes stored data unloadable.

## Consequences

- The domain references nothing outside itself, which `LayerDependencyTests` holds.
- A mapper that forgets a property loses data without failing anything. The barrier is a fidelity
  test per aggregate that walks the aggregate's state by reflection and round-trips it; the
  generator writes one for every `--model aggregate` feature.
<!--#if (todoLists)-->
  `TodoListMapperFidelityTests` is this project's example.
<!--#endif-->
- An aggregate costs a record, a mapper and a tracker more than a mapped entity; the generator writes
  them. A feature with no state to protect has none of the three
  ([ADR 0002](0002-two-feature-shapes.md)).
- No code may save around the interceptors, and none needs to.

[The save pipeline](../architecture/persistence-model.md#the-save-pipeline) describes the order and
what each step may assume.

## Revisit when

EF can map the aggregates without shaping them, or the mapping cost is felt on every change while
the storage and domain shapes never actually diverge.
